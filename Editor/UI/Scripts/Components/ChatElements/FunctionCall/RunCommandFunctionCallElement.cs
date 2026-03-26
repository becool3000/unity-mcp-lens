using System;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Tools.Editor;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
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
            ExecutionLogFormatter.PopulateLogContainer(m_LogsContainer, logs);
        }
    }
}
