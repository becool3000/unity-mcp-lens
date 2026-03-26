using System.IO;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Editor.Acp;
using Unity.AI.Assistant.Tools.Editor;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    /// <summary>
    /// UI element for displaying ACP (Agent Client Protocol) tool calls in the assistant chat.
    /// Handles tool calls from external agents like Claude Code.
    /// Supports an optional <see cref="IAcpToolCallRenderer"/> for specialized rendering.
    /// </summary>
    class AcpToolCallElement : FunctionCallBaseElement
    {
        const string k_CodeEditDisplayName = "Code Edit";

        readonly IAcpToolCallRenderer m_Renderer;

        bool m_HasExpanded;
        TextField m_ContentField;
        VisualElement m_WidgetContainer;
        ScrollView m_ParentScrollView;
        bool m_HasWidget;
        bool m_HasDiffContent;

        string ToolCallId { get; set; }
        string ToolName { get; set; }

        /// <summary>
        /// Whether this element is using a custom renderer.
        /// </summary>
        public bool HasRenderer => m_Renderer != null;

        public AcpToolCallElement(IAcpToolCallRenderer renderer = null)
        {
            m_Renderer = renderer;
        }

        protected override void InitializeContent()
        {
            if (m_Renderer != null)
            {
                var rendererElement = (VisualElement)m_Renderer;
                ContentRoot.Add(rendererElement);

                // Initialize the renderer if it's a ManagedTemplate
                if (rendererElement is ManagedTemplate managedTemplate)
                    managedTemplate.Initialize(Context);
                if (rendererElement is IAssistantUIContextAware contextAware)
                    contextAware.Context = Context;

                return;
            }

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

            if (m_Renderer != null)
            {
                m_Renderer.OnToolCall(info);
                SetTitle(m_Renderer.Title ?? GetDisplayTitle(info.Title, info.ToolName));
                SetDetails(m_Renderer.TitleDetails ?? string.Empty);

                if (m_Renderer.Expanded && !m_HasExpanded)
                {
                    EnableFoldout();
                    SetFoldoutExpanded(true);
                    m_HasExpanded = true;
                }

                return;
            }

            // Don't override title/details if diff content already set them to CodeEdit style
            if (!m_HasDiffContent)
            {
                SetTitle(GetDisplayTitle(info.Title, info.ToolName));
                SetDetails(info.Description ?? string.Empty);
            }

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

            if (m_Renderer != null)
            {
                m_Renderer.OnToolCallUpdate(update);

                // Update title/details in case the renderer changed them
                var title = m_Renderer.Title;
                if (!string.IsNullOrEmpty(title))
                    SetTitle(title);

                var details = m_Renderer.TitleDetails;
                if (details != null)
                    SetDetails(details);

                return;
            }

            // Try to render a widget if UI metadata is present
            if (TryRenderWidget(update))
            {
                // Widget rendered successfully, hide the ScrollView and show widget container
                if (m_ParentScrollView != null)
                    m_ParentScrollView.style.display = DisplayStyle.None;
                m_WidgetContainer.style.display = DisplayStyle.Flex;
            }
            else if (!string.IsNullOrEmpty(update.Content) && !m_HasDiffContent)
            {
                // No widget and no diff content, show text content as usual
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
            if (m_Renderer != null)
            {
                if (CurrentState == ToolCallState.InProgress)
                {
                    SetState(ToolCallState.Failed);
                    EnableFoldout();
                }

                m_Renderer.OnConversationCancelled();
                return;
            }

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
        /// Renders file diff/write content inline in the tool call's expandable content area.
        /// Delegates to <see cref="DiffPermissionContentRenderer"/> for the actual rendering.
        /// </summary>
        public void SetDiffContent(JObject rawInput, AssistantUIContext context)
        {
            if (m_HasDiffContent || rawInput == null)
                return;

            var renderer = new DiffPermissionContentRenderer();
            if (!renderer.CanRender(rawInput))
                return;

            var rendered = renderer.Render(rawInput, context);
            if (rendered == null)
                return;

            // Match AI Assistant's CodeEdit appearance
            var isEdit = rawInput["new_string"] != null;
            var filePath = rawInput["file_path"]?.ToString();
            var filename = !string.IsNullOrEmpty(filePath) ? Path.GetFileName(filePath) : null;
            SetTitle(k_CodeEditDisplayName);
            SetDetails(filename != null
                ? $"{(isEdit ? "Edit" : "Create")} {filename}"
                : string.Empty);

            ContentRoot.Add(rendered);

            // Hide the empty text field so it doesn't create a gap above the diff
            m_ContentField.style.display = DisplayStyle.None;

            EnableFoldout();
            SetFoldoutExpanded(true);

            m_HasDiffContent = true;
        }

        /// <summary>
        /// Gets the display title for a tool call, preferring title unless it looks like JSON.
        /// Gemini sends JSON-stringified params as title (e.g., "{}"), so we fall back to toolName.
        /// </summary>
        internal static string GetDisplayTitle(string title, string toolName)
        {
            // Use title if it's not empty and doesn't look like JSON
            if (!string.IsNullOrEmpty(title) && !title.StartsWith("{") && !title.StartsWith("["))
                return title;

            return toolName ?? "Tool Call";
        }
    }
}
