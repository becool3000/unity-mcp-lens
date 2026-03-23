using Unity.AI.Assistant.Editor.Acp;
using Unity.AI.Assistant.FunctionCalling;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    /// <summary>
    /// UI element for displaying ACP (Agent Client Protocol) tool calls in the assistant chat.
    /// Handles tool calls from external agents like Claude Code.
    /// </summary>
    class AcpToolCallElement : FunctionCallBaseElement
    {
        TextField m_ContentField;
        VisualElement m_WidgetContainer;
        ScrollView m_ParentScrollView;
        bool m_HasWidget;

        string ToolCallId { get; set; }
        string ToolName { get; set; }

        protected override void InitializeContent()
        {
            m_ContentField = new TextField { isReadOnly = true };
            m_ContentField.AddToClassList("mui-function-call-text-field");
            ContentRoot.Add(m_ContentField);

            // Find the parent ScrollView so we can manage it when showing widgets
            // Widget components like GenerationSelector have their own scrolling
            m_ParentScrollView = ContentRoot.parent as ScrollView;

            // Container for widget content - added as sibling to ScrollView (outside it)
            // to avoid nested scrolling issues
            m_WidgetContainer = new VisualElement();
            m_WidgetContainer.AddToClassList("mui-function-call-widget-container");
            m_WidgetContainer.style.display = DisplayStyle.None;

            if (m_ParentScrollView?.parent != null)
            {
                var scrollViewParent = m_ParentScrollView.parent;
                scrollViewParent.Add(m_WidgetContainer);
            }
            else
            {
                ContentRoot.Add(m_WidgetContainer);
            }
        }

        /// <summary>
        /// Called when a new tool call is received or updated.
        /// </summary>
        public void OnToolCall(AcpToolCallInfo info)
        {
            if (info == null)
                return;

            ToolCallId = info.ToolCallId;
            ToolName = info.ToolName;

            // Set state based on actual status, not always InProgress
            var state = info.Status switch
            {
                AcpToolCallStatus.Completed => ToolCallState.Success,
                AcpToolCallStatus.Failed => ToolCallState.Failed,
                _ => ToolCallState.InProgress
            };
            SetState(state);

            // Enable foldout if completed or failed
            if (state != ToolCallState.InProgress)
                EnableFoldout();

            SetTitle(GetDisplayTitle(info.Title, info.ToolName));
            SetDetails(info.Description ?? string.Empty);

            // Only clear content if starting fresh (pending status)
            if (info.Status == AcpToolCallStatus.Pending)
                m_ContentField.value = string.Empty;
        }

        /// <summary>
        /// Called when a tool call update is received (status change, result, etc.).
        /// </summary>
        public void OnToolCallUpdate(AcpToolCallUpdate update)
        {
            // Update state based on status
            switch (update.Status)
            {
                case AcpToolCallStatus.Completed:
                    SetState(ToolCallState.Success);
                    EnableFoldout();
                    break;
                case AcpToolCallStatus.Failed:
                    SetState(ToolCallState.Failed);
                    EnableFoldout();
                    break;
                case AcpToolCallStatus.Pending:
                    // Still in progress, no state change needed
                    break;
            }

            // Try to render a widget if UI metadata is present
            if (TryRenderWidget(update))
            {
                // Widget rendered successfully, hide the ScrollView and show widget container
                if (m_ParentScrollView != null)
                    m_ParentScrollView.style.display = DisplayStyle.None;
                m_WidgetContainer.style.display = DisplayStyle.Flex;
            }
            else if (!string.IsNullOrEmpty(update.Content))
            {
                // No widget, show text content as usual
                m_ContentField.value = update.Content;
            }
        }

        /// <summary>
        /// Attempts to render a widget based on UI metadata in the update.
        /// </summary>
        bool TryRenderWidget(AcpToolCallUpdate update)
        {
            // Only render widget once and only for completed status
            if (m_HasWidget || update.Status != AcpToolCallStatus.Completed)
                return m_HasWidget;

            var widget = AcpWidgetRendererFactory.TryRenderWidget(update.Ui);
            if (widget == null)
                return false;

            m_WidgetContainer.Clear();
            m_WidgetContainer.Add(widget);
            m_HasWidget = true;
            return true;
        }

        /// <summary>
        /// Called when the conversation is cancelled while a tool call is in progress.
        /// </summary>
        public void OnConversationCancelled()
        {
            if (CurrentState == ToolCallState.InProgress)
            {
                SetState(ToolCallState.Failed);
                m_ContentField.value = "Conversation cancelled.";
                EnableFoldout();
            }
        }

        /// <summary>
        /// Hides the details when content will be rendered elsewhere (e.g., in the permission element).
        /// </summary>
        public void HideDetails()
        {
            SetDetails(string.Empty);
        }

        /// <summary>
        /// Gets the display title for a tool call, preferring title unless it looks like JSON.
        /// Gemini sends JSON-stringified params as title (e.g., "{}"), so we fall back to toolName.
        /// </summary>
        static string GetDisplayTitle(string title, string toolName)
        {
            // Use title if it's not empty and doesn't look like JSON
            if (!string.IsNullOrEmpty(title) && !title.StartsWith("{") && !title.StartsWith("["))
                return title;

            return toolName ?? "Tool Call";
        }
    }
}
