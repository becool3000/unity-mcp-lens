using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Editor.Utils;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class CodeEditTool
    {
        const string k_FunctionId = "Unity.CodeEdit";

        enum FileType
        {
            CSharp,
            Manifest,
            Shader,
            Other
        }

        [Serializable]
        public struct CodeEditOutput
        {
            [JsonProperty("result")]
            public string Result;

            [JsonProperty("compilationOutput")]
            public string CompilationOutput;
        }

        [AgentTool(
            "Edit scripts (csharp, uxml, uss etc.) using precise string replacement or save scripts with source code. Editing existing script requires exact literal text matching. For creating new scripts, use empty oldString. File paths can be relative to Unity project root (e.g., \"Assets/Scripts/Player.cs\") or absolute. Automatically validates C# compilation after edits.",
            k_FunctionId,
            ToolCallEnvironment.EditMode,
            tags: FunctionCallingUtilities.k_CodeEditTag
            )]
        public static async Task<CodeEditOutput> SaveCode(
            ToolExecutionContext context,
            [Parameter("Path to the file to modify or create. Can be relative to Unity project root (e.g., \"Assets/Scripts/Player.cs\") or absolute.")]
            string filePath,
            [Parameter("Short description of the changes being made")]
            string description,
            [Parameter("Exact literal text to replace oldString with. For new files, this becomes the entire file content. Include proper whitespace, indentation, and ensure resulting code is correct.")]
            string newString,
            [Parameter("Exact literal text to replace. Must include sufficient context (3+ lines) to uniquely identify the location. Match whitespace and indentation precisely. For new files ONLY, use empty string otherwise a valid value has to be supplied.")]
            string oldString = "",
            [Parameter("Number of occurrences expected to be replaced. Defaults to 1.")]
            int expectedOccurrences = 1)
        {
            try
            {
                // Resolve file path (handle relative paths from Unity project root)
                var resolvedPath = ResolvePath(filePath);

                // Read original file content or empty string for new files
                var originalCode = "";
                var isNewFile = false;

                if (File.Exists(resolvedPath))
                {
                    await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Modify, resolvedPath);
                    originalCode = File.ReadAllText(resolvedPath);
                }
                else
                {
                    await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Create, resolvedPath);
                    isNewFile = true;
                    // Ensure parent directory exists for new files
                    var directory = Path.GetDirectoryName(resolvedPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                }

                // Handle file creation: if new file and oldString is empty, treat as file creation
                string modifiedCode;
                if (isNewFile)
                {
                    if (!string.IsNullOrEmpty(oldString))
                    {
                        throw new Exception("Cannot specify oldString when creating a new file. Use empty oldString for file creation.");
                    }

                    modifiedCode = newString;
                }
                else
                {
                    if (string.IsNullOrEmpty(oldString))
                    {
                        throw new Exception("File already exists and no oldString was provided. oldString is required for editing existing files.");
                    }

                    // Validate old string exists and create regex pattern for replacement
                    var escapedPattern = Regex.Escape(oldString);
                    var matches = Regex.Matches(originalCode, escapedPattern);

                    if (matches.Count == 0)
                    {
                        throw new Exception("The specified oldString was not found in the file. Ensure the text matches exactly, including whitespace and indentation.");
                    }

                    if (matches.Count != expectedOccurrences)
                    {
                        throw new Exception($"Expected {expectedOccurrences} occurrences of oldString, but found {matches.Count}. Please provide more specific context to uniquely identify the location.");
                    }

                    var regex = new Regex(escapedPattern);
                    modifiedCode = regex.Replace(originalCode, newString, expectedOccurrences);
                }

                await File.WriteAllTextAsync(resolvedPath, modifiedCode);

                AssetDatabase.Refresh();

                var fileType = GetFileType(resolvedPath);

                switch (fileType)
                {
                    case FileType.Other:
                    {
                        var outputMessage = isNewFile
                            ? $"The file was successfully created and saved at {Path.GetFileName(resolvedPath)}"
                            : $"The file was successfully edited and saved at {Path.GetFileName(resolvedPath)}";

                        return new CodeEditOutput { Result = outputMessage, CompilationOutput = string.Empty };
                    }
                    case FileType.Shader:
                    {
                        // Check if project is compiling for shaders as well
                        var compilationResult = await ProjectScriptCompilation.RequestProjectCompilation();

                        var loadPath = GetRelativeAssetPath(resolvedPath);
                        var shader = AssetDatabase.LoadAssetAtPath<Shader>(loadPath);
                        if (shader != null)
                        {
                            if (ShaderUtil.ShaderHasError(shader))
                            {
                                var shaderMessages = ShaderUtil.GetShaderMessages(shader);
                                var errorMessage = string.Join("\n", System.Linq.Enumerable.Select(shaderMessages, m => $"{m.severity}: {m.message} (line {m.line})"));

                                var shaderCompilationMessage = (isNewFile
                                    ? $"The file was successfully created and saved at {Path.GetFileName(resolvedPath)}"
                                    : $"The file was successfully edited and saved at {Path.GetFileName(resolvedPath)}")
                                    + ", but it now contains compilation errors that need to be fixed.";

                                return new CodeEditOutput
                                {
                                    Result = shaderCompilationMessage,
                                    CompilationOutput = errorMessage
                                };
                            }    
                        }
                        else
                        {
                            return new CodeEditOutput
                            {
                                Result = $"The was saved and created, however the shader could not be loaded at {Path.GetFileName(resolvedPath)}",
                                CompilationOutput = $"AssetDatabase.LoadAssetAtPath<Shader>({loadPath}) returned null"!
                            }; 
                        }

                        var successMessage = (isNewFile
                            ? $"The file was successfully created and saved at {Path.GetFileName(resolvedPath)}"
                            : $"The file was successfully edited and saved at {Path.GetFileName(resolvedPath)}")
                            + ", and it compiled successfully.";

                        return new CodeEditOutput { Result = successMessage, CompilationOutput = string.Empty };
                    }
                    default:
                    {
                        // Check if project is compiling for C# and manifest files
                        var compilationResult = await ProjectScriptCompilation.RequestProjectCompilation();

                        var compilationMessage =
                            (isNewFile
                                ? $"The file was successfully created and saved at {Path.GetFileName(resolvedPath)}"
                                : $"The file was successfully edited and saved at {Path.GetFileName(resolvedPath)}")
                            + (compilationResult.Success
                                ? ", and it compiled successfully."
                                : ", but it now contains compilation errors that need to be fixed.");

                        var result = new CodeEditOutput
                        {
                            Result = compilationMessage,
                            CompilationOutput = compilationResult.ErrorMessage
                        };

                        if (compilationResult.Success)
                            ProjectScriptCompilation.ForceDomainReload();

                        return result;        
                    }
                }
            }
            catch (Exception ex)
            {
                InternalLog.LogException(ex);
                throw;
            }
        }

        static string ResolvePath(string filePath)
        {
            if (Path.IsPathRooted(filePath))
                return filePath;

            // Handle Unity project relative paths
            var projectPath = Directory.GetCurrentDirectory();
            return Path.Combine(projectPath, filePath.Replace('/', Path.DirectorySeparatorChar));
        }

        static FileType GetFileType(string filePath)
        {
            if (filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                return FileType.CSharp;

            if (filePath.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase))
                return FileType.Manifest;

            if (filePath.EndsWith(".shader", StringComparison.OrdinalIgnoreCase))
                return FileType.Shader;

            return FileType.Other;
        }

        static string GetRelativeAssetPath(string absolutePath)
        {
            var projectPath = Directory.GetCurrentDirectory();
            if (absolutePath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = absolutePath.Substring(projectPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return relativePath.Replace(Path.DirectorySeparatorChar, '/');
            }
            return absolutePath;
        }
    }
}
