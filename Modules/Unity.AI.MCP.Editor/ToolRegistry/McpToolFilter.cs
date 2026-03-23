using System;
using System.Collections.Generic;

namespace Unity.AI.MCP.Editor.ToolRegistry
{
    /// <summary>
    /// Provides an optional programmatic filter for tools exposed via the MCP server.
    /// By default no filtering is applied; tools are controlled by their
    /// <see cref="McpToolAttribute.EnabledByDefault"/> attribute and user overrides in settings.
    /// Set the <see cref="Filter"/> delegate to add custom filtering on top.
    /// </summary>
    /// <example>
    /// <code>
    /// // In an InitializeOnLoadMethod or similar:
    /// McpToolFilter.Filter = tools =>
    /// {
    ///     // Remove specific tools by name
    ///     tools.RemoveAll(t => t.name.StartsWith("Unity_Profiler_"));
    ///
    ///     // Or keep only specific tools
    ///     // tools.RemoveAll(t => !allowedTools.Contains(t.name));
    ///
    ///     return tools;
    /// };
    /// </code>
    /// </example>
    static class McpToolFilter
    {
        /// <summary>
        /// Optional filter function that receives the full list of enabled tools and returns
        /// the filtered list. The function receives a mutable list that can be modified in place.
        /// Return the list after making modifications.
        /// </summary>
        /// <remarks>
        /// This filter is applied in <see cref="McpToolRegistry.GetAvailableTools"/> after
        /// the enabled state check. It allows fine-grained programmatic control over which
        /// tools are exposed to MCP clients.
        ///
        /// The filter is called each time tools are requested, so keep the implementation
        /// efficient. For complex filtering logic, consider caching results.
        ///
        /// When null (the default), no additional filtering is applied.
        /// </remarks>
        public static Func<List<McpToolInfo>, List<McpToolInfo>> Filter { get; set; }

        /// <summary>
        /// Applies the filter to the given array of tools.
        /// If no filter is set, returns tools unchanged.
        /// </summary>
        /// <param name="tools">The tools to filter</param>
        /// <returns>The filtered tools array</returns>
        internal static McpToolInfo[] Apply(McpToolInfo[] tools)
        {
            if (Filter == null)
                return tools;

            var toolList = new List<McpToolInfo>(tools);
            var filtered = Filter(toolList);
            return filtered?.ToArray() ?? tools;
        }
    }
}
