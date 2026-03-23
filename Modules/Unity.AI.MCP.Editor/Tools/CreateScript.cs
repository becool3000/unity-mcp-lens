using System;
using System.IO;
using UnityEngine;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry;
using Unity.AI.MCP.Editor.Tools.Parameters;
using Newtonsoft.Json.Linq;

namespace Unity.AI.MCP.Editor.Tools
{
    /// <summary>
    /// Handles creation of new C# scripts at specified project paths.
    /// </summary>
    public static class CreateScript
    {
        /// <summary>
        /// Human-readable description of the Unity.CreateScript tool functionality and usage.
        /// </summary>
        public const string Description = @"Create a new C# script at the given project path.

Args: path (e.g., 'Assets/Scripts/My.cs'), contents (string), script_type, namespace.
Rules: path must be under Assets/. Contents will be Base64-encoded over transport.";

        /// <summary>
        /// Returns the output schema for this tool.
        /// </summary>
        /// <returns>The output schema object defining the structure of successful responses.</returns>
        [McpOutputSchema("Unity.CreateScript")]
        public static object GetOutputSchema()
        {
            return new {type = "object", properties = new {success = new {type = "boolean", description = "Whether the operation succeeded"}, message = new {type = "string", description = "Human-readable message about the operation"}, data = new {type = "object", description = "Script creation data", properties = new {uri = new {type = "string", description = "Unity URI of the created script"}, path = new {type = "string", description = "Relative path of the created script"}, scheduledRefresh = new {type = "boolean", description = "Whether a refresh was scheduled"}}}}, required = new[] {"success", "message"}};
        }

        /// <summary>
        /// Main handler for script creation.
        /// </summary>
        /// <param name="parameters">Parameters containing the script path, contents, type, and namespace.</param>
        /// <returns>A response object indicating success or failure with relevant details.</returns>
        [McpTool("Unity.CreateScript", Description, Groups = new string[] {"core", "scripting"})]
        public static object HandleCommand(CreateScriptParams parameters)
        {
            var @params = parameters;

            // Extract and validate path
            string path = @params.Path;
            if (string.IsNullOrEmpty(path))
            {
                return Response.Error("path parameter is required.");
            }

            // Local validation to avoid round-trips on obviously bad input
            string normPath = Path.GetFullPath(path).Replace('\\', '/');

            // Check for path traversal
            if (path.Contains("..") || Path.IsPathRooted(path))
            {
                return Response.Error("bad_path: path must not contain traversal or be absolute.");
            }

            // Extract name from path
            string name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(name))
            {
                return Response.Error("bad_path: path must include a script file name.");
            }

            // Ensure .cs extension
            if (!path.ToLowerInvariant().EndsWith(".cs"))
            {
                return Response.Error("bad_extension: script file must end with .cs.");
            }

            // Get directory
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                directory = "Assets";
            }

            // Ensure path is under Assets/
            if (!directory.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                return Response.Error($"path_outside_assets: path must be under 'Assets/'; got '{path}'.");
            }

            try
            {
                // Create JObject parameters for ManageScript.HandleCommand
                var scriptParams = new JObject();
                scriptParams["action"] = "create";
                scriptParams["name"] = name;
                scriptParams["path"] = directory;

                if (!string.IsNullOrEmpty(parameters.Contents))
                {
                    scriptParams["contents"] = parameters.Contents;
                }

                if (!string.IsNullOrEmpty(parameters.ScriptType))
                {
                    scriptParams["script_type"] = parameters.ScriptType;
                }

                if (!string.IsNullOrEmpty(parameters.Namespace))
                {
                    scriptParams["namespace"] = parameters.Namespace;
                }

                // Call ManageScript.HandleCommand with the prepared parameters
                var result = ManageScript.HandleCommand(scriptParams);

                return result;
            }
            catch (Exception e)
            {
                return Response.Error($"Failed to create script: {e.Message}");
            }
        }
    }
}
