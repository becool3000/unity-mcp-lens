using System;

namespace Becool.UnityMcpLens.Editor.Utils
{
    static class ConsoleNoiseFilter
    {
        static readonly string[] k_KnownNoisePatterns =
        {
            "[MCP]",
            "[UnityMCPBridge]",
            "[MCP Approval]",
            "[RelayService]",
            "connection.state_change",
            "Connection validation is DISABLED",
            "Editor never reached a stable state for relay startup",
            "MCP Bridge V2 started",
            "Saved connection info to",
            "Client connected:",
            "Client disconnected:",
            "Sending tools response with",
            "Tools changed:",
            "Becool.UnityMcpLens.Editor.Tracing.ConsoleSink",
            "Becool.UnityMcpLens.Editor.Tracing.TraceWriter",
            "C:/UnityAIAssistantPatch/Editor/Assistant/Relay/RelayService.cs"
        };

        public static bool ShouldExclude(string message, string stackTrace = null)
        {
            if (string.IsNullOrWhiteSpace(message) && string.IsNullOrWhiteSpace(stackTrace))
                return false;

            var combined = string.Concat(message ?? string.Empty, "\n", stackTrace ?? string.Empty);
            foreach (var pattern in k_KnownNoisePatterns)
            {
                if (combined.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }
    }
}
