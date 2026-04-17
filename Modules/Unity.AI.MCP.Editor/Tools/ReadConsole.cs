using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Unity.AI.MCP.Editor.Utils;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry; // For Response class
using Unity.AI.MCP.Editor.Tools.Parameters;
using Unity.AI.MCP.Editor.Lens;

namespace Unity.AI.MCP.Editor.Tools
{
    /// <summary>
    /// Handles reading and clearing Unity Editor console log entries.
    /// Uses reflection to access internal LogEntry methods/properties.
    /// </summary>
    public static class ReadConsole
    {
        /// <summary>
        /// Gets the description text for the Unity.ReadConsole tool.
        /// Describes the available parameters and return values for reading or clearing console messages.
        /// </summary>
        public const string Description = @"Gets messages from or clears the Unity Editor console.

Args:
    Action: Operation ('Get' or 'Clear').
    Types: Message types to get ('Error', 'Warning', 'Log', 'All').
    Count: Max messages to return.
    FilterText: Text filter for messages.
    SinceTimestamp: Get messages after this timestamp (ISO 8601).
    Cursor: Optional incremental cursor from a previous response.
    Format: Output format ('Summary', 'Plain', 'Detailed', 'Json').
    ExcludeMcpNoise: Exclude MCP, relay, and package self-noise from output (default: true).
    IncludeStacktrace: Include stack traces in output.

Returns:
    Dictionary with results. For 'get', includes 'data' (messages).";
        /// <summary>
        /// Returns the output schema for this tool.
        /// </summary>
        /// <returns>The JSON schema object describing the tool's output structure.</returns>
        [McpOutputSchema("Unity.ReadConsole")]
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
                        description = "Console log entries or grouped digests plus a cursor for incremental reads",
                        properties = new
                        {
                            cursor = new { type = "integer", description = "Cursor to pass back on the next call" }
                        }
                    }
                },
                required = new[] { "success", "message" }
            };
        }

        // (Calibration removed)

        // Reflection members for accessing internal LogEntry data
        // private static MethodInfo _getEntriesMethod; // Removed as it's unused and fails reflection
        static MethodInfo _startGettingEntriesMethod;
        static MethodInfo _endGettingEntriesMethod; // Renamed from _stopGettingEntriesMethod, trying End...
        static MethodInfo _clearMethod;
        static MethodInfo _getCountMethod;
        static MethodInfo _getEntryMethod;
        static FieldInfo _modeField;
        static FieldInfo _messageField;
        static FieldInfo _fileField;
        static FieldInfo _lineField;
        static FieldInfo _instanceIdField;

        // Note: Timestamp is not directly available in LogEntry; need to parse message or find alternative?

        // Static constructor for reflection setup
        static ReadConsole()
        {
            try
            {
                Type logEntriesType = typeof(EditorApplication).Assembly.GetType(
                    "UnityEditor.LogEntries"
                );
                if (logEntriesType == null)
                    throw new Exception("Could not find internal type UnityEditor.LogEntries");



                // Include NonPublic binding flags as internal APIs might change accessibility
                BindingFlags staticFlags =
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                BindingFlags instanceFlags =
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                _startGettingEntriesMethod = logEntriesType.GetMethod(
                    "StartGettingEntries",
                    staticFlags
                );
                if (_startGettingEntriesMethod == null)
                    throw new Exception("Failed to reflect LogEntries.StartGettingEntries");

                // Try reflecting EndGettingEntries based on warning message
                _endGettingEntriesMethod = logEntriesType.GetMethod(
                    "EndGettingEntries",
                    staticFlags
                );
                if (_endGettingEntriesMethod == null)
                    throw new Exception("Failed to reflect LogEntries.EndGettingEntries");

                _clearMethod = logEntriesType.GetMethod("Clear", staticFlags);
                if (_clearMethod == null)
                    throw new Exception("Failed to reflect LogEntries.Clear");

                _getCountMethod = logEntriesType.GetMethod("GetCount", staticFlags);
                if (_getCountMethod == null)
                    throw new Exception("Failed to reflect LogEntries.GetCount");

                _getEntryMethod = logEntriesType.GetMethod("GetEntryInternal", staticFlags);
                if (_getEntryMethod == null)
                    throw new Exception("Failed to reflect LogEntries.GetEntryInternal");

                Type logEntryType = typeof(EditorApplication).Assembly.GetType(
                    "UnityEditor.LogEntry"
                );
                if (logEntryType == null)
                    throw new Exception("Could not find internal type UnityEditor.LogEntry");

                _modeField = logEntryType.GetField("mode", instanceFlags);
                if (_modeField == null)
                    throw new Exception("Failed to reflect LogEntry.mode");

                _messageField = logEntryType.GetField("message", instanceFlags);
                if (_messageField == null)
                    throw new Exception("Failed to reflect LogEntry.message");

                _fileField = logEntryType.GetField("file", instanceFlags);
                if (_fileField == null)
                    throw new Exception("Failed to reflect LogEntry.file");

                _lineField = logEntryType.GetField("line", instanceFlags);
                if (_lineField == null)
                    throw new Exception("Failed to reflect LogEntry.line");

                string[] idFieldNames =
                {
                    UnityApiAdapter.GetLogEntryIdFieldName(),
                    "instanceID",
                    "instanceId",
                    "entityId"
                };

                foreach (
                    string idFieldName in idFieldNames
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.Ordinal)
                )
                {
                    _instanceIdField = logEntryType.GetField(idFieldName, instanceFlags);
                    if (_instanceIdField != null)
                    {
                        break;
                    }
                }

                if (_instanceIdField == null)
                {
                    Debug.LogWarning(
                        "[ReadConsole] Optional LogEntry id field was not found. Continuing without console object reference metadata."
                    );
                }

                // (Calibration removed)

            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"[ReadConsole] Static Initialization Failed: Could not setup reflection for LogEntries/LogEntry. Console reading/clearing will likely fail. Specific Error: {e.Message}"
                );
                // Set members to null to prevent NullReferenceExceptions later, HandleCommand should check this.
                _startGettingEntriesMethod =
                    _endGettingEntriesMethod =
                    _clearMethod =
                    _getCountMethod =
                    _getEntryMethod =
                        null;
                _modeField = _messageField = _fileField = _lineField = _instanceIdField = null;
            }
        }

        // --- Input Validation ---

        /// <summary>
        /// Validates and normalizes input parameters, similar to Python's defensive parameter handling.
        /// Applies defaults and defensive coercion for robustness.
        /// </summary>
        static ReadConsoleParams ValidateInput(ReadConsoleParams parameters) =>
            parameters with
            {
                // Default Types to [Error, Warning, Log] if null or empty
                Types = parameters.Types == null || parameters.Types.Length == 0
                    ? new[] { ConsoleLogType.Error, ConsoleLogType.Warning, ConsoleLogType.Log }
                    : parameters.Types,

                // Treat negative or zero Count as null (no limit)
                Count = parameters.Count.HasValue && parameters.Count.Value <= 0
                    ? null
                    : parameters.Count,

                // Trim and normalize FilterText
                FilterText = string.IsNullOrWhiteSpace(parameters.FilterText)
                    ? null
                    : parameters.FilterText.Trim(),

                // Trim and normalize SinceTimestamp
                SinceTimestamp = string.IsNullOrWhiteSpace(parameters.SinceTimestamp)
                    ? null
                    : parameters.SinceTimestamp.Trim(),

                Cursor = parameters.Cursor.HasValue && parameters.Cursor.Value < 0
                    ? 0
                    : parameters.Cursor
            };

        // --- Main Handler ---

        /// <summary>
        /// Main handler for console management actions.
        /// Processes Unity.ReadConsole tool requests to get or clear Unity console messages.
        /// </summary>
        /// <param name="parameters">The parameters specifying the console operation to perform.</param>
        /// <returns>A Response object containing the operation result. For 'Get' actions, includes console log entries in the data field.</returns>    
        [McpTool("Unity.ReadConsole", Description, Groups = new[] { "debug", "editor" })]
        public static object HandleCommand(ReadConsoleParams parameters)
        {
            // Check if ALL required reflection members were successfully initialized.
            if (
                _startGettingEntriesMethod == null
                || _endGettingEntriesMethod == null
                || _clearMethod == null
                || _getCountMethod == null
                || _getEntryMethod == null
                || _modeField == null
                || _messageField == null
                || _fileField == null
                || _lineField == null
            )
            {
                // Log the error here as well for easier debugging in Unity Console
                Debug.LogError(
                    "[ReadConsole] HandleCommand called but reflection members are not initialized. Static constructor might have failed silently or there's an issue."
                );
                return Response.Error(
                    "ReadConsole handler failed to initialize due to reflection errors. Cannot access console logs."
                );
            }

            // Validate and normalize input parameters
            var @params = ValidateInput(parameters);

            try
            {
                if (@params.Action == ConsoleAction.Clear)
                {
                    return ClearConsole();
                }
                else if (@params.Action == ConsoleAction.Get)
                {
                    // Extract parameters for 'get'
                    var types = new List<string>();
                    if (@params.Types != null)
                    {
                        foreach (var type in @params.Types)
                        {
                            types.Add(type.ToString().ToLower());
                        }
                    }
                    else
                    {
                        types = new List<string> { "error", "warning", "log" };
                    }

                    int? count = @params.Count;
                    string filterText = @params.FilterText;
                    string sinceTimestampStr = @params.SinceTimestamp;
                    string format = @params.Format.ToString().ToLower();
                    bool excludeMcpNoise = @params.ExcludeMcpNoise;
                    bool includeStacktrace = @params.IncludeStacktrace;
                    int startCursor = ResolveCursor(@params.Cursor, sinceTimestampStr);

                    if (types.Contains("all"))
                    {
                        types = new List<string> { "error", "warning", "log" }; // Expand 'all'
                    }

                    return GetConsoleEntries(types, count, filterText, format, includeStacktrace, excludeMcpNoise, startCursor);
                }
                else
                {
                    return Response.Error(
                        $"Unknown action: '{@params.Action}'. Valid actions are 'Get' or 'Clear'."
                    );
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ReadConsole] Action '{@params.Action}' failed: {e}");
                return Response.Error($"Internal error processing action '{@params.Action}': {e.Message}");
            }
        }

        // --- Action Implementations ---

        static object ClearConsole()
        {
            try
            {
                _clearMethod.Invoke(null, null); // Static method, no instance, no parameters

                return Response.Success("Console cleared successfully.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ReadConsole] Failed to clear console: {e}");
                return Response.Error($"Failed to clear console: {e.Message}");
            }
        }

        static object GetConsoleEntries(
            List<string> types,
            int? count,
            string filterText,
            string format,
            bool includeStacktrace,
            bool excludeMcpNoise,
            int startCursor
        )
        {
            List<ConsoleLogEntry> formattedEntries = new List<ConsoleLogEntry>();
            int retrievedCount = 0;
            int totalEntries = 0;
            int scanStart = 0;

            try
            {
                // LogEntries requires calling Start/Stop around GetEntries/GetEntryInternal
                _startGettingEntriesMethod.Invoke(null, null);

                totalEntries = (int)_getCountMethod.Invoke(null, null);
                // Create instance to pass to GetEntryInternal - Ensure the type is correct
                Type logEntryType = typeof(EditorApplication).Assembly.GetType(
                    "UnityEditor.LogEntry"
                );
                if (logEntryType == null)
                    throw new Exception(
                        "Could not find internal type UnityEditor.LogEntry during GetConsoleEntries."
                    );
                object logEntryInstance = Activator.CreateInstance(logEntryType);

                int boundedWindow = format == "summary"
                    ? Math.Max(200, (count ?? 50) * 20)
                    : Math.Max(100, count ?? 100);
                scanStart = Math.Max(startCursor, Math.Max(0, totalEntries - boundedWindow));

                for (int i = scanStart; i < totalEntries; i++)
                {
                    // Get the entry data into our instance using reflection
                    _getEntryMethod.Invoke(null, new object[] { i, logEntryInstance });

                    // Extract data using reflection
                    int mode = (int)_modeField.GetValue(logEntryInstance);
                    string message = (string)_messageField.GetValue(logEntryInstance);
                    string file = (string)_fileField.GetValue(logEntryInstance);

                    int line = (int)_lineField.GetValue(logEntryInstance);
                    // int instanceId = (int)_instanceIdField.GetValue(logEntryInstance);

                    if (string.IsNullOrEmpty(message))
                    {
                        continue; // Skip empty messages
                    }

                    // (Calibration removed)

                    // --- Filtering ---
                    // Use mode bits as primary source since LogEntry.message doesn't include stack trace
                    // InferTypeFromMessage only works if full message with stack trace is available
                    LogType unityType = GetLogTypeFromMode(mode);

                    bool want;
                    if (!types.Any())
                        types.Add(nameof(LogType.Error).ToLowerInvariant());

                    // Treat Exception/Assert as errors for filtering convenience
                    if (unityType == LogType.Exception)
                    {
                        want = types.Contains("error") || types.Contains("exception");
                    }
                    else if (unityType == LogType.Assert)
                    {
                        want = types.Contains("error") || types.Contains("assert");
                    }
                    else
                    {
                        want = types.Contains(unityType.ToString().ToLowerInvariant());
                    }

                    if (!want) continue;

                    // Filter by text (case-insensitive)
                    if (
                        !string.IsNullOrEmpty(filterText)
                        && message.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) < 0
                    )
                    {
                        continue;
                    }

                    // TODO: Filter by timestamp (requires timestamp data)

                    // --- Formatting ---
                    string stackTrace = includeStacktrace ? ExtractStackTrace(message) : null;
                    if (excludeMcpNoise && ConsoleNoiseFilter.ShouldExclude(message, stackTrace))
                    {
                        continue;
                    }

                    // Get first line if stack is present and requested, otherwise use full message
                    string messageOnly =
                        (format == "summary")
                            ? message.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)[0]
                        : (includeStacktrace && !string.IsNullOrEmpty(stackTrace))
                            ? message.Split(
                                new[] { '\n', '\r' },
                                StringSplitOptions.RemoveEmptyEntries
                            )[0]
                            : message;

                    ConsoleLogEntry formattedEntry = null;
                    switch (format)
                    {
                        case "plain":
                            formattedEntry = new ConsoleLogEntry
                            {
                                Message = messageOnly,
                                Type = unityType.ToString()
                            };
                            break;
                        case "json":
                        case "detailed": // Treat detailed as json for structured return
                        default:
                            formattedEntry = new ConsoleLogEntry
                            {
                                Type = unityType.ToString(),
                                Message = messageOnly,
                                File = file,
                                Line = line,
                                StackTrace = stackTrace // Will be null if includeStacktrace is false or no stack found
                            };
                            break;
                    }

                    formattedEntries.Add(formattedEntry);
                    retrievedCount++;

                    // Apply count limit (after filtering)
                    if (format != "summary" && count.HasValue && retrievedCount >= count.Value)
                    {
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ReadConsole] Error while retrieving log entries: {e}");
                // Ensure EndGettingEntries is called even if there's an error during iteration
                try
                {
                    _endGettingEntriesMethod.Invoke(null, null);
                }
                catch
                { /* Ignore nested exception */
                }
                return Response.Error($"Error retrieving log entries: {e.Message}");
            }
            finally
            {
                // Ensure we always call EndGettingEntries
                try
                {
                    _endGettingEntriesMethod.Invoke(null, null);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ReadConsole] Failed to call EndGettingEntries: {e}");
                    // Don't return error here as we might have valid data, but log it.
                }
            }

            if (format == "summary")
            {
                var grouped = formattedEntries
                    .GroupBy(entry => CreateFingerprint(entry.Message, entry.StackTrace, entry.Type, entry.File, entry.Line))
                    .Select(group =>
                    {
                        var first = group.First();
                        return new
                        {
                            fingerprint = group.Key,
                            type = first.Type,
                            count = group.Count(),
                            message = first.Message,
                            file = first.File,
                            line = first.Line,
                            topFrame = GetTopFrame(first.StackTrace)
                        };
                    })
                    .OrderByDescending(group => group.count)
                    .ThenBy(group => group.type)
                    .Take(count ?? PayloadBudgetPolicy.MaxDiagnosticFindings)
                    .ToList();

                var rawJson = Newtonsoft.Json.JsonConvert.SerializeObject(formattedEntries, Newtonsoft.Json.Formatting.None);
                var shapedJson = Newtonsoft.Json.JsonConvert.SerializeObject(grouped, Newtonsoft.Json.Formatting.None);
                int rawBytes = PayloadBudgeting.GetUtf8ByteCount(rawJson);
                PayloadStats.Record("tool_result", "Unity.ReadConsole.summary", rawBytes, PayloadBudgeting.GetUtf8ByteCount(shapedJson), PayloadBudgeting.EstimateTokensFromBytes(PayloadBudgeting.GetUtf8ByteCount(shapedJson)), PayloadBudgeting.ComputeSha256(rawJson));
                var detailRef = rawBytes > PayloadBudgetPolicy.MaxToolResultBytes
                    ? ToolResultCompactor.CreateStoredDetailRef(
                        "Unity.ReadConsole",
                        new
                        {
                            cursor = totalEntries,
                            scannedFrom = scanStart,
                            entries = formattedEntries
                        },
                        rawBytes,
                        new
                        {
                            format = "summary",
                            entryCount = formattedEntries.Count
                        })
                    : null;

                return Response.Success(
                    $"Retrieved {formattedEntries.Count} console entries in summary mode.",
                    new
                    {
                        cursor = totalEntries,
                        scannedFrom = scanStart,
                        entryCount = formattedEntries.Count,
                        groups = grouped,
                        detailAvailable = detailRef != null,
                        detailRef
                    }
                );
            }

            return Response.Success(
                $"Retrieved {formattedEntries.Count} log entries.",
                ToolResultCompactor.ShapeJsonPayload(
                    "Unity.ReadConsole",
                    $"Retrieved {formattedEntries.Count} log entries.",
                    new
                    {
                        cursor = totalEntries,
                        scannedFrom = scanStart,
                        entries = formattedEntries
                    },
                    new
                    {
                        format,
                        includeStacktrace,
                        entryCount = formattedEntries.Count
                    }));
        }

        // --- Internal Helpers ---

        // Mapping bits from LogEntry.mode. These vary by Unity version.
        // Unity changed bit positions for log types between major versions.
        //
        // Unity 6000.x (verified via diagnostic logging on 6000.2.9f1):
        //   Error=0x100 (bit 8), Warning=0x200 (bit 9), Log=0x400 (bit 10)
        //
        // Unity 2023.x and earlier (based on historical codebase):
        //   Error=0x1 (bit 0), Warning=0x4 (bit 2), Log=0x8 (bit 3)
        //
        // Using conditional compilation to select correct bit positions at compile time.

