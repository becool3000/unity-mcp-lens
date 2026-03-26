using System;
using System.Linq;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Utils.Event;
using Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements;
using Unity.AI.Assistant.UI.Editor.Scripts.ConversationSearch;
using Unity.AI.Assistant.UI.Editor.Scripts.Data;
using Unity.AI.Assistant.UI.Editor.Scripts.Events;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using Unity.AI.Assistant.Utils;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    class AssistantConversationPanel : ManagedTemplate
    {
        VisualElement m_ViewRoot;
        VisualElement m_ConversationRoot;
        ChatScrollView<MessageModel, ChatElementWrapper> m_ConversationList;

        VisualElement m_OverlayElements;

        Button m_ScrollToBottomButton;

        ResponseFeedbackQueue m_FeedbackQueue;

        BaseEventSubscriptionTicket m_RevertedTimeStampFilterRequestedSubscription;
        BaseEventSubscriptionTicket m_ExpandedViewRequestedSubscription;

        AssistantExpandedPanel m_ExpandedPanel;


        public AssistantConversationPanel() : base(AssistantUIConstants.UIModulePath)
        {
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_ViewRoot = view;
            m_OverlayElements = view.Q<VisualElement>("conversationOverlayElements");

            m_ConversationRoot = view.Q<VisualElement>("conversationRoot");
            m_ConversationList = new ChatScrollView<MessageModel, ChatElementWrapper>
            {
                EnableDelayedElements = false,
                EnableScrollLock = true
            };

            m_ConversationList.UserScrolled += UpdateOverlayButtons;
            m_ConversationList.GeometryChanged += UpdateOverlayButtons;

            m_ConversationList.Initialize(Context);
            m_ConversationRoot.Add(m_ConversationList);

            Context.SearchHelper = new AssistantViewSearchHelper(m_ConversationList, Context);

            m_ScrollToBottomButton = view.SetupButton("conversationScrollToBottomButton", _ => ScrollToBottom());

            Context.ConversationScrollToEndRequested += () => ScrollToBottom(true);

            Context.API.ConversationChanged += OnConversationChanged;

            m_FeedbackQueue = new ResponseFeedbackQueue(Context);
            m_FeedbackQueue.LoadedFeedback += OnFeedbackLoaded;

            m_ExpandedPanel = new AssistantExpandedPanel();
            m_ExpandedPanel.Initialize(Context);
            m_ExpandedPanel.SetDisplay(false);
            m_ViewRoot.Add(m_ExpandedPanel);

            UpdateVisibility();

            RegisterAttachEvents(OnAttach, OnDetach);
        }

        public void Populate(ConversationModel conversation)
        {
            m_ConversationList.BeginUpdate();
            PopulateNormalMode(conversation);
            m_ConversationList.EndUpdate();
            UpdateVisibility();
        }

        public void ClearConversation()
        {
            m_ConversationList.ClearData();
            m_FeedbackQueue.Clear();

            UpdateVisibility();
            UpdateOverlayButtons();
        }

        void OnAttach(AttachToPanelEvent evt)
        {
            m_RevertedTimeStampFilterRequestedSubscription =
                AssistantEvents.Subscribe<EventRevertedTimeStampFilterRequested>(OnRevertedTimeStampFilterRequested);
            m_ExpandedViewRequestedSubscription =
                AssistantEvents.Subscribe<EventExpandedViewRequested>(OnExpandedViewRequested);
        }

        void OnDetach(DetachFromPanelEvent evt)
        {
            AssistantEvents.Unsubscribe(ref m_RevertedTimeStampFilterRequestedSubscription);
            AssistantEvents.Unsubscribe(ref m_ExpandedViewRequestedSubscription);
        }

        void OnExpandedViewRequested(EventExpandedViewRequested eventData)
        {
            ShowExpandedPanel(eventData.Title, eventData.Element);
        }

        void OnRevertedTimeStampFilterRequested(EventRevertedTimeStampFilterRequested eventData)
        {
            var conversation = Context.Blackboard.ActiveConversation;
            if (conversation == null)
            {
                InternalLog.LogError("Requested reverted timestamp filter for a conversation that does not exist");
                return;
            }

            var list = new ChatScrollView<MessageModel, ChatElementWrapper>
            {
                EnableDelayedElements = false,
                EnableScrollLock = false,
                style =
                {
                    flexGrow = 1
                }
            };
            list.Initialize(Context);

            list.BeginUpdate();
            foreach (var msg in conversation.Messages)
            {
                if (msg.RevertedTimeStamp == eventData.Timestamp)
                    list.AddData(msg);
            }

            list.EndUpdate();
            list.SetContentEnabled(false);

            ShowExpandedPanel("Checkpoint", list);
        }

        internal void CloseExpandedPanel()
        {
            if (!m_ExpandedPanel.IsVisible)
                return;

            m_ExpandedPanel.HidePanel();
            AssistantEvents.Send(new EventExpandedPanelClosed());
        }

        void ShowExpandedPanel(string title, VisualElement element)
        {
            if (m_ExpandedPanel.IsVisible)
            {
                InternalLog.LogError(
                    $"Trying to open an expanded panel on top of an expanded panel should not happen! Panel: {title}");
                return;
            }

            m_ExpandedPanel.ShowPanel(element);
            AssistantEvents.Send(new EventExpandedPanelOpened(title));
        }

        void UpdateVisibility()
        {
            m_ConversationList.SetDisplay(m_ConversationList.HasContent);
            m_OverlayElements.SetDisplay(m_ConversationList.HasContent);
        }

        void OnConversationChanged(AssistantConversationId conversationId)
        {
            var conversation = Context.Blackboard.GetConversation(conversationId);
            if (conversation == null)
            {
                return;
            }

            // If the conversationId does not match the current list, clear everything
            if (m_ConversationList.Data.Count > 0 && m_ConversationList.Data[0].Id.ConversationId != conversationId)
            {
                m_ConversationList.ClearData();
            }

            bool scrollToEndRequired = false;
            int searchStartIndex = 0;
            for (var messageIndex = 0; messageIndex < conversation.Messages.Count; messageIndex++)
            {
                var incoming = conversation.Messages[messageIndex];

                // Skip reverted messages in normal mode
                if (incoming.RevertedTimeStamp != 0)
                {
                    continue;
                }

                // Map incoming messages to existing ones
                int incomingMessageIndex = -1;
                for (int i = searchStartIndex; i < m_ConversationList.Data.Count; i++)
                {
                    var existing = m_ConversationList.Data[i];

                    // Local added data/elements are never a match for incoming messages 
                    if (existing.IsInitialCheckpoint || existing.IsRevertedTimeStampLink)
                        continue;

                    if (existing.Id.FragmentId == incoming.Id.FragmentId)
                    {
                        Debug.Assert(existing.Role == incoming.Role);
                        incomingMessageIndex = i;
                        break;
                    }

                    // Handle special cases for internal and incomplete messages
                    if (IsTemporaryMessage(existing, incoming))
                    {
                        incomingMessageIndex = i;
                        break;
                    }
                }

                if (incomingMessageIndex == -1)
                {
                    AddChatMessage(incoming);

                    scrollToEndRequired = true;
                }
                else
                {
                    var localMessage = m_ConversationList.Data[incomingMessageIndex];

                    // Special case where we may need to update an initial checkpoint message ID
                    if (AssistantProjectPreferences.CheckpointEnabled)
                    {
                        UpdateInitialCheckpointIfNeeded(conversation, messageIndex, incoming, localMessage);
                    }

                    var messageHasContentUpdate = !incoming.HasEqualContent(localMessage);
                    m_ConversationList.UpdateData(incomingMessageIndex, incoming);
                    if (messageHasContentUpdate)
                    {
                        InternalLog.LogToFile(
                            conversationId.ToString(),
                            ("event", "Updating message in ui because of content change"),
                            ("index", incomingMessageIndex.ToString()),
                            ("total_messages_in_ui_currently", conversation.Messages.Count.ToString())
                        );
                        m_ConversationList.ScrollToEndIfNotLocked();
                    }

                    searchStartIndex = incomingMessageIndex + 1;
                }
            }

            if (scrollToEndRequired)
            {
                m_ConversationList.ScrollToEndIfNotLocked();
                UpdateVisibility();
                UpdateOverlayButtons();
            }
        }

        void AddChatMessage(MessageModel message)
        {
            InternalLog.Log($"MSG_ADD: {message.Id}");

            m_ConversationList.AddData(message);
        }

        void ScrollToBottom(bool scrollIfNotLocked = false)
        {
            if (scrollIfNotLocked)
                m_ConversationList.ScrollToEndIfNotLocked();
            else
                m_ConversationList.ScrollToEnd();

            UpdateOverlayButtons();
        }

        void UpdateOverlayButtons()
        {
            m_ScrollToBottomButton.SetDisplay(m_ConversationList.CanScrollDown);
        }

        void OnFeedbackLoaded(AssistantMessageId id, FeedbackData? feedback)
        {
            var messageIndex = FindMessageIndex(id);
            var message = m_ConversationList.Data[messageIndex];
            message.Feedback = feedback;
            m_ConversationList.UpdateData(messageIndex, message);
            ScrollToBottom(true);
        }

        int FindMessageIndex(AssistantMessageId incomingMessageId)
        {
            var message = m_ConversationList.Data.FirstOrDefault(m =>
                m.Id == incomingMessageId && IsNormalMessage(m));

            if (!message.Id.ConversationId.IsValid)
            {
                return -1;
            }

            return m_ConversationList.Data.IndexOf(message);

            bool IsNormalMessage(MessageModel message)
            {
                return message is { IsInitialCheckpoint: false, IsRevertedTimeStampLink: false };
            }
        }

        void PopulateNormalMode(ConversationModel conversation)
        {
            // Add initial checkpoint if there are any non-reverted messages
            AddInitialCheckpointIfNeeded(conversation);

            // Normal mode: Show non-reverted messages; inject grouped links per matching reverted timestamp
            long lastSeenRevertedTimeStamp = 0;

            for (var i = 0; i < conversation.Messages.Count; i++)
            {
                var msg = conversation.Messages[i];

                if (msg.RevertedTimeStamp != 0)
                {
                    if (msg.RevertedTimeStamp != lastSeenRevertedTimeStamp)
                    {
                        lastSeenRevertedTimeStamp = msg.RevertedTimeStamp;

                        // Create a synthetic message for the link
                        var linkMessage = new MessageModel
                        {
                            RevertedTimeStamp = msg.RevertedTimeStamp,
                            IsRevertedTimeStampLink = true
                        };
                        m_ConversationList.AddData(linkMessage);
                    }

                    continue;
                }

                m_ConversationList.AddData(msg);
            }

            // Queue feedback refresh for non-reverted assistant messages
            QueueFeedbackRefresh(conversation);
        }

        void AddInitialCheckpointIfNeeded(ConversationModel conversation)
        {
            if (!AssistantProjectPreferences.CheckpointEnabled)
                return;

            // Add a valid checkpoint for the first non-reverted user message
            for (var i = 0; i < conversation.Messages.Count; i++)
            {
                var msg = conversation.Messages[i];
                if (msg.RevertedTimeStamp == 0 && msg.Role == MessageModelRole.User)
                {
                    // Create a synthetic message for the initial checkpoint
                    var checkpointMessage = new MessageModel
                    {
                        Id = msg.Id,
                        IsInitialCheckpoint = true
                    };
                    m_ConversationList.AddData(checkpointMessage);
                    return;
                }
            }

            // Alternatively, add a temporary initial checkpoint; Id gets updated with the next response
            var temporaryCheckpointMessage = new MessageModel
            {
                Id = new AssistantMessageId(conversation.Id, String.Empty, AssistantMessageIdType.Incomplete),
                IsInitialCheckpoint = true
            };
            m_ConversationList.AddData(temporaryCheckpointMessage);
        }

        void UpdateInitialCheckpointIfNeeded(ConversationModel conversation, 
            int messageIndex, MessageModel incoming, MessageModel localMessage)
        {
            // Check if message ID is transitioning from temporary to external
            var isLocalMessageTemporary = IsTemporaryMessage(localMessage, incoming);
            var isIncomingMessageExternal = incoming.Id.Type == AssistantMessageIdType.External;

            if (!isLocalMessageTemporary || !isIncomingMessageExternal)
            {
                return;
            }

            AssistantMessageId checkpointId = default;
            bool shouldUpdate = false;

            // Update 1: User message ID is final; update initial checkpoint if it was the first non-reverted user message
            if (incoming.Role == MessageModelRole.User)
            {
                if (IsFirstNonRevertedUserMessage(conversation, messageIndex))
                {
                    checkpointId = incoming.Id;
                    shouldUpdate = true;
                }
            }
            // Update 2: Assistant message ID is final; update initial checkpoint if this follows message from update above
            else if (incoming.Role == MessageModelRole.Assistant)
            {
                if (messageIndex > 0 && IsFirstNonRevertedUserMessage(conversation, messageIndex - 1))
                {
                    // Note: We could improve this: We update data providing the User message ID.
                    // The checkpoint logic needs this detected Assistant message's ID anyway, and stores it.
                    checkpointId = conversation.Messages[messageIndex - 1].Id;
                    shouldUpdate = true;
                }
            }

            if (shouldUpdate)
            {
                var initialCheckpoint = m_ConversationList.Data.FirstOrDefault(msg => msg.IsInitialCheckpoint);
                if (initialCheckpoint.IsInitialCheckpoint)
                {
                    var idx = m_ConversationList.Data.IndexOf(initialCheckpoint);
                    initialCheckpoint.Id = checkpointId;
                    m_ConversationList.UpdateData(idx, initialCheckpoint);
                }
            }
        }

        void QueueFeedbackRefresh(ConversationModel conversation)
        {
            for (var i = 0; i < conversation.Messages.Count; i++)
            {
                var msg = conversation.Messages[i];
                if (msg.RevertedTimeStamp != 0)
                    continue;

                if (msg.Role == MessageModelRole.Assistant)
                {
                    m_FeedbackQueue.QueueRefresh(msg.Id);
                }
            }
        }

        bool IsTemporaryMessage(MessageModel existing, MessageModel incoming)
        {
            if (existing.Role != incoming.Role)
                return false;

            // Match internal user messages
            if (existing is { Role: MessageModelRole.User, Id: { Type: AssistantMessageIdType.Internal } })
            {
                return true;
            }

            // Match incomplete assistant messages
            if (existing is { Role: MessageModelRole.Assistant, Id: { Type: AssistantMessageIdType.Incomplete } })
            {
                return true;
            }

            return false;
        }

        bool IsFirstNonRevertedUserMessage(ConversationModel conversation, int currentIndex)
        {
            for (int i = 0; i < currentIndex; i++)
            {
                var msg = conversation.Messages[i];
                if (msg.RevertedTimeStamp == 0 && msg.Role == MessageModelRole.User)
                {
                    return false;
                }
            }

            return true;
        }
    }
}