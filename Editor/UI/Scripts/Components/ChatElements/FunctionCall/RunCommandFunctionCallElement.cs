using System;
using System.Text.RegularExpressions;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor.Backend.Socket.Tools;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Tools.Editor;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    [FunctionCallRenderer(typeof(RunCommandTool), nameof(RunCommandTool.ExecuteCommand), Emphasized = true)]
    class RunCommandFunctionCallElement : ManagedTemplate, IFunctionCallRenderer
    {
        const string k_TabActiveClass = "tab-active";

        string m_FunctionDisplayName = "Run Command";
        
        public virtual string Title => m_FunctionDisplayName;
        public virtual string TitleDetails { get; private set; }
        public virtual bool Expanded => true;

        // Regex patterns for log parsing
        static readonly Regex k_LogPrefixPattern = new Regex(@"^\[(Log|Warning|Error)\]\s*", RegexOptions.Compiled);
        
        // [displayText|InstanceID:instanceId] for Unity objects (link), [displayText] for plain values
        static readonly Regex k_BracketedSegmentPattern = new Regex(@"\[([^\]]+)\]", RegexOptions.Compiled);
        static readonly Regex k_ObjectReferencePattern = new Regex(@"^(.+)\|InstanceID:(-?\d+)$", RegexOptions.Compiled);
        
        static readonly Color k_WarningColor = new Color(1f, 0.92f, 0.016f); // Yellow
        static readonly Color k_ErrorColor = new Color(1f, 0.32f, 0.29f);    // Red

        Button m_CodeTab;
        Button m_OutputTab;
        VisualElement m_CodePane;
        VisualElement m_OutputPane;
        CodeBlockElement m_CodeBlock;
        VisualElement m_LogsContainer;

        public RunCommandFunctionCallElement() : base(AssistantUIConstants.UIModulePath) { }

        protected override void InitializeView(TemplateContainer view)
        {
            view.AddToClassList("run-command-tabs");

            m_CodeTab = view.SetupButton("runCommandCodeTab", OnCodeTabClicked);
            m_OutputTab = view.SetupButton("runCommandOutputTab", OnOutputTabClicked);

            m_CodePane = view.Q<VisualElement>("runCommandCodePane");
            m_OutputPane = view.Q<VisualElement>("runCommandOutputPane");
            m_LogsContainer = view.Q<VisualElement>("runCommandLogsContainer");

            m_CodeBlock = new CodeBlockElement();
            m_CodeBlock.Initialize(Context);
            m_CodeBlock.SetCodeType("csharp");
            m_CodeBlock.SetActions(copy: true, save: false, select: true, edit: false);
            m_CodeBlock.SetEmbeddedMode();
            m_CodePane.Add(m_CodeBlock);

            var headerActions = view.Q("runCommandHeaderActions");
            m_CodeBlock.CloneActionButtons(headerActions);

            var scrollView = GetFirstAncestorOfType<ScrollView>();
            if (scrollView != null)
            {
                var header = scrollView.parent?.Q("functionCallHeader");
                if (header != null)
                {
                    headerActions.RemoveFromHierarchy();
                    header.Add(headerActions);
                }

                scrollView.style.maxHeight = StyleKeyword.None;
            }
        }

        void OnCodeTabClicked(PointerUpEvent evt) => ShowCodeTab();

        void OnOutputTabClicked(PointerUpEvent evt) => ShowOutputTab();

        void ShowCodeTab()
        {
            m_CodeTab.AddToClassList(k_TabActiveClass);
            m_OutputTab.RemoveFromClassList(k_TabActiveClass);
            m_CodePane.SetDisplay(true);
            m_OutputPane.SetDisplay(false);
        }

        void ShowOutputTab()
        {
            m_CodeTab.RemoveFromClassList(k_TabActiveClass);
            m_OutputTab.AddToClassList(k_TabActiveClass);
            m_CodePane.SetDisplay(false);
            m_OutputPane.SetDisplay(true);
        }

        public void OnCallRequest(AssistantFunctionCall functionCall)
        {
            var code = functionCall.Parameters?["code"]?.ToString();
            var title = functionCall.Parameters?["title"]?.ToString();

            if (!string.IsNullOrEmpty(title))
                m_FunctionDisplayName = title;

            if (!string.IsNullOrEmpty(code))
                m_CodeBlock.SetCode(code);
        }

        public void OnCallSuccess(string functionId, Guid callId, IFunctionCaller.CallResult result)
        {
            var typedResult = result.GetTypedResult<RunCommandTool.ExecutionOutput>();

            // Display execution logs if present
            if (!string.IsNullOrEmpty(typedResult.ExecutionLogs))
                DisplayFormattedLogs(typedResult.ExecutionLogs);
        }

        public void OnCallError(string functionId, Guid callId, string error)
        {
            if (!string.IsNullOrEmpty(error))
                DisplayFormattedLogs(error);
        }        
        
        void DisplayFormattedLogs(string logs)
        {
            ShowOutputTab();
            m_LogsContainer.Clear();

            var lines = logs.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var (displayText, logType) = ParseLogLine(line);
                AddLogLineWithObjectLinks(displayText, logType);
            }
        }

        void AddLogLineWithObjectLinks(string displayText, LogType logType)
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
                    if (idMatch.Success)
                    {
                        var linkText = idMatch.Groups[1].Value;
                        var instanceId = int.Parse(idMatch.Groups[2].Value);
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

            m_LogsContainer.Add(row);
        }

        static void ApplyLogTypeColor(VisualElement element, LogType logType)
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
        static bool TryGetRelativeAssetPath(string text, out string path)
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

        static void PingAssetAtPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            var obj = AssetDatabase.LoadMainAssetAtPath(path);
            if (obj != null)
                EditorGUIUtility.PingObject(obj);
        }

        static void PingObjectByInstanceId(int instanceId)
        {
#if UNITY_6000_3_OR_NEWER
            var obj = EditorUtility.EntityIdToObject(instanceId);
#else
            var obj = EditorUtility.InstanceIDToObject(instanceId);
#endif
            if (obj != null)
                EditorGUIUtility.PingObject(obj);
        }        

        (string displayText, LogType logType) ParseLogLine(string line)
        {
            var logType = LogType.Log;
            var displayText = line;
            
            // Extract log type from prefix [Log], [Warning], [Error]
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
                
                // Remove the prefix from display
                displayText = line.Substring(prefixMatch.Length);
            }

            // Keep bracketed segments in displayText so we can parse object links ([text|InstanceID:id]) and inline values ([text])
            return (displayText.Trim(), logType);
        }
    }
}
