using System;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry;
using Unity.AI.MCP.Editor.Tools.Parameters;
using Newtonsoft.Json.Linq;

namespace Unity.AI.MCP.Editor.Tools
{
    /// <summary>
    /// Handles getting SHA256 and basic metadata for Unity C# scripts without returning file contents.
    /// </summary>
    public static class GetSHA
    {
        /// <summary>
        /// Human-readable description of the Unity.GetSha tool functionality and usage.
        /// </summary>
        public const string Description = @"Get SHA256 and basic metadata for a Unity C# script without returning file contents";

        /// <summary>
        /// Returns the output schema for this tool.
        /// </summary>
        /// <returns>The output schema object defining the structure of successful responses.</returns>
        [McpOutputSchema("Unity.GetSha")]
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
                        description = "Script metadata",
                        properties = new
                        {
                            sha256 = new { type = "string", description = "SHA256 hash of the script contents" },
                            lengthBytes = new { type = "integer", description = "Length of the script in bytes" },
                            uri = new { type = "string", description = "Unity URI of the script" },
                            path = new { type = "string", description = "Relative path of the script" },
                            lastModifiedUtc = new { type = "string", description = "Last modification time in UTC" }
                        }
                    }
                },
                required = new[] { "success", "message" }
            };
        }

        /// <summary>
        /// Main handler for getting script SHA256 and metadata.
        /// </summary>
        /// <param name="parameters">Parameters containing the script URI or path.</param>
        /// <returns>A response object with SHA256 hash and metadata, or an error if the operation fails.</returns>
        [McpTool("Unity.GetSha", Description, Groups = new string[] { "core", "scripting" })]
        public static object HandleCommand(GetSHAParams parameters)
        {
            string uri = parameters?.Uri;
            if (string.IsNullOrEmpty(uri))
            {
                return Response.Error("uri parameter is required.");
            }

            // Split URI into name and directory using ScriptRefreshHelpers
            var (name, directory) = ScriptRefreshHelpers.SplitUri(uri);

            // Validate the split result
            if (string.IsNullOrEmpty(name))
            {
                return Response.Error("invalid_uri: URI must include a script file name.");
            }

            if (string.IsNullOrEmpty(directory))
            {
                return Response.Error("invalid_uri: URI must include a valid directory path.");
            }

            // Ensure directory is under Assets/
            if (!directory.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                return Response.Error("path_outside_assets: URI must resolve under 'Assets/'.");
            }

            try
            {
                // Create JObject parameters for ManageScript.HandleCommand
                var scriptParams = new JObject();
                scriptParams["action"] = "get_sha";
                scriptParams["name"] = name;
                scriptParams["path"] = directory;

                // Call ManageScript.HandleCommand with the prepared parameters
                var result = ManageScript.HandleCommand(scriptParams);

                // Process the result to extract minimal data as per the Python implementation
                if (result is JObject resultObj && resultObj["success"]?.Value<bool>() == true)
                {
                    var data = resultObj["data"] as JObject;
                    if (data != null)
                    {
                        // Return minimal data like the Python version
                        var minimal = new
                        {
                            sha256 = data["sha256"]?.ToString(),
                            lengthBytes = data["lengthBytes"]?.Value<long>()
                        };

                        return Response.Success(
                            $"SHA256 computed for script '{name}.cs'.",
                            minimal
                        );
                    }
                }

                return result;
            }
            catch (Exception e)
            {
                return Response.Error($"Unity.GetSha_failed, Unity.GetSha error: {e.Message}");
            }
        }
    }
}