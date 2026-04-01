using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text;
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

        public ExecutionLog(string logTemplate, LogType logType, object[] loggedObjects = null)
        {
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

            Log = ExecutionResult.FormatLogTemplate(logTemplate, loggedObjects);
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

        public static readonly Regex PlaceholderRegex = new(@"^(\d+)(?:,(-?\d+))?(?::(.+))?$", RegexOptions.Compiled);

        int UndoGroup;

        public int Id = 1;
        public int MessageIndex = -1;

        public readonly string CommandName;

        public string FencedTag;

        public List<ExecutionLog> Logs = new();

        public string ConsoleLogs;

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

        internal static string FormatLogTemplate(string logTemplate, object[] arguments)
        {
            if (string.IsNullOrEmpty(logTemplate) || arguments == null || arguments.Length == 0)
                return logTemplate;

            var builder = new StringBuilder(logTemplate.Length + 16);
            for (var index = 0; index < logTemplate.Length; index++)
            {
                var current = logTemplate[index];
                if (current == '{')
                {
                    if (index + 1 < logTemplate.Length && logTemplate[index + 1] == '{')
                    {
                        builder.Append('{');
                        index++;
                        continue;
                    }

                    var endIndex = logTemplate.IndexOf('}', index + 1);
                    if (endIndex < 0)
                    {
                        builder.Append(current);
                        continue;
                    }

                    var placeholderContent = logTemplate.Substring(index + 1, endIndex - index - 1);
                    if (TryFormatPlaceholder(placeholderContent, arguments, out var formattedPlaceholder))
                    {
                        builder.Append(formattedPlaceholder);
                    }
                    else
                    {
                        builder.Append('{').Append(placeholderContent).Append('}');
                    }

                    index = endIndex;
                    continue;
                }

                if (current == '}' && index + 1 < logTemplate.Length && logTemplate[index + 1] == '}')
                {
                    builder.Append('}');
                    index++;
                    continue;
                }

                builder.Append(current);
            }

            return builder.ToString();
        }

        static bool TryFormatPlaceholder(string placeholderContent, object[] arguments, out string formattedPlaceholder)
        {
            formattedPlaceholder = null;
            if (string.IsNullOrWhiteSpace(placeholderContent))
                return false;

            var placeholderMatch = PlaceholderRegex.Match(placeholderContent);
            if (!placeholderMatch.Success || !int.TryParse(placeholderMatch.Groups[1].Value, out var argumentIndex))
                return false;

            if (argumentIndex < 0 || argumentIndex >= arguments.Length)
                return false;

            var alignment = 0;
            if (placeholderMatch.Groups[2].Success)
                int.TryParse(placeholderMatch.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out alignment);

            var format = placeholderMatch.Groups[3].Success ? placeholderMatch.Groups[3].Value : null;
            formattedPlaceholder = FormatArgumentToken(arguments[argumentIndex], format, alignment);
            return true;
        }

        static string FormatArgumentToken(object argument, string format, int alignment)
        {
            if (argument is Object unityObject)
            {
                if (unityObject == null)
                    return "[null]";

                return $"[{unityObject.name}|InstanceID:{unityObject.GetInstanceID()}]";
            }

            var formattedValue = FormatScalarArgument(argument, format);
            if (alignment != 0)
            {
                formattedValue = alignment < 0
                    ? formattedValue.PadRight(-alignment)
                    : formattedValue.PadLeft(alignment);
            }

            return $"[{formattedValue}]";
        }

        static string FormatScalarArgument(object argument, string format)
        {
            if (argument == null)
                return "null";

            try
            {
                if (argument is IFormattable formattable)
                    return formattable.ToString(format, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
            }

            return argument.ToString() ?? string.Empty;
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
                formattedLogs.Add($"[{content.LogType}] {content.Log}");
            }
            return formattedLogs;
        }
    }
}
