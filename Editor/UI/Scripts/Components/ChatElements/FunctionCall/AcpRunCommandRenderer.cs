using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Editor.Acp;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    /// <summary>
    /// Renders ACP RunCommand tool calls with the same two-tab UI (Code + Output)
    /// as the AI Assistant's built-in <see cref="RunCommandFunctionCallElement"/>.
    /// Reuses the same UXML/USS templates.
    /// </summary>
    [AcpToolCallRenderer("Unity_RunCommand")]
    class AcpRunCommandRenderer : ManagedTemplate, IAcpToolCallRenderer
    {
        const string k_TabActiveClass = "tab-active";
        const string k_DefaultTitle = "Run Command";

        string m_Title = k_DefaultTitle;
        bool m_HasOutput;

        Button m_CodeTab;
        Button m_OutputTab;
        VisualElement m_CodePane;
        VisualElement m_OutputPane;
        CodeBlockElement m_CodeBlock;
        VisualElement m_LogsContainer;

        public string Title => m_Title;
        public string TitleDetails { get; private set; }
        public bool Expanded => true;

        public AcpRunCommandRenderer() : base(AssistantUIConstants.UIModulePath)
        {
            SetResourceName("RunCommandFunctionCallElement");
        }

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

        public void OnToolCall(AcpToolCallInfo info)
        {
            if (info == null)
                return;

            // Extract code and title from rawInput
            // The first tool_call event may have rawInput: {} (empty), the second has the data
            var rawInput = info.RawInput;
            if (rawInput == null)
                return;

            var title = rawInput["Title"]?.ToString();
            if (!string.IsNullOrEmpty(title))
                m_Title = title;

            var code = rawInput["Code"]?.ToString();
            if (!string.IsNullOrEmpty(code))
                m_CodeBlock.SetCode(code);
        }

        public void OnToolCallUpdate(AcpToolCallUpdate update)
        {
            if (update == null || m_HasOutput)
                return;

            // Parse rawOutput.data for execution results
            var rawOutput = update.RawOutput;
            if (rawOutput == null || rawOutput.Type == JTokenType.Null)
                return;

            JToken data = null;
            string error = null;

            if (rawOutput.Type == JTokenType.Object)
            {
                data = rawOutput["data"];
                error = rawOutput["error"]?.ToString();
            }

            if (data != null && data.Type != JTokenType.Null)
            {
                DisplayExecutionResult(data);
                m_HasOutput = true;
            }
            else if (!string.IsNullOrEmpty(error))
            {
                DisplayError(error);
                m_HasOutput = true;
            }
            else if (!string.IsNullOrEmpty(update.Content))
            {
                // Fallback: display raw content as logs
                DisplayError(update.Content);
                m_HasOutput = true;
            }
        }

        public void OnConversationCancelled()
        {
            if (!m_HasOutput)
            {
                ShowOutputTab();
                m_LogsContainer.Clear();
                m_LogsContainer.Add(ExecutionLogFormatter.CreateLogRow("Conversation cancelled.", LogType.Error));
                m_HasOutput = true;
            }
        }

        void DisplayExecutionResult(JToken data)
        {
            var executionLogs = data["executionLogs"]?.ToString();
            var compilationLogs = data["compilationLogs"]?.ToString();
            var isExecutionSuccessful = data["isExecutionSuccessful"]?.Value<bool>() ?? true;

            if (!isExecutionSuccessful && !string.IsNullOrEmpty(compilationLogs))
            {
                // Compilation failure — show compilation logs as errors
                ShowOutputTab();
                ExecutionLogFormatter.PopulateLogContainer(m_LogsContainer, compilationLogs);
            }
            else if (!string.IsNullOrEmpty(executionLogs))
            {
                ShowOutputTab();
                ExecutionLogFormatter.PopulateLogContainer(m_LogsContainer, executionLogs);
            }
        }

        void DisplayError(string error)
        {
            ShowOutputTab();
            ExecutionLogFormatter.PopulateLogContainer(m_LogsContainer, error);
        }
    }
}
