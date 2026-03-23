using System;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry;
using Unity.AI.MCP.Editor.Tools.Parameters;

namespace Unity.AI.MCP.Editor.Tools
{
    /// <summary>
    /// Handles getting Unity.ManageScript capabilities including supported operations, limits, and guards.
    /// </summary>
    public static class ManageScriptCapabilities
    {
        /// <summary>
        /// Description of the Unity.ManageScript_capabilities tool for MCP clients.
        /// Returns information about supported operations, payload limits, and guard settings for script management.
        /// </summary>
        public const string Description = @"Get Unity.ManageScript capabilities (supported ops, limits, and guards).

Returns:
- ops: list of supported structured ops
- text_ops: list of supported text ops
- max_edit_payload_bytes: server edit payload cap
- guards: header/using guard enabled flag";

        /// <summary>
        /// Returns the output schema for this tool.
        /// </summary>
        /// <returns>The JSON schema object describing the tool's output structure.</returns>
        [McpOutputSchema("Unity.ManageScript_capabilities")]
        public static object GetOutputSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    success = new {type = "boolean", description = "Whether the operation succeeded"},
                    message = new {type = "string", description = "Human-readable message about the operation"},
                    data = new
                    {
                        type = "object",
                        description = "Script capabilities information",
                        properties = new
                        {
                            ops = new {type = "array", description = "List of supported structured operations", items = new {type = "string"}},
                            text_ops = new {type = "array", description = "List of supported text operations", items = new {type = "string"}},
                            max_edit_payload_bytes = new {type = "integer", description = "Maximum edit payload size in bytes"},
                            guards = new {type = "object", description = "Guard settings", properties = new {using_guard = new {type = "boolean", description = "Whether using guard is enabled"}}},
                            extras = new {type = "object", description = "Extra capabilities", properties = new {get_sha = new {type = "boolean", description = "Whether get_sha is supported"}}}
                        }
                    }
                },
                required = new[] {"success", "message"}
            };
        }

        /// <summary>
        /// Main handler for getting script capabilities.
        /// </summary>
        /// <param name="parameters">The parameters for retrieving script capabilities.</param>
        /// <returns>A response object containing supported operations, limits, and guards.</returns>
        [McpTool("Unity.ManageScript_capabilities", Description, Groups = new string[] {"core", "scripting"})]
        public static object HandleCommand(ManageScriptCapabilitiesParams parameters)
        {
            try
            {
                // Keep in sync with server/Editor ManageScript implementation
                var ops = new[] {"replace_class", "delete_class", "replace_method", "delete_method", "insert_method", "anchor_insert", "anchor_delete", "anchor_replace"};

                var textOps = new[] {"replace_range", "regex_replace", "prepend", "append"};

                // Match ManageScript.MaxEditPayloadBytes if exposed; hardcode a sensible default fallback
                int maxEditPayloadBytes = 256 * 1024;

                var guards = new {using_guard = true};
                var extras = new {get_sha = true};

                return Response.Success("Retrieved Unity.ManageScript capabilities successfully", new
                {
                    ops,
                    text_ops = textOps,
                    max_edit_payload_bytes = maxEditPayloadBytes,
                    guards,
                    extras
                });
            }
            catch (Exception e)
            {
                return Response.Error($"capabilities error: {e.Message}");
            }
        }
    }
}
