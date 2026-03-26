using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.AI.Assistant.Bridge.Editor;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using UnityEngine;

namespace Unity.AI.Assistant.Tools.Editor
{
    static class GetConsoleLogsTool
    {
        public const string ToolName = "Unity.GetConsoleLogs";

        [Serializable]
        public struct ConsoleLogEntry
        {
            [JsonProperty("message")]
            public string Message;

            [JsonProperty("stackTrace")]
            public string StackTrace;

            [JsonProperty("type")]
            public string Type; // "Info", "Warning", "Error"

            [JsonProperty("timestamp")]
            public string Timestamp;
        }

        [Serializable]
        public struct ConsoleLogsOutput
        {
            [JsonProperty("logs")]
            public ConsoleLogEntry[] Logs;

            [JsonProperty("totalCount")]
            public int TotalCount;

            [JsonProperty("errorCount")]
            public int ErrorCount;

            [JsonProperty("warningCount")]
            public int WarningCount;
        }

        [AgentTool(
            "Get Unity Console logs including messages, warnings, and errors with their stack traces. Useful for debugging and understanding what errors or issues are occurring in the Unity Editor.",
            ToolName,
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_SmartContextTag,
            Mcp = McpAvailability.Default)]
        public static ConsoleLogsOutput GetConsoleLogs(
            [Parameter("Maximum number of log entries to return (default: 50, max: 200)")]
            int maxEntries = 50,
            [Parameter("Whether to include stack traces in the output (default: true)")]
            bool includeStackTrace = true,
            [Parameter("Comma-separated list of log types to include: 'info', 'warning', 'error' (default: all types). 'info' is for regular log messages.")]
            string logTypes = "info,warning,error")
        {
            // Clamp maxEntries to reasonable bounds
            maxEntries = Mathf.Clamp(maxEntries, 1, 200);

            var logs = new List<ConsoleLogEntry>();
            int errorCount = 0;
            int warningCount = 0;

            try
            {
                // Parse requested log types into per-type bools
                bool wantLog = false;
                bool wantWarning = false;
                bool wantError = false;
                if (!string.IsNullOrEmpty(logTypes))
                {
                    wantLog = logTypes.Contains("info", StringComparison.OrdinalIgnoreCase);
                    wantWarning = logTypes.Contains("warning", StringComparison.OrdinalIgnoreCase);
                    wantError = logTypes.Contains("error", StringComparison.OrdinalIgnoreCase);
                }

                // Use ConsoleUtils to get filtered console logs
                var allLogs = new List<LogData>();
                ConsoleUtils.GetConsoleLogs(allLogs, includeLog: wantLog, includeWarning: wantWarning, includeError: wantError);

                var timeStamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                // Take the most recent entries up to maxEntries
                int startIndex = Mathf.Max(0, allLogs.Count - maxEntries);
                for (int i = startIndex; i < allLogs.Count; i++)
                {
                    var logData = allLogs[i];

                    string logType = logData.Type switch
                    {
                        LogDataType.Error => "Error",
                        LogDataType.Warning => "Warning",
                        LogDataType.Info => "Info",
                        _ => "Info"
                    };

                    if (logData.Type == LogDataType.Error)
                        errorCount++;
                    else if (logData.Type == LogDataType.Warning)
                        warningCount++;

                    logs.Add(new ConsoleLogEntry
                    {
                        Message = logData.Message,
                        StackTrace = includeStackTrace ? logData.File : "",
                        Type = logType,
                        Timestamp = timeStamp
                    });
                }
            }
            catch (Exception ex)
            {
                // Fallback: return error information
                logs.Add(new ConsoleLogEntry
                {
                    Message = $"Failed to retrieve console logs: {ex.Message}",
                    StackTrace = includeStackTrace ? ex.StackTrace : "",
                    Type = "Error",
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
                errorCount = 1;
            }

            return new ConsoleLogsOutput
            {
                Logs = logs.ToArray(),
                TotalCount = logs.Count,
                ErrorCount = errorCount,
                WarningCount = warningCount
            };
        }
    }
}
