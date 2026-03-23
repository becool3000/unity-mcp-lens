using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Bridge.Editor
{
    internal static partial class ConsoleUtils
    {
#if UNITY_6000_3_OR_NEWER
        static FieldInfo s_ConsoleListViewField;
        static ConsoleWindow s_ConsoleWindow;
        static ListViewState s_ConsoleWindowListViewState;
        static readonly LogEntry s_Entry = new();
#endif

        internal static event Action<LogData> s_EntryContextClickedEvent;
        internal static event Action s_DrawCustomToolbarGuiEvent;

        static bool s_TimestampsApiAvailable;
        static MethodInfo s_GetEntryTimestampMethod;
        static MethodInfo s_HasFlagMethod;
        static MethodInfo s_SetFlagMethod;
        static object[] s_HasFlagParameters;
        static object s_ShowTimestampFlag;
#if UNITY_6000_3_OR_NEWER
        static object s_LogLevelLogFlag;
        static object s_LogLevelWarningFlag;
        static object s_LogLevelErrorFlag;
#endif

        static readonly string s_MessageWithTimestamp = string.Empty;
        static readonly object[] s_GetEntryTimestampParameters = new object[2];

        static readonly List<LogData> s_ReusableResultslist = new();

#if UNITY_6000_3_OR_NEWER
        /// <summary>
        /// Returns the console window's ListViewState field info.
        /// </summary>
        /// <remarks>this method is internal for testing purposes</remarks>
        internal static FieldInfo GetConsoleWindowSelectionState()
        {
            if ((s_ConsoleListViewField ??= typeof(ConsoleWindow)
                    .GetField("m_ListView", BindingFlags.Instance | BindingFlags.NonPublic)) == null)
                return null;
            return s_ConsoleListViewField;
        }

        internal static T GetOpenWindow<T>() where T : EditorWindow
        {
            T[] windows = Resources.FindObjectsOfTypeAll<T>();
            if (windows != null && windows.Length > 0)
            {
                return windows[0];
            }
            return null;
        }

        internal static bool UpdateConsoleWindow()
        {
            var currentConsoleWindow = GetOpenWindow<ConsoleWindow>();
            // null if no console window can be found
            if (!ReferenceEquals(currentConsoleWindow, s_ConsoleWindow))
            {
                s_ConsoleWindow = currentConsoleWindow;
                if (s_ConsoleWindow == null)
                    return false;
                var consoleWindowSelectionState = GetConsoleWindowSelectionState();
                s_ConsoleWindowListViewState = consoleWindowSelectionState.GetValue(s_ConsoleWindow) as ListViewState;
            }
            else
            {
                if (s_ConsoleWindow == null)
                    return false;
            }

            // null if the m_ListView private field has been renamed or its type has changed</returns>
            if (s_ConsoleWindowListViewState == null)
                return false;

            return true;
        }

        internal static void GetSelectedConsoleLogs(List<LogData> results, string searchFilter = null)
        {
            results.Clear();

            if (!UpdateConsoleWindow())
            {
                return;
            }

            // no array allocation in any case.
            // true if the console window row with the same index is selected.
            bool[] selectedRows = s_ConsoleWindowListViewState.selectedItems;

            if (selectedRows == null)
            {
                return;
            }

            for (int i = 0; i < selectedRows.Length; i++)
            {
                if (!selectedRows[i])
                    continue;

                if (LogEntries.GetEntryInternal(i, s_Entry))
                {
                    if (!DoesLogEntryMatchFilter(s_Entry, searchFilter))
                        continue;

                    var entryToAdd = LogEntryToInternal(s_Entry);

                    TryAddMessageWithTimestamp(ref entryToAdd, i);

                    results.Add(entryToAdd);
                }
            }
        }

        internal static void SelectConsoleLog(LogData entryToSelect)
        {
            EditorWindow.GetWindow<ConsoleWindow>();

            if (!UpdateConsoleWindow())
            {
                return;
            }

            int entryCount = LogEntries.GetCount();

            for (int i = 0; i < entryCount; i++)
            {
                if (LogEntries.GetEntryInternal(i, s_Entry))
                {
                    var entry = LogEntryToInternal(s_Entry);

                    // We only test two fields, since they suffice and AssistantContextEntry cannot provide other fields
                    if (entry.Type.Equals((entryToSelect.Type)) && entry.Message.Equals(entryToSelect.Message))
                    {
                        s_ConsoleWindowListViewState.selectedItems = new bool[i + 1];
                        s_ConsoleWindowListViewState.selectedItems[i] = true;
                        s_ConsoleWindowListViewState.selectionChanged = true;

                        s_ConsoleWindowListViewState.row = i;

                        s_ConsoleWindowListViewState.scrollPos = new Vector2(0f, i * s_ConsoleWindowListViewState.rowHeight);

                        s_ConsoleWindow.Repaint();

                        break;
                    }
                }
            }
        }

        static void TryAddMessageWithTimestamp(ref LogData entry, int i)
        {
            if (s_TimestampsApiAvailable)
            {
                s_GetEntryTimestampParameters[0] = i;
                s_GetEntryTimestampParameters[1] = s_MessageWithTimestamp;
                s_GetEntryTimestampMethod.Invoke(null, s_GetEntryTimestampParameters);
                entry.MessageWithTimestamp = (string) s_GetEntryTimestampParameters[1];
            }
            else
            {
                entry.MessageWithTimestamp = null;
            }
        }

        internal static int GetSelectedConsoleLogCount(string searchFilter = null)
        {
            if (!UpdateConsoleWindow())
                return 0;

            if (string.IsNullOrEmpty(searchFilter))
            {
                bool[] selectedRows = s_ConsoleWindowListViewState.selectedItems;
                int resultCount = 0;
                if (selectedRows == null)
                    return 0;

                var numRows = LogEntries.GetCount();

                for (int i = 0; i < selectedRows.Length; i++)
                {
                    if (i >= numRows)
                        break;

                    bool isRowSelected = selectedRows[i];
                    if (isRowSelected)
                    {
                        resultCount++;
                    }
                }

                return resultCount;
            }

            GetSelectedConsoleLogs(s_ReusableResultslist, searchFilter);
            return s_ReusableResultslist.Count;
        }

        internal static int GetConsoleLogCount(string searchFilter)
        {
            if (!UpdateConsoleWindow())
                return 0;

            if (string.IsNullOrEmpty(searchFilter))
            {
                return s_ConsoleWindowListViewState.totalRows;
            }

            GetConsoleLogs(s_ReusableResultslist, searchFilter);
            return s_ReusableResultslist.Count;
        }

        static bool DoesLogEntryMatchFilter(LogEntry entry, string searchFilter)
        {
            if (string.IsNullOrEmpty(searchFilter))
                return true;

            return entry.message.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (!string.IsNullOrEmpty(entry.file) &&
                    entry.file.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static bool GetConsoleFlag(object flag)
        {
            if (s_HasFlagMethod == null || flag == null) return true;
            return s_HasFlagMethod.Invoke(null, new[] { flag }) is bool b && b;
        }

        static void SetConsoleFlag(object flag, bool val)
        {
            if (s_SetFlagMethod == null || flag == null) return;
            s_SetFlagMethod.Invoke(null, new[] { flag, (object)val });
        }

        internal static void GetConsoleLogs(List<LogData> results, string searchFilter = null,
            bool? includeLog = null, bool? includeWarning = null, bool? includeError = null)
        {
            results.Clear();

            bool hadLog = false, hadWarning = false, hadError = false;
            if (includeLog.HasValue)
            {
                hadLog = GetConsoleFlag(s_LogLevelLogFlag);
                SetConsoleFlag(s_LogLevelLogFlag, includeLog.Value);
            }
            if (includeWarning.HasValue)
            {
                hadWarning = GetConsoleFlag(s_LogLevelWarningFlag);
                SetConsoleFlag(s_LogLevelWarningFlag, includeWarning.Value);
            }
            if (includeError.HasValue)
            {
                hadError = GetConsoleFlag(s_LogLevelErrorFlag);
                SetConsoleFlag(s_LogLevelErrorFlag, includeError.Value);
            }

            try
            {
                int entryCount = LogEntries.GetCount();

                for (int i = 0; i < entryCount; i++)
                {
                    if (LogEntries.GetEntryInternal(i, s_Entry))
                    {
                        if (!DoesLogEntryMatchFilter(s_Entry, searchFilter))
                            continue;

                        var entryToAdd = LogEntryToInternal(s_Entry);

                        TryAddMessageWithTimestamp(ref entryToAdd, i);

                        results.Add(entryToAdd);
                    }
                }
            }
            finally
            {
                if (includeLog.HasValue)
                    SetConsoleFlag(s_LogLevelLogFlag, hadLog);
                if (includeWarning.HasValue)
                    SetConsoleFlag(s_LogLevelWarningFlag, hadWarning);
                if (includeError.HasValue)
                    SetConsoleFlag(s_LogLevelErrorFlag, hadError);
            }
        }

        static LogData LogEntryToInternal(LogEntry entry)
        {
            var internalEntry = new LogData
            {
                Message = entry.message,
                File = entry.file,
                Line = entry.line,
                Column = entry.column
            };

            var mode = (ConsoleWindow.Mode) entry.mode;
            if ((mode & (ConsoleWindow.Mode.Error | ConsoleWindow.Mode.Assert |
                                                   ConsoleWindow.Mode.Fatal | ConsoleWindow.Mode.AssetImportError |
                                                   ConsoleWindow.Mode.ScriptingError |
                                                   ConsoleWindow.Mode.ScriptCompileError |
                                                   ConsoleWindow.Mode.ScriptingException |
                                                   ConsoleWindow.Mode.GraphCompileError |
                                                   ConsoleWindow.Mode.ScriptingAssertion |
                                                   ConsoleWindow.Mode.StickyError | ConsoleWindow.Mode.ReportBug |
                                                   ConsoleWindow.Mode.DisplayPreviousErrorInStatusBar |
                                                   ConsoleWindow.Mode.VisualScriptingError
                                                   )) != 0)
            {
                internalEntry.Type = LogDataType.Error;
            }
            else if ((mode & (ConsoleWindow.Mode.AssetImportWarning |
                              ConsoleWindow.Mode.ScriptingWarning |
                              ConsoleWindow.Mode.ScriptCompileWarning)) != 0)
            {
                internalEntry.Type = LogDataType.Warning;
            }
            else
            {
                internalEntry.Type = LogDataType.Info;
            }

            return internalEntry;
        }

        internal static bool IsConsoleShowingTimestamps()
        {
            if (!s_TimestampsApiAvailable)
                return false;

            return (bool) s_HasFlagMethod.Invoke(null, s_HasFlagParameters);
        }

        [InitializeOnLoadMethod]
        static void Initialize()
        {
            InitConsoleIntegrationDelegates();
            InitializeReflection();
        }

        static void InitConsoleIntegrationDelegates()
        {
            var consoleWindowType = typeof(ConsoleWindow);
            var entryContextClickedField = consoleWindowType.GetField("entryContextClicked",
                BindingFlags.Static | BindingFlags.NonPublic);

            if (entryContextClickedField != null)
            {
                var entryContextClickedDelegate = entryContextClickedField.GetValue(null) as Action<LogEntry>;
                var combinedDelegate = (Action<LogEntry>) Delegate.Combine(entryContextClickedDelegate, (Action<LogEntry>) OnContextClicked);

                entryContextClickedField.SetValue(null, combinedDelegate);

                void OnContextClicked(LogEntry entry)
                {
                    var results = new List<LogData>();
                    GetConsoleLogs(results);

                    var logData = LogEntryToInternal(entry);

                    s_EntryContextClickedEvent?.Invoke(logData);
                }
            }

            var drawCustomToolbarGuiField = consoleWindowType.GetField("drawCustomToolbarGui",
                BindingFlags.Static | BindingFlags.NonPublic);

            if (drawCustomToolbarGuiField != null)
            {
                var drawCustomToolbarGuiDelegate = drawCustomToolbarGuiField.GetValue(null) as Action;
                var combinedDelegate = (Action) Delegate.Combine(drawCustomToolbarGuiDelegate, (Action) OnDrawCustomToolbarGui);

                drawCustomToolbarGuiField.SetValue(null, combinedDelegate);

                void OnDrawCustomToolbarGui()
                {
                    s_DrawCustomToolbarGuiEvent?.Invoke();
                }
            }
        }

        static void InitializeReflection()
        {
            var logEntriesType = typeof(LogEntries);
            s_GetEntryTimestampMethod = logEntriesType.GetMethod("GetEntryTimestampInternal", BindingFlags.Static | BindingFlags.NonPublic);

            var consoleWindowType = typeof(ConsoleWindow);
            s_HasFlagMethod = consoleWindowType.GetMethod("HasFlag", BindingFlags.Static | BindingFlags.NonPublic);

            s_SetFlagMethod = consoleWindowType.GetMethod("SetFlag", BindingFlags.Static | BindingFlags.NonPublic);

            var consoleFlagsType = consoleWindowType.GetNestedType("ConsoleFlags", BindingFlags.NonPublic);
            if (consoleFlagsType != null)
            {
                Enum.TryParse(consoleFlagsType, "ShowTimestamp", out s_ShowTimestampFlag);
                Enum.TryParse(consoleFlagsType, "LogLevelLog", out s_LogLevelLogFlag);
                Enum.TryParse(consoleFlagsType, "LogLevelWarning", out s_LogLevelWarningFlag);
                Enum.TryParse(consoleFlagsType, "LogLevelError", out s_LogLevelErrorFlag);
            }

            s_TimestampsApiAvailable = s_GetEntryTimestampMethod != null && s_HasFlagMethod != null &&
                                       consoleFlagsType != null && s_ShowTimestampFlag != null;

            if (s_TimestampsApiAvailable)
            {
                s_HasFlagParameters = new[] { s_ShowTimestampFlag };
            }
        }
#endif

        internal static int FindLogEntry(List<LogData> entries, LogData entry)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var l = entries[i];
                if (l.Message.Equals(entry.Message) && l.Type == entry.Type)
                {
                    return i;
                }
            }

            return -1;
        }

        internal static bool HasEqualLogEntries(HashSet<LogData> hashSet, HashSet<LogData> searchedHash)
        {
            foreach (var entry in hashSet)
            {
                if (!searchedHash.Contains(entry))
                {
                    return false;
                }
            }

            return true;
        }

        internal static void HashEntries(List<LogData> entries, HashSet<LogData> hashSet)
        {
            hashSet.Clear();

            foreach (var l in entries)
            {
                hashSet.Add(l);
            }
        }
    }
}
