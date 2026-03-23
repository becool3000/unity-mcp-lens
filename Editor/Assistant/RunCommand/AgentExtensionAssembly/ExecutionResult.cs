using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.Agent.Dynamic.Extension.Editor
{
    [Serializable]
    struct ExecutionLog
    {
        public string Log;
        public LogType LogType;
        public int[] LoggedObjectInstanceIds;
        public string[] LoggedObjectNames;

        internal GameObject[] LoggedObjects
        {
            get
            {
                if (LoggedObjectInstanceIds == null || LoggedObjectInstanceIds.Length == 0)
                    return null;

                var loggedObjects = new GameObject[LoggedObjectInstanceIds.Length];
                for (var i = 0; i < LoggedObjectInstanceIds.Length; i++)
                {
                    var instanceId = LoggedObjectInstanceIds[i];
#if UNITY_6000_3_OR_NEWER
                    var obj = EditorUtility.EntityIdToObject(instanceId) as GameObject;
#else
                    var obj = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
#endif

                    // There is a chance for a clash, make sure the object is a GameObject and the name matches:
                    if (obj != null && LoggedObjectNames?.Length > i && LoggedObjectNames?[i] == obj.name)
                    {
                        loggedObjects[i] = obj;
                    }
                }
                return loggedObjects;
            }
        }

        public ExecutionLog(string formattedLog, LogType logType, object[] loggedObjects = null)
        {
            Log = formattedLog;
            LogType = logType;

            if (loggedObjects != null)
            {
                LoggedObjectInstanceIds = new int[loggedObjects.Length];
                for (var i = 0; i < loggedObjects.Length; i++)
                {
                    var loggedObject = loggedObjects[i];
                    var obj = loggedObject as Object;
                    if (obj != null)
                    {
                        LoggedObjectInstanceIds[i] = obj.GetInstanceID();
                    }
                }
            }
            else
            {
                LoggedObjectInstanceIds = null;
            }

            LoggedObjectNames = loggedObjects != null ? new string[loggedObjects.Length] : null;

            if (LoggedObjectNames != null)
            {
                for (int i = 0; i < loggedObjects.Length; i++)
                {
                    LoggedObjectNames[i] = loggedObjects[i] is Object obj ? obj.name : loggedObjects[i]?.ToString();
                }
            }
        }
    }

    [Serializable]
#if CODE_LIBRARY_INSTALLED
    public
#else
    internal
#endif
    class ExecutionResult
    {
        internal static readonly string LinkTextColor = EditorGUIUtility.isProSkin ? "#8facef" : "#055b9f";
        internal static readonly string WarningTextColor = EditorGUIUtility.isProSkin ? "#DFB33D" : "#B76300";

        public static readonly Regex PlaceholderRegex = new(@"\{(\d+)\}", RegexOptions.Compiled);

        int UndoGroup;

        public int Id = 1;
        public int MessageIndex = -1;

        public readonly string CommandName;

        public string FencedTag;

        public List<ExecutionLog> Logs = new();

        public string ConsoleLogs = string.Empty;

        public bool SuccessfullyStarted;

        public ExecutionResult(string commandName)
        {
            CommandName = commandName;
        }

        public void RegisterObjectCreation(Object objectCreated)
        {
            if (objectCreated != null)
                Undo.RegisterCreatedObjectUndo(objectCreated, $"{objectCreated.name} was created");
        }

        public void RegisterObjectCreation(Component component)
        {
            if (component != null)
                Undo.RegisterCreatedObjectUndo(component, $"{component} was attached to {component.gameObject.name}");
        }

        public void RegisterObjectModification(Object objectToRegister, string operationDescription = "")
        {
            if (!string.IsNullOrEmpty(operationDescription))
                Undo.RecordObject(objectToRegister, operationDescription);
            else
                Undo.RegisterCompleteObjectUndo(objectToRegister, $"{objectToRegister.name} was modified");
        }

        public void DestroyObject(Object objectToDestroy)
        {
            if (EditorUtility.IsPersistent(objectToDestroy))
            {
                var path = AssetDatabase.GetAssetPath(objectToDestroy);
                AssetDatabase.DeleteAsset(path);
            }
            else
            {
                if (!EditorApplication.isPlaying)
                    Undo.DestroyObjectImmediate(objectToDestroy);
                else
                    Object.Destroy(objectToDestroy);
            }
        }

        public void Start()
        {
            SuccessfullyStarted = true;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(CommandName ?? "Run command execution");
            UndoGroup = Undo.GetCurrentGroup();

            Application.logMessageReceived += HandleConsoleLog;
        }

        public void End()
        {
            Application.logMessageReceived -= HandleConsoleLog;

            Undo.CollapseUndoOperations(UndoGroup);
        }

        public void Log(string log, params object[] references)
        {
            Logs.Add(new ExecutionLog(log, LogType.Log, references));
        }

        public void LogWarning(string log, params object[] references)
        {
            Logs.Add(new ExecutionLog(log, LogType.Warning, references));
        }

        public void LogError(string log, params object[] references)
        {
            Logs.Add(new ExecutionLog(log, LogType.Error, references));
        }

        void HandleConsoleLog(string logString, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Warning)
            {
                ConsoleLogs += $"{type}: {logString}\n";
            }
        }

        public List<string> GetFormattedLogs()
        {
            List<string> formattedLogs = new();

            if (Logs == null)
            {
                return formattedLogs;
            }

            foreach (var content in Logs)
            {
                if (string.IsNullOrEmpty(content.Log))
                {
                    continue;
                }

                string logTemplate = content.Log;
                var references = content.LoggedObjects;
                var names = content.LoggedObjectNames;

                string formattedLog = PlaceholderRegex.Replace(logTemplate, match =>
                {
                    if (int.TryParse(match.Groups[1].Value, out int index))
                    {
                        // Unity Object: [displayText|InstanceID:instanceId] for clickable link in UI
                        if (references != null && index >= 0 && index < references.Length)
                        {
                            var obj = references[index];
                            if (obj != null)
                                return "[" + obj.name + "|InstanceID:" + obj.GetInstanceID() + "]";
                        }

                        // Non-Object (string, int, etc.): [displayText] only; renderer may detect paths and make them clickable
                        if (names != null && index >= 0 && index < names.Length && names[index] != null)
                            return "[" + names[index] + "]";
                    }

                    return match.Value;
                });
                formattedLogs.Add($"[{content.LogType}] {formattedLog}");
            }
            return formattedLogs;
        }
    }
}
