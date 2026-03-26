using System;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    /// <summary>
    /// Utility for parsing and rendering execution logs from RunCommand tool results.
    /// Shared between the AI Assistant's <see cref="RunCommandFunctionCallElement"/> and
    /// the ACP <see cref="AcpRunCommandRenderer"/>.
    /// </summary>
    static class ExecutionLogFormatter
    {
        // Regex patterns for log parsing
        static readonly Regex k_LogPrefixPattern = new(@"^\[(Log|Warning|Error)\]\s*", RegexOptions.Compiled);

        // [displayText|InstanceID:instanceId] for Unity objects (link), [displayText] for plain values
        static readonly Regex k_BracketedSegmentPattern = new(@"\[([^\]]+)\]", RegexOptions.Compiled);
        static readonly Regex k_ObjectReferencePattern = new(@"^(.+)\|InstanceID:(-?\d+)$", RegexOptions.Compiled);

        static readonly Color k_WarningColor = new(1f, 0.92f, 0.016f); // Yellow
        static readonly Color k_ErrorColor = new(1f, 0.32f, 0.29f);    // Red

        /// <summary>
        /// Parses a log line, extracting the log type from the prefix and stripping it from the display text.
        /// </summary>
        public static (string displayText, LogType logType) ParseLogLine(string line)
        {
            var logType = LogType.Log;
            var displayText = line;

            var prefixMatch = k_LogPrefixPattern.Match(line);
            if (prefixMatch.Success)
            {
                var typeStr = prefixMatch.Groups[1].Value;
                logType = typeStr switch
                {
                    "Warning" => LogType.Warning,
                    "Error" => LogType.Error,
                    _ => LogType.Log
                };

                displayText = line.Substring(prefixMatch.Length);
            }

            return (displayText.Trim(), logType);
        }

        /// <summary>
        /// Creates a log row <see cref="VisualElement"/> with clickable object/asset links.
        /// </summary>
        public static VisualElement CreateLogRow(string displayText, LogType logType)
        {
            var row = new VisualElement();
            row.AddToClassList("mui-execution-log-row");

            var matches = k_BracketedSegmentPattern.Matches(displayText);
            if (matches.Count == 0)
            {
                var label = new Label(displayText.Trim());
                label.AddToClassList("mui-execution-log-label");
                ApplyLogTypeColor(label, logType);
                row.Add(label);
            }
            else
            {
                var lastEnd = 0;
                foreach (Match match in matches)
                {
                    var prefix = displayText.Substring(lastEnd, match.Index - lastEnd);
                    if (!string.IsNullOrEmpty(prefix))
                    {
                        var prefixLabel = new Label(prefix);
                        prefixLabel.AddToClassList("mui-execution-log-label");
                        ApplyLogTypeColor(prefixLabel, logType);
                        row.Add(prefixLabel);
                    }

                    var content = match.Groups[1].Value;
                    var idMatch = k_ObjectReferencePattern.Match(content);
                    if (idMatch.Success && int.TryParse(idMatch.Groups[2].Value, out var instanceId))
                    {
                        var linkText = idMatch.Groups[1].Value;
                        var linkButton = new Button(() => PingObjectByInstanceId(instanceId)) { text = linkText };
                        linkButton.AddToClassList("mui-execution-log-object-link");
                        row.Add(linkButton);
                    }
                    else if (TryGetRelativeAssetPath(content, out var assetPath))
                    {
                        var linkButton = new Button(() => PingAssetAtPath(assetPath)) { text = content };
                        linkButton.AddToClassList("mui-execution-log-object-link");
                        row.Add(linkButton);
                    }
                    else
                    {
                        var textLabel = new Label(content);
                        textLabel.AddToClassList("mui-execution-log-label");
                        textLabel.AddToClassList("mui-execution-log-inline-value");
                        ApplyLogTypeColor(textLabel, logType);
                        row.Add(textLabel);
                    }

                    lastEnd = match.Index + match.Length;
                }

                var suffix = displayText.Substring(lastEnd);
                if (!string.IsNullOrEmpty(suffix))
                {
                    var suffixLabel = new Label(suffix);
                    suffixLabel.AddToClassList("mui-execution-log-label");
                    ApplyLogTypeColor(suffixLabel, logType);
                    row.Add(suffixLabel);
                }
            }

            return row;
        }

        /// <summary>
        /// Populates a container with formatted log rows parsed from a multi-line log string.
        /// </summary>
        public static void PopulateLogContainer(VisualElement container, string logs)
        {
            container.Clear();

            var lines = logs.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var (displayText, logType) = ParseLogLine(line);
                container.Add(CreateLogRow(displayText, logType));
            }
        }

        /// <summary>
        /// Applies color styling based on log type.
        /// </summary>
        public static void ApplyLogTypeColor(VisualElement element, LogType logType)
        {
            switch (logType)
            {
                case LogType.Warning:
                    element.style.color = k_WarningColor;
                    break;
                case LogType.Error:
                    element.style.color = k_ErrorColor;
                    break;
            }
        }

        /// <summary>
        /// Returns true if the text is or contains a valid asset path, with the project-relative path in the out param.
        /// Accepts both relative paths (Assets/..., Packages/...) and absolute paths containing those segments.
        /// </summary>
        public static bool TryGetRelativeAssetPath(string text, out string path)
        {
            path = null;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            var t = text.Trim();
            string relative;
            if (t.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || t.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                relative = t;
            else
            {
                var lastAssets = t.LastIndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
                var lastPackages = t.LastIndexOf("Packages/", StringComparison.OrdinalIgnoreCase);
                var idx = lastAssets >= 0 ? lastAssets : -1;
                if (lastPackages >= 0 && lastPackages > idx)
                    idx = lastPackages;
                if (idx < 0)
                    return false;
                relative = t.Substring(idx);
            }
            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(relative)))
                return false;
            path = relative;
            return true;
        }

        /// <summary>
        /// Pings an asset at the given project-relative path in the Unity Editor.
        /// </summary>
        public static void PingAssetAtPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            var obj = AssetDatabase.LoadMainAssetAtPath(path);
            if (obj != null)
                EditorGUIUtility.PingObject(obj);
        }

        /// <summary>
        /// Pings a Unity object by its instance ID in the Unity Editor.
        /// </summary>
        public static void PingObjectByInstanceId(int instanceId)
        {
#if UNITY_6000_3_OR_NEWER
            var obj = EditorUtility.EntityIdToObject(instanceId);
#else
            var obj = EditorUtility.InstanceIDToObject(instanceId);
#endif
            if (obj != null)
                EditorGUIUtility.PingObject(obj);
        }
    }
}
