#if !UNITY_6000_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Bridge.Editor
{
    internal static partial class ConsoleUtils
    {
        static Type s_ConsoleWindowType;
        static Type s_ListViewStateType;
        static Type s_LogEntryType;
        static Type s_LogEntriesType;

        static FieldInfo s_ConsoleListViewField;
        static EditorWindow s_ConsoleWindow;
        static object s_ConsoleWindowListViewState;
        static object s_Entry;

        static FieldInfo s_LogEntryMessageField;
        static FieldInfo s_LogEntryFileField;
        static FieldInfo s_LogEntryLineField;
        static FieldInfo s_LogEntryColumnField;
        static FieldInfo s_LogEntryModeField;

        static FieldInfo s_ListViewStateSelectedItemsField;
        static FieldInfo s_ListViewStateSelectionChangedField;
        static FieldInfo s_ListViewStateRowField;
        static FieldInfo s_ListViewStateScrollPosField;
        static FieldInfo s_ListViewStateRowHeightField;
        static FieldInfo s_ListViewStateTotalRowsField;

        static MethodInfo s_LogEntriesGetCountMethod;
        static MethodInfo s_LogEntriesGetEntryInternalMethod;

        static MethodInfo s_ConsoleWindowHasFlagMethod;
        static MethodInfo s_ConsoleWindowSetFlagMethod;
        static object s_LogLevelLogFlag;
        static object s_LogLevelWarningFlag;
        static object s_LogLevelErrorFlag;

        static int s_ErrorModeFlags;
        static int s_WarningModeFlags;
        static bool s_Initialized;

        static void EnsureInitialized()
        {
            if (s_Initialized) return;
            s_Initialized = true;

            var asm = typeof(EditorWindow).Assembly;
            s_ConsoleWindowType = asm.GetType("UnityEditor.ConsoleWindow");
            s_ListViewStateType = asm.GetType("UnityEditor.ListViewState");
            s_LogEntryType = asm.GetType("UnityEditor.LogEntry");
            s_LogEntriesType = asm.GetType("UnityEditor.LogEntries");

            if (s_LogEntryType != null)
            {
                s_LogEntryMessageField = s_LogEntryType.GetField("message", BindingFlags.Public | BindingFlags.Instance);
                s_LogEntryFileField = s_LogEntryType.GetField("file", BindingFlags.Public | BindingFlags.Instance);
                s_LogEntryLineField = s_LogEntryType.GetField("line", BindingFlags.Public | BindingFlags.Instance);
                s_LogEntryColumnField = s_LogEntryType.GetField("column", BindingFlags.Public | BindingFlags.Instance);
                s_LogEntryModeField = s_LogEntryType.GetField("mode", BindingFlags.Public | BindingFlags.Instance);
                s_Entry = Activator.CreateInstance(s_LogEntryType);
            }

            if (s_ListViewStateType != null)
            {
                s_ListViewStateSelectedItemsField = s_ListViewStateType.GetField("selectedItems", BindingFlags.Public | BindingFlags.Instance);
                s_ListViewStateSelectionChangedField = s_ListViewStateType.GetField("selectionChanged", BindingFlags.Public | BindingFlags.Instance);
                s_ListViewStateRowField = s_ListViewStateType.GetField("row", BindingFlags.Public | BindingFlags.Instance);
                s_ListViewStateScrollPosField = s_ListViewStateType.GetField("scrollPos", BindingFlags.Public | BindingFlags.Instance);
                s_ListViewStateRowHeightField = s_ListViewStateType.GetField("rowHeight", BindingFlags.Public | BindingFlags.Instance);
                s_ListViewStateTotalRowsField = s_ListViewStateType.GetField("totalRows", BindingFlags.Public | BindingFlags.Instance);
            }

            if (s_LogEntriesType != null)
            {
                s_LogEntriesGetCountMethod = s_LogEntriesType.GetMethod("GetCount", BindingFlags.Public | BindingFlags.Static);
                s_LogEntriesGetEntryInternalMethod = s_LogEntriesType.GetMethod("GetEntryInternal", BindingFlags.Public | BindingFlags.Static);
            }

            if (s_ConsoleWindowType != null)
            {
                s_ConsoleListViewField = s_ConsoleWindowType.GetField("m_ListView", BindingFlags.Instance | BindingFlags.NonPublic);
                s_ConsoleWindowHasFlagMethod = s_ConsoleWindowType.GetMethod("HasFlag", BindingFlags.Static | BindingFlags.NonPublic);
                s_ConsoleWindowSetFlagMethod = s_ConsoleWindowType.GetMethod("SetFlag", BindingFlags.Static | BindingFlags.NonPublic);
                var consoleFlagsType = s_ConsoleWindowType.GetNestedType("ConsoleFlags", BindingFlags.NonPublic);
                if (consoleFlagsType != null)
                {
                    Enum.TryParse(consoleFlagsType, "LogLevelLog", out s_LogLevelLogFlag);
                    Enum.TryParse(consoleFlagsType, "LogLevelWarning", out s_LogLevelWarningFlag);
                    Enum.TryParse(consoleFlagsType, "LogLevelError", out s_LogLevelErrorFlag);
                }

                var modeType = s_ConsoleWindowType.GetNestedType("Mode", BindingFlags.Public | BindingFlags.NonPublic);
                if (modeType != null)
                {
                    s_ErrorModeFlags = GetModeFlag(modeType, "Error") | GetModeFlag(modeType, "Assert") | GetModeFlag(modeType, "Fatal") |
                                       GetModeFlag(modeType, "ScriptingError") | GetModeFlag(modeType, "ScriptCompileError") |
                                       GetModeFlag(modeType, "ScriptingException");
                    s_WarningModeFlags = GetModeFlag(modeType, "ScriptingWarning") | GetModeFlag(modeType, "ScriptCompileWarning");
                }
            }
        }

        static int GetModeFlag(Type modeType, string name)
        {
            var field = modeType.GetField(name, BindingFlags.Public | BindingFlags.Static);
            return field != null ? (int)field.GetValue(null) : 0;
        }

        internal static FieldInfo GetConsoleWindowSelectionState()
        {
            EnsureInitialized();
            return s_ConsoleListViewField;
        }

        internal static T GetOpenWindow<T>() where T : EditorWindow
        {
            var windows = Resources.FindObjectsOfTypeAll<T>();
            return windows != null && windows.Length > 0 ? windows[0] : null;
        }

        static EditorWindow GetOpenConsoleWindow()
        {
            EnsureInitialized();
            if (s_ConsoleWindowType == null) return null;
            var windows = Resources.FindObjectsOfTypeAll(s_ConsoleWindowType);
            return windows != null && windows.Length > 0 ? windows[0] as EditorWindow : null;
        }

        internal static bool UpdateConsoleWindow()
        {
            EnsureInitialized();
            var current = GetOpenConsoleWindow();
            if (!ReferenceEquals(current, s_ConsoleWindow))
            {
                s_ConsoleWindow = current;
                if (s_ConsoleWindow == null) return false;
                s_ConsoleWindowListViewState = s_ConsoleListViewField?.GetValue(s_ConsoleWindow);
            }
            return s_ConsoleWindow != null && s_ConsoleWindowListViewState != null;
        }

        static int GetLogEntriesCount() =>
            s_LogEntriesGetCountMethod?.Invoke(null, null) is int c ? c : 0;

        static bool GetEntryInternal(int index, object entry) =>
            s_LogEntriesGetEntryInternalMethod?.Invoke(null, new[] { index, entry }) is bool b && b;

        internal static void GetSelectedConsoleLogs(List<LogData> results, string searchFilter = null)
        {
            results.Clear();
            if (!UpdateConsoleWindow()) return;
            var selected = s_ListViewStateSelectedItemsField?.GetValue(s_ConsoleWindowListViewState) as bool[];
            if (selected == null) return;

            for (int i = 0; i < selected.Length; i++)
            {
                if (!selected[i]) continue;
                if (GetEntryInternal(i, s_Entry))
                {
                    var entry = LogEntryToInternal(s_Entry);
                    if (MatchesFilter(entry, searchFilter))
                        results.Add(entry);
                }
            }
        }

        internal static void SelectConsoleLog(LogData entryToSelect)
        {
            EnsureInitialized();
            if (s_ConsoleWindowType == null) return;
            EditorWindow.GetWindow(s_ConsoleWindowType);
            if (!UpdateConsoleWindow()) return;

            int count = GetLogEntriesCount();
            for (int i = 0; i < count; i++)
            {
                if (!GetEntryInternal(i, s_Entry)) continue;
                var entry = LogEntryToInternal(s_Entry);
                if (entry.Type == entryToSelect.Type && entry.Message == entryToSelect.Message)
                {
                    var newSelected = new bool[i + 1];
                    newSelected[i] = true;
                    s_ListViewStateSelectedItemsField?.SetValue(s_ConsoleWindowListViewState, newSelected);
                    s_ListViewStateSelectionChangedField?.SetValue(s_ConsoleWindowListViewState, true);
                    s_ListViewStateRowField?.SetValue(s_ConsoleWindowListViewState, i);
                    var rowHeight = s_ListViewStateRowHeightField?.GetValue(s_ConsoleWindowListViewState);
                    if (rowHeight != null)
                        s_ListViewStateScrollPosField?.SetValue(s_ConsoleWindowListViewState, new Vector2(0f, i * (int)rowHeight));
                    s_ConsoleWindow.Repaint();
                    break;
                }
            }
        }

        internal static int GetSelectedConsoleLogCount(string searchFilter = null)
        {
            if (!UpdateConsoleWindow()) return 0;
            if (string.IsNullOrEmpty(searchFilter))
            {
                var selected = s_ListViewStateSelectedItemsField?.GetValue(s_ConsoleWindowListViewState) as bool[];
                if (selected == null) return 0;
                int count = 0, total = GetLogEntriesCount();
                for (int i = 0; i < selected.Length && i < total; i++)
                    if (selected[i]) count++;
                return count;
            }
            GetSelectedConsoleLogs(s_ReusableResultslist, searchFilter);
            return s_ReusableResultslist.Count;
        }

        internal static int GetConsoleLogCount(string searchFilter)
        {
            if (!UpdateConsoleWindow()) return 0;
            if (string.IsNullOrEmpty(searchFilter))
                return s_ListViewStateTotalRowsField?.GetValue(s_ConsoleWindowListViewState) is int r ? r : 0;
            GetConsoleLogs(s_ReusableResultslist, searchFilter);
            return s_ReusableResultslist.Count;
        }

        static bool GetConsoleFlag(object flag)
        {
            if (s_ConsoleWindowHasFlagMethod == null || flag == null) return true;
            return s_ConsoleWindowHasFlagMethod.Invoke(null, new[] { flag }) is bool b && b;
        }

        static void SetConsoleFlag(object flag, bool val)
        {
            if (s_ConsoleWindowSetFlagMethod == null || flag == null) return;
            s_ConsoleWindowSetFlagMethod.Invoke(null, new[] { flag, (object)val });
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
                int count = GetLogEntriesCount();
                for (int i = 0; i < count; i++)
                {
                    if (!GetEntryInternal(i, s_Entry)) continue;
                    var entry = LogEntryToInternal(s_Entry);
                    if (!MatchesFilter(entry, searchFilter)) continue;
                    if (FindLogEntry(results, entry) < 0)
                        results.Add(entry);
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

        static bool MatchesFilter(LogData entry, string filter) =>
            string.IsNullOrEmpty(filter) ||
            entry.Message.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
            (!string.IsNullOrEmpty(entry.File) && entry.File.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);

        static LogData LogEntryToInternal(object entry)
        {
            var data = new LogData
            {
                Message = s_LogEntryMessageField?.GetValue(entry) as string ?? "",
                File = s_LogEntryFileField?.GetValue(entry) as string ?? "",
                Line = s_LogEntryLineField?.GetValue(entry) is int l ? l : 0,
                Column = s_LogEntryColumnField?.GetValue(entry) is int c ? c : 0
            };
            int mode = s_LogEntryModeField?.GetValue(entry) is int m ? m : 0;
            data.Type = (mode & s_ErrorModeFlags) != 0 ? LogDataType.Error :
                        (mode & s_WarningModeFlags) != 0 ? LogDataType.Warning : LogDataType.Info;
            return data;
        }

        internal static bool IsConsoleShowingTimestamps()
        {
            // Timestamps API not available in older Unity versions
            return false;
        }
    }
}
#endif