#if UNITY_6000_0_OR_NEWER
        // Unity 6000.x uses higher bit positions
        const int ModeBitError = 1 << 8;        // 0x100
        const int ModeBitWarning = 1 << 9;      // 0x200
        const int ModeBitLog = 1 << 10;         // 0x400
#else
        // Unity 2023.x and earlier use lower bit positions
        const int ModeBitError = 1 << 0;        // 0x1
        const int ModeBitWarning = 1 << 2;      // 0x4
        const int ModeBitLog = 1 << 3;          // 0x8
#endif

        // These appear consistent across versions
        const int ModeBitAssert = 1 << 1;
        const int ModeBitException = 1 << 4;

        static LogType GetLogTypeFromMode(int mode)
        {
            // Check individual bits (positions are version-specific, see constants above)
            // Check each bit independently without OR-ing to avoid false positives
            if ((mode & ModeBitException) != 0) return LogType.Exception;
            if ((mode & ModeBitError) != 0) return LogType.Error;
            if ((mode & ModeBitAssert) != 0) return LogType.Assert;
            if ((mode & ModeBitWarning) != 0) return LogType.Warning;
            if ((mode & ModeBitLog) != 0) return LogType.Log;
            return LogType.Log; // Default fallback
        }

        // (Calibration helpers removed)

        /// <summary>
        /// Classifies severity using message/stacktrace content. Works across Unity versions.
        /// </summary>
        static LogType InferTypeFromMessage(string fullMessage)
        {
            if (string.IsNullOrEmpty(fullMessage)) return LogType.Log;

            // Fast path: look for explicit Debug API names in the appended stack trace
            // e.g., "UnityEngine.Debug:LogError (object)" or "LogWarning"
            if (fullMessage.IndexOf("LogError", StringComparison.OrdinalIgnoreCase) >= 0)
                return LogType.Error;
            if (fullMessage.IndexOf("LogWarning", StringComparison.OrdinalIgnoreCase) >= 0)
                return LogType.Warning;

            // Compiler diagnostics (C#): "warning CSxxxx" / "error CSxxxx"
            if (fullMessage.IndexOf(" warning CS", StringComparison.OrdinalIgnoreCase) >= 0
                || fullMessage.IndexOf(": warning CS", StringComparison.OrdinalIgnoreCase) >= 0)
                return LogType.Warning;
            if (fullMessage.IndexOf(" error CS", StringComparison.OrdinalIgnoreCase) >= 0
                || fullMessage.IndexOf(": error CS", StringComparison.OrdinalIgnoreCase) >= 0)
                return LogType.Error;

            // Exceptions (avoid misclassifying compiler diagnostics)
            if (fullMessage.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0)
                return LogType.Exception;

            // Unity assertions
            if (fullMessage.IndexOf("Assertion", StringComparison.OrdinalIgnoreCase) >= 0)
                return LogType.Assert;

            return LogType.Log;
        }

        static bool IsExplicitDebugLog(string fullMessage)
        {
            if (string.IsNullOrEmpty(fullMessage)) return false;
            if (fullMessage.IndexOf("Debug:Log (", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (fullMessage.IndexOf("UnityEngine.Debug:Log (", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>
        /// Applies the "one level lower" remapping for filtering, like the old version.
        /// This ensures compatibility with the filtering logic that expects remapped types.
        /// </summary>
        static LogType GetRemappedTypeForFiltering(LogType unityType)
        {
            switch (unityType)
            {
                case LogType.Error:
                    return LogType.Warning; // Error becomes Warning
                case LogType.Warning:
                    return LogType.Log; // Warning becomes Log
                case LogType.Assert:
                    return LogType.Assert; // Assert remains Assert
                case LogType.Log:
                    return LogType.Log; // Log remains Log
                case LogType.Exception:
                    return LogType.Warning; // Exception becomes Warning
                default:
                    return LogType.Log; // Default fallback
            }
        }

        /// <summary>
        /// Attempts to extract the stack trace part from a log message.
        /// Unity log messages often have the stack trace appended after the main message,
        /// starting on a new line and typically indented or beginning with "at ".
        /// </summary>
        /// <param name="fullMessage">The complete log message including potential stack trace.</param>
        /// <returns>The extracted stack trace string, or null if none is found.</returns>
        static string ExtractStackTrace(string fullMessage)
        {
            if (string.IsNullOrEmpty(fullMessage))
                return null;

            // Split into lines, removing empty ones to handle different line endings gracefully.
            // Using StringSplitOptions.None might be better if empty lines matter within stack trace, but RemoveEmptyEntries is usually safer here.
            string[] lines = fullMessage.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries
            );

            // If there's only one line or less, there's no separate stack trace.
            if (lines.Length <= 1)
                return null;

            int stackStartIndex = -1;

            // Start checking from the second line onwards.
            for (int i = 1; i < lines.Length; ++i)
            {
                // Performance: TrimStart creates a new string. Consider using IsWhiteSpace check if performance critical.
                string trimmedLine = lines[i].TrimStart();

                // Check for common stack trace patterns.
                if (
                    trimmedLine.StartsWith("at ")
                    || trimmedLine.StartsWith("UnityEngine.")
                    || trimmedLine.StartsWith("UnityEditor.")
                    || trimmedLine.Contains("(at ")
                    || // Covers "(at Assets/..." pattern
                    // Heuristic: Check if line starts with likely namespace/class pattern (Uppercase.Something)
                    (
                        trimmedLine.Length > 0
                        && char.IsUpper(trimmedLine[0])
                        && trimmedLine.Contains('.')
                    )
                )
                {
                    stackStartIndex = i;
                    break; // Found the likely start of the stack trace
                }
            }

            // If a potential start index was found...
            if (stackStartIndex > 0)
            {
                // Join the lines from the stack start index onwards using standard newline characters.
                // This reconstructs the stack trace part of the message.
                return string.Join("\n", lines.Skip(stackStartIndex));
            }

            // No clear stack trace found based on the patterns.
            return null;
        }

        static int ResolveCursor(int? cursor, string sinceTimestamp)
        {
            if (cursor.HasValue)
                return Math.Max(0, cursor.Value);

            if (string.IsNullOrWhiteSpace(sinceTimestamp))
                return 0;

            var trimmed = sinceTimestamp.Trim();
            if (trimmed.StartsWith("cursor:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(trimmed.Substring("cursor:".Length), out var cursorFromSince))
            {
                return Math.Max(0, cursorFromSince);
            }

            return int.TryParse(trimmed, out var numericCursor) ? Math.Max(0, numericCursor) : 0;
        }

        static string CreateFingerprint(string message, string stackTrace, string type, string file, int? line)
        {
            var normalizedMessage = (message ?? string.Empty).Trim();
            var topFrame = GetTopFrame(stackTrace);
            return PayloadBudgeting.ComputeSha256($"{type}|{normalizedMessage}|{topFrame}|{file}|{line}");
        }

        static string GetTopFrame(string stackTrace)
        {
            if (string.IsNullOrWhiteSpace(stackTrace))
                return string.Empty;

            return stackTrace.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .FirstOrDefault(line => !string.IsNullOrEmpty(line)) ?? string.Empty;
        }

        /* LogEntry.mode bits exploration (based on Unity decompilation/observation):
           May change between versions.

           Basic Types:
           kError = 1 << 0 (1)
           kAssert = 1 << 1 (2)
           kWarning = 1 << 2 (4)
           kLog = 1 << 3 (8)
           kFatal = 1 << 4 (16) - Often treated as Exception/Error

           Modifiers/Context:
           kAssetImportError = 1 << 7 (128)
           kAssetImportWarning = 1 << 8 (256)
           kScriptingError = 1 << 9 (512)
           kScriptingWarning = 1 << 10 (1024)
           kScriptingLog = 1 << 11 (2048)
           kScriptCompileError = 1 << 12 (4096)
           kScriptCompileWarning = 1 << 13 (8192)
           kStickyError = 1 << 14 (16384) - Stays visible even after Clear On Play
           kMayIgnoreLineNumber = 1 << 15 (32768)
           kReportBug = 1 << 16 (65536) - Shows the "Report Bug" button
           kDisplayPreviousErrorInStatusBar = 1 << 17 (131072)
           kScriptingException = 1 << 18 (262144)
           kDontExtractStacktrace = 1 << 19 (524288) - Hint to the console UI
           kShouldClearOnPlay = 1 << 20 (1048576) - Default behavior
           kGraphCompileError = 1 << 21 (2097152)
           kScriptingAssertion = 1 << 22 (4194304)
           kVisualScriptingError = 1 << 23 (8388608)

           Example observed values:
           Log: 2048 (ScriptingLog) or 8 (Log)
           Warning: 1028 (ScriptingWarning | Warning) or 4 (Warning)
           Error: 513 (ScriptingError | Error) or 1 (Error)
           Exception: 262161 (ScriptingException | Error | kFatal?) - Complex combination
           Assertion: 4194306 (ScriptingAssertion | Assert) or 2 (Assert)
        */
    }
}
