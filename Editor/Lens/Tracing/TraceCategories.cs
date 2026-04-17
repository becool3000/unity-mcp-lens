#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
#endif

namespace Becool.UnityMcpLens.Editor.Tracing
{
    /// <summary>
    /// Describes a known trace category: its key and display name.
    /// </summary>
    record TraceCategoryInfo(string Key, string DisplayName);

    /// <summary>
    /// Known trace categories for the dev window toggles.
    /// Adding a new entry here automatically creates a toggle in the developer tools window.
    /// Categories may be emitted by any component (Unity, Relay, MCP).
    /// </summary>
    static class TraceCategories
    {
        internal static readonly TraceCategoryInfo[] Known =
        {
            new("gateway", "AI Gateway"),
            new("gateway.session", "Gateway Sessions"),
            new("gateway.provider", "Gateway Provider"),
            new("gateway.cleanup", "Gateway Cleanup"),
            new("gateway.connection", "Gateway Connection"),
            new("gateway.registry", "Gateway Registry"),
            new("gateway.observer", "Gateway Observer"),
            new("mcp", "MCP"),
            new("relay", "Relay"),
            new("general", "General"),
            new("search", "Search"),
            new("search.verbose", "Search Verbose"),
            new("mcp_client", "MCP Client"),
            new("connection", "Connection"),
            new("message", "Messages"),
            new("assistant", "Cloud Backend"),
            new("tool_call", "Tool Execution"),
        };

#if UNITY_EDITOR
        /// <summary>
        /// Check if a category is enabled for the console sink.
        /// Reads from TraceSinkConfigManager; returns false if no override is stored.
        /// </summary>
        internal static bool IsEnabled(string category)
        {
            var config = TraceSinkConfigManager.GetSinkConfig("unity.console");
            if (config.Categories != null &&
                config.Categories.TryGetValue(category, out var level))
            {
                return level != "none";
            }

            return false;
        }

        /// <summary>
        /// Set a category's enabled state for the console sink.
        /// Persists via TraceSinkConfigManager and updates the in-memory sink config.
        /// </summary>
        internal static void SetEnabled(string category, bool enabled)
        {
            var config = TraceSinkConfigManager.GetSinkConfig("unity.console");
            config.Categories ??= new Dictionary<string, string>();
            config.Categories[category] = enabled ? "info" : "none";
            TraceSinkConfigManager.SetSinkConfig("unity.console", config);
            Trace.Writer.MergeSinkCategoryOverride("console", category, enabled ? "info" : "none");
        }

        /// <summary>
        /// Initialize all Unity sink configs from persisted settings.
        /// Called once at editor startup to sync stored state into the trace system.
        /// </summary>
        [InitializeOnLoadMethod]
        static void InitializeFromSettings()
        {
            // Apply stored Unity sink configs at startup
            var fileConfig = TraceSinkConfigManager.GetSinkConfig("unity.file");
            Trace.Writer.UpdateSinkConfig("file", fileConfig);

            var consoleConfig = TraceSinkConfigManager.GetSinkConfig("unity.console");
            Trace.Writer.UpdateSinkConfig("console", consoleConfig);
        }
#endif
    }
}
