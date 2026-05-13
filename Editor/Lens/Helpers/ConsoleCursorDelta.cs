using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Utils;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Helpers
{
    sealed class ConsoleCursorSnapshot
    {
        public int Cursor { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public bool Available { get; set; }
        public string Error { get; set; }
    }

    static class ConsoleCursorDelta
    {
#if UNITY_6000_0_OR_NEWER
        const int ModeBitError = 1 << 8;
        const int ModeBitWarning = 1 << 9;
        const int ModeBitLog = 1 << 10;
#else
        const int ModeBitError = 1 << 0;
        const int ModeBitWarning = 1 << 2;
        const int ModeBitLog = 1 << 3;
#endif
        const int ModeBitAssert = 1 << 1;
        const int ModeBitException = 1 << 4;
        const int MaxInlineEntries = 5;

        public static ConsoleCursorSnapshot Capture()
        {
            if (!TryCreateReader(out var reader, out string error))
            {
                return new ConsoleCursorSnapshot
                {
                    Cursor = -1,
                    ErrorCount = -1,
                    WarningCount = -1,
                    Available = false,
                    Error = error
                };
            }

            int errorCount = 0;
            int warningCount = 0;
            int totalEntries = 0;
            try
            {
                reader.Start();
                totalEntries = reader.Count;
                for (int i = 0; i < totalEntries; i++)
                {
                    if (!reader.TryRead(i, out var entry))
                        continue;

                    if (entry.IsError)
                        errorCount++;
                    else if (entry.IsWarning)
                        warningCount++;
                }
            }
            catch (Exception ex)
            {
                return new ConsoleCursorSnapshot
                {
                    Cursor = -1,
                    ErrorCount = -1,
                    WarningCount = -1,
                    Available = false,
                    Error = ex.Message
                };
            }
            finally
            {
                reader.End();
            }

            return new ConsoleCursorSnapshot
            {
                Cursor = totalEntries,
                ErrorCount = errorCount,
                WarningCount = warningCount,
                Available = true
            };
        }

        public static object BuildDelta(bool enabled, ConsoleCursorSnapshot before, string toolName, object detailRefMeta = null)
        {
            if (!enabled)
                return null;

            before ??= Capture();
            string readerError = null;
            if (!before.Available || !TryCreateReader(out var reader, out readerError))
            {
                return new
                {
                    enabled = true,
                    available = false,
                    reason = before.Error ?? readerError,
                    newErrors = 0,
                    newWarnings = 0,
                    staleErrorsPresent = before.ErrorCount > 0,
                    staleWarningsPresent = before.WarningCount > 0,
                    cursorBefore = before.Cursor,
                    cursorAfter = before.Cursor,
                    scannedCount = 0,
                    consoleDeltaRef = (object)null,
                    consoleErrorsDetected = false
                };
            }

            int totalEntries = 0;
            var entries = new List<ConsoleDeltaEntry>();
            int start = Math.Max(0, before.Cursor);
            try
            {
                reader.Start();
                totalEntries = reader.Count;
                for (int i = start; i < totalEntries; i++)
                {
                    if (!reader.TryRead(i, out var entry))
                        continue;

                    if (ConsoleNoiseFilter.ShouldExclude(entry.Message, null))
                        continue;

                    entries.Add(entry);
                }
            }
            catch (Exception ex)
            {
                return new
                {
                    enabled = true,
                    available = false,
                    reason = ex.Message,
                    newErrors = 0,
                    newWarnings = 0,
                    staleErrorsPresent = before.ErrorCount > 0,
                    staleWarningsPresent = before.WarningCount > 0,
                    cursorBefore = before.Cursor,
                    cursorAfter = totalEntries > 0 ? totalEntries : before.Cursor,
                    scannedCount = 0,
                    consoleDeltaRef = (object)null,
                    consoleErrorsDetected = false
                };
            }
            finally
            {
                reader.End();
            }

            int newErrors = entries.Count(entry => entry.IsError);
            int newWarnings = entries.Count(entry => entry.IsWarning);
            var inlineEntries = entries
                .Where(entry => entry.IsError || entry.IsWarning)
                .Take(MaxInlineEntries)
                .Select(entry => new
                {
                    type = entry.Type,
                    message = entry.Message,
                    file = entry.File,
                    line = entry.Line
                })
                .ToArray();
            object consoleDeltaRef = CreateConsoleDeltaRef(toolName, entries, detailRefMeta);

            return new
            {
                enabled = true,
                available = true,
                newErrors,
                newWarnings,
                staleErrorsPresent = before.ErrorCount > 0,
                staleWarningsPresent = before.WarningCount > 0,
                cursorBefore = before.Cursor,
                cursorAfter = totalEntries,
                scannedCount = entries.Count,
                inlineEntries,
                consoleDeltaRef,
                consoleErrorsDetected = newErrors > 0,
                initialConsoleErrorCount = before.ErrorCount,
                finalConsoleErrorCount = Math.Max(0, before.ErrorCount) + newErrors,
                newConsoleErrorCount = newErrors
            };
        }

        static object CreateConsoleDeltaRef(string toolName, IReadOnlyList<ConsoleDeltaEntry> entries, object detailRefMeta)
        {
            if (entries == null || entries.Count <= MaxInlineEntries)
                return null;

            string rawJson = JsonConvert.SerializeObject(entries, Formatting.None);
            int rawBytes = PayloadBudgeting.GetUtf8ByteCount(rawJson);
            return ToolResultCompactor.CreateStoredDetailRef(
                toolName,
                new
                {
                    entries
                },
                rawBytes,
                detailRefMeta ?? new { kind = "console_delta_entries" });
        }

        static bool TryCreateReader(out ConsoleReader reader, out string error)
        {
            reader = null;
            error = null;
            try
            {
                var logEntriesType = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries");
                var logEntryType = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntry");
                if (logEntriesType == null || logEntryType == null)
                {
                    error = "UnityEditor.LogEntries or UnityEditor.LogEntry type was not found.";
                    return false;
                }

                var staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                var instanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var start = logEntriesType.GetMethod("StartGettingEntries", staticFlags);
                var end = logEntriesType.GetMethod("EndGettingEntries", staticFlags);
                var getCount = logEntriesType.GetMethod("GetCount", staticFlags);
                var getEntry = logEntriesType.GetMethod("GetEntryInternal", staticFlags);
                var modeField = logEntryType.GetField("mode", instanceFlags);
                var messageField = logEntryType.GetField("message", instanceFlags);
                var fileField = logEntryType.GetField("file", instanceFlags);
                var lineField = logEntryType.GetField("line", instanceFlags);
                if (start == null || end == null || getCount == null || getEntry == null || modeField == null || messageField == null)
                {
                    error = "Required Unity console reflection members were not found.";
                    return false;
                }

                reader = new ConsoleReader(start, end, getCount, getEntry, logEntryType, modeField, messageField, fileField, lineField);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        static bool IsErrorMode(int mode)
        {
            return (mode & ModeBitException) != 0 ||
                (mode & ModeBitError) != 0 ||
                (mode & ModeBitAssert) != 0;
        }

        static bool IsWarningMode(int mode) => (mode & ModeBitWarning) != 0;

        static bool LooksLikeCompilerError(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            return message.IndexOf(" error CS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf(": error CS", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        sealed class ConsoleReader
        {
            readonly MethodInfo m_Start;
            readonly MethodInfo m_End;
            readonly MethodInfo m_GetCount;
            readonly MethodInfo m_GetEntry;
            readonly Type m_LogEntryType;
            readonly FieldInfo m_ModeField;
            readonly FieldInfo m_MessageField;
            readonly FieldInfo m_FileField;
            readonly FieldInfo m_LineField;
            readonly object m_LogEntry;

            public ConsoleReader(
                MethodInfo start,
                MethodInfo end,
                MethodInfo getCount,
                MethodInfo getEntry,
                Type logEntryType,
                FieldInfo modeField,
                FieldInfo messageField,
                FieldInfo fileField,
                FieldInfo lineField)
            {
                m_Start = start;
                m_End = end;
                m_GetCount = getCount;
                m_GetEntry = getEntry;
                m_LogEntryType = logEntryType;
                m_ModeField = modeField;
                m_MessageField = messageField;
                m_FileField = fileField;
                m_LineField = lineField;
                m_LogEntry = Activator.CreateInstance(m_LogEntryType);
            }

            public int Count => (int)m_GetCount.Invoke(null, null);

            public void Start() => m_Start.Invoke(null, null);

            public void End()
            {
                try { m_End.Invoke(null, null); } catch { }
            }

            public bool TryRead(int index, out ConsoleDeltaEntry entry)
            {
                entry = null;
                m_GetEntry.Invoke(null, new object[] { index, m_LogEntry });
                int mode = (int)m_ModeField.GetValue(m_LogEntry);
                string message = m_MessageField.GetValue(m_LogEntry) as string;
                if (string.IsNullOrWhiteSpace(message))
                    return false;

                bool isError = IsErrorMode(mode) || LooksLikeCompilerError(message);
                bool isWarning = !isError && IsWarningMode(mode);
                entry = new ConsoleDeltaEntry
                {
                    Type = isError ? "Error" : isWarning ? "Warning" : "Log",
                    Message = FirstLine(message),
                    File = m_FileField?.GetValue(m_LogEntry) as string,
                    Line = m_LineField != null ? (int?)m_LineField.GetValue(m_LogEntry) : null,
                    IsError = isError,
                    IsWarning = isWarning
                };
                return true;
            }

            static string FirstLine(string message)
            {
                return string.IsNullOrWhiteSpace(message)
                    ? string.Empty
                    : message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? message;
            }
        }

        sealed class ConsoleDeltaEntry
        {
            public string Type { get; set; }
            public string Message { get; set; }
            public string File { get; set; }
            public int? Line { get; set; }
            public bool IsError { get; set; }
            public bool IsWarning { get; set; }
        }
    }
}
