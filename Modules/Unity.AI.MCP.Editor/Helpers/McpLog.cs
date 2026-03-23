using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.AI.Tracing;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.MCP.Editor.Helpers
{
    static class McpLog
    {
        static readonly TraceLogger s_Logger = new TraceLogger("mcp");
        static readonly HashSet<string> s_OnceKeys = new();

        static string WithPackageTag(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "[MCP]";

            return message.StartsWith("[MCP]", StringComparison.OrdinalIgnoreCase)
                ? message
                : $"[MCP] {message}";
        }

        public static void Log(string message, TraceEventOptions opts = null) => s_Logger.Info(WithPackageTag(message), opts: opts);
        public static void Warning(string message, TraceEventOptions opts = null) => s_Logger.Warn(WithPackageTag(message), opts: opts);
        public static void Error(string message, TraceEventOptions opts = null) => s_Logger.Error(WithPackageTag(message), opts: opts);

        /// <summary>
        /// Log a warning at most once per unique key until <see cref="ClearOnceKeys"/> is called.
        /// Useful for messages that may be triggered repeatedly (e.g., retry-happy clients).
        /// </summary>
        public static void WarningOnce(string key, string message, TraceEventOptions opts = null)
        {
            lock (s_OnceKeys)
            {
                if (!s_OnceKeys.Add(key))
                    return;
            }
            Warning(message, opts);
        }

        /// <summary>
        /// Background-thread variant of <see cref="WarningOnce"/>: deduplicates by key,
        /// then defers the log to the main thread via <see cref="EditorApplication.delayCall"/>.
        /// </summary>
        public static void WarningOnceDelayed(string key, string message, TraceEventOptions opts = null)
        {
            lock (s_OnceKeys)
            {
                if (!s_OnceKeys.Add(key))
                    return;
            }
            LogDelayed(message, LogType.Warning, opts);
        }

        /// <summary>
        /// Reset the once-per-session deduplication set.
        /// Call on bridge stop/restart so messages can fire again in the next session.
        /// </summary>
        public static void ClearOnceKeys()
        {
            lock (s_OnceKeys) { s_OnceKeys.Clear(); }
        }

        /// <summary>
        /// Log from a background thread - delays execution to main thread via EditorApplication.delayCall
        /// </summary>
        public static void LogDelayed(string message, LogType logType = LogType.Log, TraceEventOptions opts = null)
        {
            var stackTrace = new StackTrace(1, true);
            EditorApplication.delayCall += () =>
            {
                var messageWithStack = $"{WithPackageTag(message)}\n{stackTrace}";
                switch (logType)
                {
                    case LogType.Warning:
                        s_Logger.Warn(messageWithStack, opts: opts);
                        break;
                    case LogType.Error:
                        s_Logger.Error(messageWithStack, opts: opts);
                        break;
                    default:
                        s_Logger.Info(messageWithStack, opts: opts);
                        break;
                }
            };
        }
    }
}
