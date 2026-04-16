using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.Assistant.Utils;
using Unity.AI.MCP.Editor.Lens;
using Unity.AI.MCP.Editor.ToolRegistry;
using Unity.AI.MCP.Editor.Tools.Parameters;

namespace Unity.AI.MCP.Editor.Tools
{
    /// <summary>
    /// Handles CRUD operations for shader files within the Unity project.
    /// </summary>
    public static class ManageShader
    {
        /// <summary>
        /// Description of the Unity.ManageShader tool for MCP clients.
        /// Provides information about shader CRUD operations including create, read, update, and delete.
        /// </summary>
        public const string Title = "Manage shaders";

        public const string Description = @"Manages shader scripts in Unity (create, read, update, delete).

Args:
    Action: Operation ('Create', 'Read', 'Update', 'Delete').
    Name: Shader name (no .cs extension).
    Path: Asset path (default: ""Assets/"").
    Contents: Shader code for 'create'/'update'.

Returns:
    Dictionary with results ('success', 'message', 'data').";
        /// <summary>
        /// Returns the output schema for this tool.
        /// </summary>
        /// <returns>The JSON schema object describing the tool's output structure.</returns>
        [McpOutputSchema("Unity.ManageShader")]
        public static object GetOutputSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    success = new { type = "boolean", description = "Whether the operation succeeded" },
                    message = new { type = "string", description = "Human-readable message about the operation" },
                    data = new
                    {
                        type = "object",
                        description = "Shader-specific operation data",
                        properties = new
                        {
                            name = new { type = "string", description = "Shader name" },
                            path = new { type = "string", description = "Relative path to shader file" },
                            contents = new { type = "string", description = "Shader source code (for read operations)" },
                            encodedContents = new { type = "string", description = "Base64-encoded shader contents when the legacy full-inline path is used" },
                            sha256 = new { type = "string", description = "SHA-256 of the full shader contents" },
                            length_bytes = new { type = "integer", description = "Full shader length in bytes" },
                            truncated = new { type = "boolean", description = "Whether the inline contents were truncated to a preview" },
                            detailAvailable = new { type = "boolean", description = "Whether full contents can be resolved through Unity.ReadDetailRef" },
                            detailRef = new { type = "object", description = "Stored detail ref for the full shader contents when compacted" }
                        }
                    }
                },
                required = new[] { "success", "message" }
            };
        }

        /// <summary>
        /// Main handler for shader management actions.
        /// </summary>
        /// <param name="parameters">The parameters specifying the shader action and related settings.</param>
        /// <returns>A response object containing success status, message, and optional data.</returns>
        [McpTool("Unity.ManageShader", Description, Title, Groups = new string[] { "assets", "scripting" })]
        public static object HandleCommand(ManageShaderParams parameters)
        {
            var @params = parameters;

            // Extract parameters
            string action = @params.Action.ToString().ToLower();
            string name = @params.Name;
            string path = @params.Path; // Relative to Assets/
            string contents = null;

            // Check if we have base64 encoded contents
            bool contentsEncoded = @params.ContentsEncoded;
            if (contentsEncoded && !string.IsNullOrEmpty(@params.EncodedContents))
            {
                try
                {
                    contents = DecodeBase64(@params.EncodedContents);
                }
                catch (Exception e)
                {
                    return Response.Error($"Failed to decode shader contents: {e.Message}");
                }
            }
            else
            {
                contents = @params.Contents;
            }

            // Validate required parameters
            if (string.IsNullOrEmpty(name))
            {
                return Response.Error("Name parameter is required.");
            }
            // Basic name validation (alphanumeric, underscores, cannot start with number)
            if (!Regex.IsMatch(name, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
            {
                return Response.Error(
                    $"Invalid shader name: '{name}'. Use only letters, numbers, underscores, and don't start with a number."
                );
            }

            // Ensure path is relative to Assets/, removing any leading "Assets/"
            // Set default directory to "Shaders" if path is not provided
            string relativeDir = path ?? "Shaders"; // Default to "Shaders" if path is null
            if (!string.IsNullOrEmpty(relativeDir))
            {
                relativeDir = relativeDir.Replace('\\', '/').Trim('/');
                if (relativeDir.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    relativeDir = relativeDir.Substring("Assets/".Length).TrimStart('/');
                }
            }
            // Handle empty string case explicitly after processing
            if (string.IsNullOrEmpty(relativeDir))
            {
                relativeDir = "Shaders"; // Ensure default if path was provided as "" or only "/" or "Assets/"
            }

            // Construct paths
            string shaderFileName = $"{name}.shader";
            string fullPathDir = Path.Combine(Application.dataPath, relativeDir);
            string fullPath = Path.Combine(fullPathDir, shaderFileName);
            string relativePath = Path.Combine("Assets", relativeDir, shaderFileName)
                .Replace('\\', '/'); // Ensure "Assets/" prefix and forward slashes

            // Ensure the target directory exists for create/update
            if (action == "create" || action == "update")
            {
                try
                {
                    if (!Directory.Exists(fullPathDir))
                    {
                        Directory.CreateDirectory(fullPathDir);
                        // Refresh AssetDatabase to recognize new folders
                        AssetDatabase.Refresh();
                    }
                }
                catch (Exception e)
                {
                    return Response.Error(
                        $"Could not create directory '{fullPathDir}': {e.Message}"
                    );
                }
            }

            // Route to specific action handlers
            switch (@params.Action)
            {
                case ShaderAction.Create:
                    return CreateShader(fullPath, relativePath, name, contents);
                case ShaderAction.Read:
                    return ReadShader(fullPath, relativePath);
                case ShaderAction.Update:
                    return UpdateShader(fullPath, relativePath, name, contents);
                case ShaderAction.Delete:
                    return DeleteShader(fullPath, relativePath);
                default:
                    return Response.Error(
                        $"Unknown action: '{@params.Action}'. Valid actions are: Create, Read, Update, Delete."
                    );
            }
        }

        /// <summary>
        /// Decode base64 string to normal text
        /// </summary>
        static string DecodeBase64(string encoded)
        {
            byte[] data = Convert.FromBase64String(encoded);
            return System.Text.Encoding.UTF8.GetString(data);
        }

        /// <summary>
        /// Encode text to base64 string
        /// </summary>
        static string EncodeBase64(string text)
        {
            byte[] data = System.Text.Encoding.UTF8.GetBytes(text);
            return Convert.ToBase64String(data);
        }

        static object CreateShader(
            string fullPath,
            string relativePath,
            string name,
            string contents
        )
        {
            // Check if shader already exists
            if (File.Exists(fullPath))
            {
                return Response.Error(
                    $"Shader already exists at '{relativePath}'. Use 'update' action to modify."
                );
            }

            // Add validation for shader name conflicts in Unity
            if (Shader.Find(name) != null)
            {
                return Response.Error(
                    $"A shader with name '{name}' already exists in the project. Choose a different name."
                );
            }

            // Generate default content if none provided
            if (string.IsNullOrEmpty(contents))
            {
                contents = GenerateDefaultShaderContent(name);
            }

            try
            {
                File.WriteAllText(fullPath, contents, new System.Text.UTF8Encoding(false));
                AssetDatabase.ImportAsset(relativePath);
                AssetDatabase.Refresh(); // Ensure Unity recognizes the new shader

                return Response.Success(
                    $"Shader '{name}.shader' created successfully at '{relativePath}'.",
                    new { name = name, path = relativePath }
                );
            }
            catch (Exception e)
            {
                return Response.Error($"Failed to create shader '{relativePath}': {e.Message}");
            }
        }

        static object ReadShader(string fullPath, string relativePath)
        {
            if (!File.Exists(fullPath))
            {
                return Response.Error($"Shader not found at '{relativePath}'.");
            }

            try
            {
                string contents = File.ReadAllText(fullPath);
                var budgeted = ToolResultCompactor.ShapeTextPayload(
                    "Unity.ManageShader.read",
                    $"Read shader '{Path.GetFileName(relativePath)}' successfully.",
                    contents,
                    detailRefMeta: new
                    {
                        tool = "Unity.ManageShader.read",
                        path = relativePath
                    },
                    maxPreviewLines: PayloadBudgetPolicy.MaxPreviewFileLines,
                    maxPreviewBytes: PayloadBudgetPolicy.MaxPreviewFileBytes,
                    requireDetailRefForCompaction: true);

                // Return both normal and encoded contents for larger files
                //TODO: Consider a threshold for large files
                bool isLarge = contents.Length > 10000; // If content is large, include encoded version

                var responseData = new
                {
                    name = Path.GetFileNameWithoutExtension(relativePath),
                    path = relativePath,
                    contents = budgeted.Preview,
                    encodedContents = budgeted.DetailAvailable ? null : isLarge ? EncodeBase64(contents) : null,
                    sha256 = budgeted.Sha256,
                    length_bytes = budgeted.Bytes,
                    truncated = budgeted.Truncated,
                    detailAvailable = budgeted.DetailAvailable,
                    detailRef = budgeted.DetailRef
                };

                return Response.Success(
                    $"Shader '{Path.GetFileName(relativePath)}' read successfully.",
                    responseData
                );
            }
            catch (Exception e)
            {
                return Response.Error($"Failed to read shader '{relativePath}': {e.Message}");
            }
        }

        static object UpdateShader(
            string fullPath,
            string relativePath,
            string name,
            string contents
        )
        {
            if (!File.Exists(fullPath))
            {
                return Response.Error(
                    $"Shader not found at '{relativePath}'. Use 'create' action to add a new shader."
                );
            }
            if (string.IsNullOrEmpty(contents))
            {
                return Response.Error("Content is required for the 'update' action.");
            }

            try
            {
                File.WriteAllText(fullPath, contents, new System.Text.UTF8Encoding(false));
                AssetDatabase.ImportAsset(relativePath);
                AssetDatabase.Refresh();

                return Response.Success(
                    $"Shader '{Path.GetFileName(relativePath)}' updated successfully.",
                    new { name = Path.GetFileNameWithoutExtension(relativePath), path = relativePath }
                );
            }
            catch (Exception e)
            {
                return Response.Error($"Failed to update shader '{relativePath}': {e.Message}");
            }
        }

        static object DeleteShader(string fullPath, string relativePath)
        {
            if (!File.Exists(fullPath))
            {
                return Response.Error($"Shader not found at '{relativePath}'.");
            }

            try
            {
                // Delete the asset through Unity's AssetDatabase first
                bool success = AssetDatabase.DeleteAsset(relativePath);
                if (!success)
                {
                    return Response.Error($"Failed to delete shader through Unity's AssetDatabase: '{relativePath}'");
                }

                // If the file still exists (rare case), try direct deletion
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }

                return Response.Success(
                    $"Shader '{Path.GetFileName(relativePath)}' deleted successfully.",
                    new { name = Path.GetFileNameWithoutExtension(relativePath), path = relativePath }
                );
            }
            catch (Exception e)
            {
                return Response.Error($"Failed to delete shader '{relativePath}': {e.Message}");
            }
        }

        //This is a CGProgram template
        //TODO: making a HLSL template as well?
        static string GenerateDefaultShaderContent(string name)
        {
            return @"Shader """ + name + @"""
        {
            Properties
            {
                _MainTex (""Texture"", 2D) = ""white"" {}
            }
            SubShader
            {
                Tags { ""RenderType""=""Opaque"" }
                LOD 100

                Pass
                {
                    CGPROGRAM
                    #pragma vertex vert
                    #pragma fragment frag
                    #include ""UnityCG.cginc""

                    struct appdata
                    {
                        float4 vertex : POSITION;
                        float2 uv : TEXCOORD0;
                    };

                    struct v2f
                    {
                        float2 uv : TEXCOORD0;
                        float4 vertex : SV_POSITION;
                    };

                    sampler2D _MainTex;
                    float4 _MainTex_ST;

                    v2f vert (appdata v)
                    {
                        v2f o;
                        o.vertex = UnityObjectToClipPos(v.vertex);
                        o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                        return o;
                    }

                    fixed4 frag (v2f i) : SV_Target
                    {
                        fixed4 col = tex2D(_MainTex, i.uv);
                        return col;
                    }
                    ENDCG
                }
            }
        }";
        }
    }
}
