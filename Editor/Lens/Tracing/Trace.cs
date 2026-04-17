using System;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Becool.UnityMcpLens.Editor.Tracing
{
    /// <summary>
    /// Global trace writer for the Unity Editor process.
    /// Initialized on first access. Writes to the project's Logs/traces.jsonl.
    /// </summary>
    static class Trace
    {
        static TraceWriter s_Writer;
        static readonly object s_Lock = new object();

        /// <summary>
        /// Internal logger used by static convenience methods.
        /// Console = true forces output through Writer.Log (always reaches console + file).
        /// </summary>
        static readonly TraceLogger s_ConsoleLogger = new(new TraceEventOptions { Console = true });

#if UNITY_EDITOR
        const string k_EnabledKey = "Trace.Enabled";

        static bool? s_Enabled;

        /// <summary>
        /// Whether file tracing is enabled. When false, no traces.jsonl is written.
        /// Persisted via EditorUserSettings. Defaults to false (true when ASSISTANT_INTERNAL is defined).
        /// </summary>
        public static bool Enabled
        {
            get
            {
                if (s_Enabled.HasValue)
                    return s_Enabled.Value;

                var stored = EditorUserSettings.GetConfigValue(k_EnabledKey);
                if (!string.IsNullOrEmpty(stored) && bool.TryParse(stored, out var parsed))
                {
                    s_Enabled = parsed;
                }
                else
                {
#if ASSISTANT_INTERNAL
                    s_Enabled = true;
#else
                    s_Enabled = false;
#endif
                }

                return s_Enabled.Value;
            }
        }

        /// <summary>
        /// Enable or disable file tracing. Persists the value and re-initializes the writer.
        /// </summary>
        public static void SetEnabled(bool enabled)
        {
            if (s_Enabled == enabled) return;
            s_Enabled = enabled;
            EditorUserSettings.SetConfigValue(k_EnabledKey, enabled.ToString());

            // Re-initialize writer so FileSink is added/removed
            lock (s_Lock)
            {
                s_Writer = null;
            }
        }
#else
        public static bool Enabled => false;
        public static void SetEnabled(bool enabled) { }
#endif

        /// <summary>
        /// The global TraceWriter instance for the Unity component.
        /// Lazily initialized with default file and console sinks.
        /// Uses <see cref="TraceLogDir.LogDir"/> as the log directory.
        /// </summary>
        public static TraceWriter Writer
        {
            get
            {
                if (s_Writer != null)
                    return s_Writer;

                lock (s_Lock)
                {
                    if (s_Writer != null)
                        return s_Writer;

                    var logDir = TraceLogDir.LogDir;

                    s_Writer = new TraceWriter(TraceComponent.Unity, logDir);

                    // File sink: only when file tracing is enabled
                    if (Enabled)
                        s_Writer.AddSink(new FileSink(logDir, new() { DefaultLevel = "debug" }));

                    // Console sink: quiet, only important things in the Unity console
                    s_Writer.AddSink(new ConsoleSink(new() { DefaultLevel = "warn" }));

                    // Re-initialize when the log directory changes
                    TraceLogDir.OnChanged += OnLogDirChanged;
                }

                return s_Writer;
            }
        }

        static void OnLogDirChanged()
        {
            lock (s_Lock)
            {
                s_Writer = null;
            }
            // Next access will reinitialize with the new path
        }

        /// <summary>
        /// Convenience: start a span on the global writer.
        /// </summary>
        public static TraceSpan StartSpan(string name, TraceEventOptions opts)
            => Writer.StartSpan(name, opts);

        /// <summary>
        /// Convenience: emit an event on the global writer.
        /// </summary>
        public static void Event(string name, TraceEventOptions opts)
            => Writer.Event(name, opts);

        // ========== LOG APIs (Debug.Log replacements — always reach console) ==========

        /// <summary>
        /// Emit a log at the specified level. Always appears in console and file.
        /// </summary>
        public static void Log(string level, string message, string sessionId = null,
            string traceId = null, TraceEventOptions opts = null)
            => s_ConsoleLogger.Log(level, message, sessionId, traceId, opts);

        /// <summary>
        /// Emit an info-level log. Always appears in console and file.
        /// </summary>
        public static void Print(string message, string sessionId = null,
            string traceId = null, TraceEventOptions opts = null)
            => s_ConsoleLogger.Info(message, sessionId, traceId, opts);

        /// <summary>
        /// Emit a debug-level log. Always appears in console and file.
        /// </summary>
        public static void Debug(string message, string sessionId = null,
            string traceId = null, TraceEventOptions opts = null)
            => s_ConsoleLogger.Debug(message, sessionId, traceId, opts);

        /// <summary>
        /// Emit an info-level log. Always appears in console and file.
        /// </summary>
        public static void Info(string message, string sessionId = null,
            string traceId = null, TraceEventOptions opts = null)
            => s_ConsoleLogger.Info(message, sessionId, traceId, opts);

        /// <summary>
        /// Emit a warning-level log. Always appears in console and file.
        /// </summary>
        public static void Warn(string message, string sessionId = null,
            string traceId = null, TraceEventOptions opts = null)
            => s_ConsoleLogger.Warn(message, sessionId, traceId, opts);

        /// <summary>
        /// Emit an error-level log. Always appears in console and file.
        /// </summary>
        public static void Error(string message, string sessionId = null,
            string traceId = null, TraceEventOptions opts = null)
            => s_ConsoleLogger.Error(message, sessionId, traceId, opts);

        /// <summary>
        /// Log an exception. Always appears in both file and console.
        /// Captures exception type, message, stack trace, and inner exceptions.
        /// </summary>
        public static void Exception(Exception exception, string sessionId = null,
            string traceId = null, TraceEventOptions opts = null)
            => s_ConsoleLogger.Exception(exception, sessionId, traceId, opts);

        /// <summary>
        /// Log an exception with additional context. Always appears in both file and console.
        /// </summary>
        public static void Exception(Exception exception, string context,
            string sessionId = null, string traceId = null, TraceEventOptions opts = null)
            => s_ConsoleLogger.Exception(exception, context, sessionId, traceId, opts);

        // ========== CATEGORY VISIBILITY CONTROL ==========

        /// <summary>
        /// Enable or disable console output for a specific category at runtime.
        /// Merges into the existing console sink config so multiple categories are preserved.
        /// </summary>
        /// <param name="category">The category to configure (e.g. "search", "mcp")</param>
        /// <param name="enabled">True to show in console, false to hide</param>
        public static void SetCategoryEnabled(string category, bool enabled)
        {
            Writer.MergeSinkCategoryOverride("console", category, enabled ? "info" : "none");
        }
    }
}
