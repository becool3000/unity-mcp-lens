using System;

namespace Unity.AI.MCP.Editor.ToolRegistry
{
    static class ExtensionUtilities
    {
        internal static McpToolInfo ToMcpToolInfo(this IToolHandler handler)
        {
            if (handler == null) return null;
            return new()
            {
                name = handler.Attribute?.Name,
                title = handler.Attribute?.Title,
                description = handler.Attribute?.Description,
                inputSchema = handler.GetInputSchema(),
                outputSchema = handler.GetOutputSchema(),
                annotations = handler.Attribute?.Annotations,
            };
        }
    }
}
