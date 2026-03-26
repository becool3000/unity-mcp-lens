using System;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    /// <summary>
    /// Abstract base class for tool/function call UI elements.
    /// Provides common visual infrastructure for displaying tool calls with title, status indicator,
    /// and expandable content area. Subclasses handle specific data models (AssistantFunctionCall, AcpToolCallInfo, etc.).
    /// </summary>
    abstract class FunctionCallBaseElement : ManagedTemplate
    {
        protected const string k_TextFieldClassName = "mui-function-call-text-field";
        protected const string k_ExpandedContentClassName = "mui-function-call-expanded";
        protected const string k_StatusSuccessClassName = "mui-function-call-status-success";
        protected const string k_StatusInProgressClassName = "mui-function-call-status-in-progress";
        protected const string k_StatusFailedClassName = "mui-function-call-status-failed";
        protected const string k_IconCheckmarkClassName = "mui-icon-checkmark";
        protected const string k_IconCloseClassName = "mui-icon-close";

        VisualElement m_Root;
        VisualElement m_Header;
        Label m_Title;
        Label m_Parameters;
        VisualElement m_StatusElement;
        LoadingSpinner m_LoadingSpinner;
        ScrollView m_ScrollView;
        bool m_FoldoutEnabled;

        protected enum ToolCallState
        {
            InProgress,
            Success,
            Failed
        }

        protected VisualElement ContentRoot { get; private set; }
        protected ToolCallState CurrentState { get; private set; }

        protected FunctionCallBaseElement() : base(typeof(FunctionCallBaseElement), AssistantUIConstants.UIModulePath) { }

        protected FunctionCallBaseElement(string basePath) : base(typeof(FunctionCallBaseElement), basePath) { }

        protected override void InitializeView(TemplateContainer view)
        {
            m_Root = view.Q("functionCallEntryRoot");
            m_Header = view.Q("functionCallHeader");

            m_Title = view.Q<Label>("functionCallTitle");
            m_Parameters = view.Q<Label>("functionCallParameters");
            ContentRoot = view.Q<VisualElement>("functionCallContent");
            m_StatusElement = view.Q("functionCallStatus");
            m_ScrollView = view.Q<ScrollView>("functionCallScrollView");

            m_Title.AddToClassList(k_TextFieldClassName);

            // Create and initialize the loading spinner
            m_LoadingSpinner = new LoadingSpinner();
            m_StatusElement.Add(m_LoadingSpinner);
            m_LoadingSpinner.AddToClassList(k_StatusInProgressClassName);
            m_LoadingSpinner.Show();

            ContentRoot.SetDisplay(false);

            InitializeContent();
        }

        /// <summary>
        /// Called during initialization to allow subclasses to set up their content in ContentRoot.
        /// </summary>
        protected abstract void InitializeContent();

        protected void SetState(ToolCallState state)
        {
            if (state == CurrentState)
                return;

            switch (state)
            {
                case ToolCallState.InProgress:
                    m_StatusElement.RemoveFromClassList(k_StatusSuccessClassName);
                    m_StatusElement.RemoveFromClassList(k_StatusFailedClassName);
                    m_StatusElement.RemoveFromClassList(k_IconCheckmarkClassName);
                    m_StatusElement.RemoveFromClassList(k_IconCloseClassName);
                    m_LoadingSpinner?.Show();
                    break;
                case ToolCallState.Success:
                    m_LoadingSpinner?.Hide();
                    m_StatusElement.RemoveFromClassList(k_StatusFailedClassName);
                    m_StatusElement.RemoveFromClassList(k_IconCloseClassName);
                    m_StatusElement.AddToClassList(k_StatusSuccessClassName);
                    m_StatusElement.AddToClassList(k_IconCheckmarkClassName);

                    // This is temporary until this is fixed in UITK https://jira.unity3d.com/browse/UUM-108227
                    m_StatusElement.AddToClassList("mui-icon-tint-success");
                    break;
                case ToolCallState.Failed:
                    m_LoadingSpinner?.Hide();
                    m_StatusElement.RemoveFromClassList(k_StatusSuccessClassName);
                    m_StatusElement.RemoveFromClassList(k_IconCheckmarkClassName);
                    m_StatusElement.AddToClassList(k_StatusFailedClassName);
                    m_StatusElement.AddToClassList(k_IconCloseClassName);

                    // This is temporary until this is fixed in UITK https://jira.unity3d.com/browse/UUM-108227
                    m_StatusElement.AddToClassList("mui-icon-tint-error");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }

            CurrentState = state;
        }

        protected void SetTitle(string title) => m_Title.text = title;

        protected void SetContentMaxHeight(float height) => m_ScrollView.style.maxHeight = height;

        protected void SetDetails(string details)
        {
            m_Parameters.text = details;
            m_Header.tooltip = details;
        }

        protected void EnableFoldout()
        {
            if (m_FoldoutEnabled)
                return;

            m_FoldoutEnabled = true;
            m_Header.RegisterCallback<MouseDownEvent>(ToggleFoldout);
        }

        void ToggleFoldout(MouseDownEvent evt)
        {
            var newValue = !IsContentVisible();
            SetFoldoutExpanded(newValue);
        }

        protected void SetFoldoutExpanded(bool expanded)
        {
            ContentRoot.SetDisplay(expanded);

            if (expanded)
                m_Root.AddToClassList(k_ExpandedContentClassName);
            else
                m_Root.RemoveFromClassList(k_ExpandedContentClassName);
        }

        protected bool IsContentVisible() => ContentRoot.style.display != DisplayStyle.None;
    }
}
