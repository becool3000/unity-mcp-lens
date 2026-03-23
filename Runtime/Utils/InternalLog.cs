using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.AI.Assistant.Utils
{
    enum LogFilter
    {
        General,
        Search,
        SearchVerbose,
        McpClient
    }

    /// <summary>
    /// Runtime version of InternalLog utility
    /// Define ASSISTANT_INTERNAL to see the logs.
    /// </summary>
    static class InternalLog
    {
        private static readonly object _fileLock = new object();
        private static int _currentFrame = 0;
        private static bool _isFrameCounterInitialized = false;

        const string k_LogFilterSettingKey = "Assistant.InternalLog.Filter_{0}";

        static bool[] s_EnabledFilters;

        static InternalLog()
        {
            InitializeFrameCounter();
        }

        /// <summary>
        /// Maps a LogFilter enum value to a trace category string.
        /// </summary>
        static string FilterToCategory(LogFilter filter) => filter switch
        {
            LogFilter.General => "general",
            LogFilter.Search => "search",
            LogFilter.SearchVerbose => "search.verbose",
            LogFilter.McpClient => "mcp_client",
            _ => "general"
        };

        [Conditional("ASSISTANT_INTERNAL")]
        public static void Log(object message, LogFilter filter = LogFilter.General)
        {
            if (!IsFilterEnabled(filter))
                return;

            Tracing.Trace.Info(message.ToString(),
                opts: new Tracing.TraceEventOptions { Category = FilterToCategory(filter) });
        }

        [Conditional("ASSISTANT_INTERNAL")]
        public static void LogWarning(object message, LogFilter filter = LogFilter.General)
        {
            if (!IsFilterEnabled(filter))
                return;

            Tracing.Trace.Warn(message.ToString(),
                opts: new Tracing.TraceEventOptions { Category = FilterToCategory(filter) });
        }

        [Conditional("ASSISTANT_INTERNAL")]
        public static void LogError(object message, LogFilter filter = LogFilter.General)
        {
            if (!IsFilterEnabled(filter))
                return;

            Tracing.Trace.Error(message.ToString(),
                opts: new Tracing.TraceEventOptions { Category = FilterToCategory(filter) });
        }

        [Conditional("ASSISTANT_INTERNAL")]
        public static void LogException(Exception exception, LogFilter filter = LogFilter.General)
        {
            if (!IsFilterEnabled(filter))
                return;

            Tracing.Trace.Exception(exception);
        }

        /// <summary>
        /// Writes key-value pairs as JSON to a log file in the Unity project's Logs folder.
        /// Thread-safe and supports concurrent access from multiple processes.
        /// Automatically adds timestamp and frame number entries.
        /// </summary>
        /// <param name="filename">The name of the log file (without path)</param>
        /// <param name="keyValuePairs">Key-value pairs to log as JSON</param>
        [Conditional("ASSISTANT_INTERNAL")]
        public static void LogToFile(string filename, params (string key, string value)[] keyValuePairs)
        {
            if (string.IsNullOrEmpty(filename))
                return;

            lock (_fileLock)
            {
                try
                {
                    // Get the Unity project root directory (assuming Application.dataPath points to Assets folder)
                    string projectRoot = Path.GetDirectoryName(UnityEngine.Application.dataPath);
                    string logsDir = Path.Combine(projectRoot, "Logs");

                    // Ensure the Logs directory exists
                    if (!Directory.Exists(logsDir))
                    {
                        Directory.CreateDirectory(logsDir);
                    }

                    string filePath = Path.Combine(logsDir, filename);

                    // Build JSON object
                    var jsonBuilder = new StringBuilder();
                    jsonBuilder.Append("{ ");

                    // Add timestamp and frame number first
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    int frameNumber = GetCurrentFrame();
                    jsonBuilder.Append($"\"timestamp\": \"{EscapeJsonString(timestamp)}\", \"frame\": {frameNumber}");

                    // Add user-provided key-value pairs
                    if (keyValuePairs != null && keyValuePairs.Length > 0)
                    {
                        foreach (var pair in keyValuePairs)
                        {
                            if (!string.IsNullOrEmpty(pair.key))
                            {
                                jsonBuilder.Append(", ");
                                jsonBuilder.Append(
                                    $"\"{EscapeJsonString(pair.key)}\": \"{EscapeJsonString(pair.value ?? "")}\"");
                            }
                        }
                    }

                    jsonBuilder.Append(" }");

                    // Use FileStream with FileShare.Read to allow other processes to read while we write
                    // FileOptions.SequentialScan for better performance when appending
                    using (FileStream fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read,
                               4096, FileOptions.SequentialScan))
                    using (StreamWriter writer = new StreamWriter(fs))
                    {
                        writer.WriteLine(jsonBuilder.ToString());
                        writer.Flush();
                        fs.Flush(true); // Force OS to write to disk
                    }
                }
                catch (Exception ex)
                {
                    // Fall back to Unity's debug log if file writing fails
                    Debug.LogError($"Failed to write to log file '{filename}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Initializes the frame counter by registering for editor updates
        /// </summary>
        private static void InitializeFrameCounter()
        {
            if (_isFrameCounterInitialized)
                return;

#if UNITY_EDITOR
            EditorApplication.update -= UpdateFrameCounter;
            EditorApplication.update += UpdateFrameCounter;
#endif

            _isFrameCounterInitialized = true;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Updates the frame counter (called from editor update on main thread)
        /// </summary>
        private static void UpdateFrameCounter()
        {
            try
            {
                // Since we're called from EditorApplication.update, we're guaranteed to be on main thread
                // So we can safely access Time.frameCount
                _currentFrame = Time.frameCount;
            }
            catch
            {
                // Fallback to increment if for some reason Time.frameCount fails
                _currentFrame++;
            }
        }
#endif

        /// <summary>
        /// Gets the current frame number in a thread-safe way
        /// </summary>
        /// <returns>Current frame number</returns>
        private static int GetCurrentFrame()
        {
            return _currentFrame;
        }

        /// <summary>
        /// Escapes special characters in a string for JSON formatting
        /// </summary>
        /// <param name="str">The string to escape</param>
        /// <returns>JSON-safe escaped string</returns>
        private static string EscapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str))
                return "";

            return str.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

#if UNITY_EDITOR
        static void InitializeLogFilters()
        {
            s_EnabledFilters = new bool[Enum.GetValues(typeof(LogFilter)).Length];

            foreach (LogFilter logFilterOption in Enum.GetValues(typeof(LogFilter)))
            {
                var filterOptionValue =
                    EditorUserSettings.GetConfigValue(string.Format(k_LogFilterSettingKey, logFilterOption));

                if (!string.IsNullOrEmpty(filterOptionValue) && bool.TryParse(filterOptionValue, out var parsedSetting))
                {
                    s_EnabledFilters[(int)logFilterOption] = parsedSetting;
                }
                else
                {
                    // Set default for that filter:
                    if (logFilterOption == LogFilter.General)
                    {
                        s_EnabledFilters[(int)logFilterOption] = true;
                    }
                }
            }
        }

        internal static bool IsFilterEnabled(LogFilter filter)
        {
            if (s_EnabledFilters == null)
            {
                // InitializeLogFilters uses Unity APIs that require the main thread.
                // If called from a background thread before initialization, return default.
                if (!MainThread.IsMainThread)
                {
                    return filter == LogFilter.General;
                }
                InitializeLogFilters();
            }

            return s_EnabledFilters[(int)filter];
        }

        internal static void SetFilterEnabled(LogFilter filter, bool enabled)
        {
            if (s_EnabledFilters == null)
            {
                InitializeLogFilters();
            }

            s_EnabledFilters[(int)filter] = enabled;

            EditorUserSettings.SetConfigValue(string.Format(k_LogFilterSettingKey, filter), enabled.ToString());
        }
#else
         internal static bool IsFilterEnabled(LogFilter filter) => false;
#endif
    }
}
