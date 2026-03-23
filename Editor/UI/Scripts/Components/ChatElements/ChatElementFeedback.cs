using System;
using System.Collections.Generic;
using System.Threading;
using Unity.AI.Assistant.ApplicationModels;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Analytics;
using Unity.AI.Assistant.UI.Editor.Scripts.Data;
using Unity.AI.Assistant.UI.Editor.Scripts.Data.MessageBlocks;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using Unity.AI.Assistant.Utils;
using UnityEngine;
using UnityEngine.UIElements;
using TextField = UnityEngine.UIElements.TextField;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    [UxmlElement]
    partial class ChatElementFeedback : ManagedTemplate
    {
        const string k_FeedbackButtonActiveClass = "mui-feedback-button-active";

        CancellationTokenSource m_FeedbackSendButtonTokenSource;
        CancellationTokenSource m_ResponseCopyButtonActiveTokenSource;

        VisualElement m_OptionsSection;
        VisualElement m_FeedbackParamSection;

        Button m_CopyButton;
        AssistantImage m_CopyButtonImage;
        Button m_UpVoteButton;
        Button m_DownVoteButton;
        Label m_MessageCostLabel;

        Toggle m_FeedbackFlagInappropriateCheckbox;
        TextField m_FeedbackText;
        VisualElement m_FeedbackPlaceholderContent;
        Label m_FeedbackPlaceholder;
        bool m_FeedbackTextFocused;

        Button m_FeedbackSendButton;
        Label m_FeedbackSendButtonLabel;
        AssistantImage m_FeedbackSendButtonImage;

        Foldout m_FeedbackCommentFoldout;

        FeedbackEditMode m_FeedbackMode = FeedbackEditMode.None;

        ChatElementCheckpoint m_Checkpoint;

        AssistantMessageId m_MessageId;

        static AssistantConversationId s_CurrentStoredConversationFeedbacks;
        static readonly Dictionary<AssistantMessageId, FeedbackData> k_StoredFeedbackUIState = new();

        MessageModel m_Message;

        private bool m_MessageCostRequested;

        enum FeedbackEditMode
        {
            None,
            UpVote,
            DownVote
        }

        public ChatElementFeedback() : base(AssistantUIConstants.UIModulePath) { }

        protected override void InitializeView(TemplateContainer view)
        {
            m_OptionsSection = view.Q("optionsSection");
            m_OptionsSection.SetDisplay(false);
            m_CopyButton = view.SetupButton("copyButton", OnCopyClicked);
            m_CopyButtonImage = m_CopyButton.SetupImage("copyButtonImage", "copy");
            m_UpVoteButton = view.SetupButton("upVoteButton", OnUpvoteClicked);
            m_DownVoteButton = view.SetupButton("downVoteButton", OnDownvoteClicked);
            m_MessageCostLabel = view.Q<Label>("messageCostLabel");
            m_MessageCostLabel.SetDisplay(false);

            m_FeedbackParamSection = view.Q("feedbackParamSection");
            m_FeedbackPlaceholderContent = view.Q("placeholderContent");
            m_FeedbackPlaceholder = view.Q<Label>("placeholderText");

            m_Checkpoint = view.Q<ChatElementCheckpoint>();
            m_Checkpoint.Initialize(Context);

            SetupFeedbackParameters();
        }

        public void SetData(MessageModel message)
        {
            m_MessageId = message.Id;
            m_Message = message;

            SetCurrentConversation(message.Id.ConversationId);

            m_FeedbackMode = FeedbackEditMode.None;

            RefreshFeedbackParameters();

            if (message.Feedback != null)
            {
                // Feedback returned from backend
                SetFeedback(message.Id, message.Feedback);
                StoreFeedbackUIState(message.Id, message.Feedback.Value);
            }
            else if (k_StoredFeedbackUIState.TryGetValue(m_Message.Id, out var feedbackData))
            {
                // Feedback cached for current conversation
                SetFeedback(message.Id, feedbackData);
            }

            // Check if last block is a complete response to show feedback options and it's not an error
            if (message.IsComplete && message.Blocks.Count > 0 && message.Blocks[^1] is AnswerBlockModel)
            {
                m_OptionsSection.SetDisplay(true);

                // Hide vote buttons for non-Unity providers (they don't support feedback)
                bool isUnitySession = Context.IsUnityProvider;
                m_UpVoteButton.SetDisplay(isUnitySession);
                m_DownVoteButton.SetDisplay(isUnitySession);

                if (!m_MessageCostRequested)
                {
                    // Fetch message cost
                    if (m_MessageId != AssistantMessageId.Invalid)
                    {
                        Context.API.MessageCostReceived += OnMessageCostReceived;
                        Context.API.FetchMessageCost(m_MessageId);
                        m_MessageCostRequested = true;
                    }
                }
            }
            m_Checkpoint.SetCheckpointData(message.Id, 1);
        }

        void OnMessageCostReceived(AssistantMessageId id, int? cost)
        {
            if (id != m_MessageId) return;

            if (cost.HasValue && m_MessageCostLabel != null)
            {
                m_MessageCostLabel.text = $"• {cost.Value} pts";
                m_MessageCostLabel.SetDisplay(true);
            }
            Context.API.MessageCostReceived -= OnMessageCostReceived;
        }

        void SetupFeedbackParameters()
        {
            m_FeedbackFlagInappropriateCheckbox = m_FeedbackParamSection.Q<Toggle>("feedbackFlagCheckbox");

            m_FeedbackText = m_FeedbackParamSection.Q<TextField>("feedbackValueText");
            m_FeedbackText.multiline = true;
            m_FeedbackText.maxLength = AssistantMessageSizeConstraints.FeedbackLimit;
            m_FeedbackText.RegisterValueChangedCallback(_ => CheckFeedbackState());

            m_FeedbackText.RegisterCallback<FocusInEvent>(_ => SetFeedbackTextFocused(true));
            m_FeedbackText.RegisterCallback<FocusOutEvent>(_ => SetFeedbackTextFocused(false));

            m_FeedbackSendButton = m_FeedbackParamSection.SetupButton("feedbackSendButton", OnSendFeedback);
            m_FeedbackSendButtonLabel = m_FeedbackSendButton.Q<Label>();
            m_FeedbackSendButtonImage = m_FeedbackSendButton.SetupImage("feedbackSendButtonImage", "checkmark");
            m_FeedbackSendButtonImage.SetDisplay(false);

            m_FeedbackCommentFoldout = m_FeedbackParamSection.Q<Foldout>("commentFoldout");
            m_FeedbackCommentFoldout.value = false;
            m_FeedbackCommentFoldout.RegisterValueChangedCallback(_ =>
            {
                Context.SendScrollToEndRequest();
            });

            m_FeedbackPlaceholderContent.RegisterCallback<ClickEvent>(_ => m_FeedbackText.Focus());

            CheckFeedbackState();
        }

        void SetFeedbackTextFocused(bool state)
        {
            m_FeedbackTextFocused = state;

            CheckFeedbackState();
        }

        void CheckFeedbackState()
        {
            m_FeedbackSendButton.SetEnabled(!string.IsNullOrEmpty(m_FeedbackText.value));
            m_FeedbackPlaceholderContent.SetDisplay(!m_FeedbackTextFocused && string.IsNullOrEmpty(m_FeedbackText.value));
        }

        void OnSendFeedback(PointerUpEvent evt)
        {
            if (string.IsNullOrEmpty(m_FeedbackText.value))
            {
                ErrorHandlingUtils.ShowGeneralError($"Failed to send Feedback: 'your feedback' section is empty");
                return;
            }

            string message = m_FeedbackText.value.Trim();

            if (m_FeedbackMode != FeedbackEditMode.DownVote && m_FeedbackMode != FeedbackEditMode.UpVote)
            {
                ErrorHandlingUtils.ShowGeneralError($"Failed to send Feedback: Sentiment must be set");
                return;
            }

            if (m_FeedbackFlagInappropriateCheckbox.value)
            {
                message += " (Message was flagged as inappropriate.)";
            }

            Context.API.SendFeedback(m_MessageId, m_FeedbackFlagInappropriateCheckbox.value, message, m_FeedbackMode == FeedbackEditMode.UpVote);

            if (k_StoredFeedbackUIState.TryGetValue(m_Message.Id, out var feedbackData))
            {
                // Null is intentional since we clear the sent text at this point
                var newFeedbackData = new FeedbackData(feedbackData.Sentiment, null);
                StoreFeedbackUIState(m_Message.Id, newFeedbackData);
            }

            m_FeedbackSendButton.EnableInClassList(AssistantUIConstants.ActiveActionButtonClass, true);
            m_FeedbackSendButtonLabel.text = AssistantUIConstants.FeedbackButtonSentTitle;
            m_FeedbackSendButtonImage.SetDisplay(true);

            ClearFeedbackParameters();

            TimerUtils.DelayedAction(ref m_FeedbackSendButtonTokenSource, () =>
            {
                m_FeedbackSendButton.EnableInClassList(AssistantUIConstants.ActiveActionButtonClass, false);
                m_FeedbackSendButtonLabel.text = AssistantUIConstants.FeedbackButtonDefaultTitle;

                m_FeedbackSendButtonImage.SetDisplay(false);

                m_FeedbackCommentFoldout.value = false;
                m_FeedbackText.value = string.Empty;
                RefreshFeedbackParameters();
            });
        }

        void ClearFeedbackParameters()
        {
            m_FeedbackFlagInappropriateCheckbox.value = false;
            m_FeedbackText.value = string.Empty;
            RefreshFeedbackParameters();
        }

        void OnDownvoteClicked(PointerUpEvent evt)
        {
            if (m_FeedbackMode == FeedbackEditMode.DownVote)
            {
                return;
            }

            m_FeedbackPlaceholder.text = AssistantUIConstants.FeedbackDownVotePlaceholder;

            Context.API.SendFeedback(m_MessageId, m_FeedbackFlagInappropriateCheckbox.value, string.Empty, false);

            var newFeedbackData = new FeedbackData(Sentiment.Negative, m_FeedbackText.value);
            StoreFeedbackUIState(m_Message.Id, newFeedbackData);

            m_FeedbackMode = FeedbackEditMode.DownVote;
            RefreshFeedbackParameters();
        }

        void OnUpvoteClicked(PointerUpEvent evt)
        {
            if (m_FeedbackMode == FeedbackEditMode.UpVote)
            {
                return;
            }

            m_FeedbackPlaceholder.text = AssistantUIConstants.FeedbackUpVotePlaceholder;

            Context.API.SendFeedback(m_MessageId, false, string.Empty, true);

            var newFeedbackData = new FeedbackData(Sentiment.Positive, m_FeedbackText.value);
            StoreFeedbackUIState(m_Message.Id, newFeedbackData);

            m_FeedbackMode = FeedbackEditMode.UpVote;
            RefreshFeedbackParameters();
            m_FeedbackFlagInappropriateCheckbox.value = false;
        }

        void OnCopyClicked(PointerUpEvent evt)
        {
            // Format message with footnotes (indices to sources)
            IList<SourceBlock> sourceBlocks = new List<SourceBlock>();

            var outMessage = string.Empty;
            foreach (var block in m_Message.Blocks)
            {
                if (block is not AnswerBlockModel responseBlockModel)
                    continue;

                MessageUtils.ProcessContent(responseBlockModel.Content, responseBlockModel.IsComplete, ref sourceBlocks, out var outBlockMessage, MessageUtils.FootnoteFormat.SimpleIndexForClipboard);
                outMessage += outBlockMessage;
            }

            // Add sources in same order of footnote indices
            MessageUtils.AppendSourceBlocks(sourceBlocks, ref outMessage);

            GUIUtility.systemCopyBuffer = string.Concat(AssistantConstants.GetDisclaimerHeader(), outMessage);

            m_CopyButton.EnableInClassList(AssistantUIConstants.ActiveActionButtonClass, true);
            m_CopyButtonImage.SetOverrideIconClass("checkmark");
            TimerUtils.DelayedAction(ref m_ResponseCopyButtonActiveTokenSource, () =>
            {
                m_CopyButton.EnableInClassList(AssistantUIConstants.ActiveActionButtonClass, false);
                m_CopyButtonImage.SetOverrideIconClass(null);
            });

            AIAssistantAnalytics.ReportUITriggerLocalCopyResponseEvent(m_MessageId, outMessage);
        }

        void RefreshFeedbackParameters(bool initialLoadedState = false)
        {
            if (m_Message.Role == MessageModelRole.Error || !m_Message.IsComplete)
            {
                m_CopyButton.SetEnabled(false);
                m_UpVoteButton.SetEnabled(false);
                m_DownVoteButton.SetEnabled(false);
                m_FeedbackParamSection.style.display = DisplayStyle.None;
                return;
            }

            m_CopyButton.SetEnabled(true);
            m_UpVoteButton.SetEnabled(true);
            m_DownVoteButton.SetEnabled(true);

            switch (m_FeedbackMode)
            {
                case FeedbackEditMode.None:
                {
                    m_FeedbackParamSection.style.display = DisplayStyle.None;
                    m_UpVoteButton.RemoveFromClassList(k_FeedbackButtonActiveClass);
                    m_DownVoteButton.RemoveFromClassList(k_FeedbackButtonActiveClass);
                    return;
                }

                case FeedbackEditMode.DownVote:
                {
                    m_FeedbackParamSection.style.display = DisplayStyle.Flex;
                    m_FeedbackFlagInappropriateCheckbox.style.display = DisplayStyle.Flex;
                    m_UpVoteButton.RemoveFromClassList(k_FeedbackButtonActiveClass);
                    m_DownVoteButton.AddToClassList(k_FeedbackButtonActiveClass);

                    if (!initialLoadedState)
                        Context.SendScrollToEndRequest();

                    break;
                }

                case FeedbackEditMode.UpVote:
                {
                    m_FeedbackParamSection.style.display = DisplayStyle.Flex;
                    m_FeedbackFlagInappropriateCheckbox.style.display = DisplayStyle.None;
                    m_UpVoteButton.AddToClassList(k_FeedbackButtonActiveClass);
                    m_DownVoteButton.RemoveFromClassList(k_FeedbackButtonActiveClass);

                    if (!initialLoadedState)
                        Context.SendScrollToEndRequest();

                    break;
                }
            }
        }

        void SetFeedback(AssistantMessageId assistantMessageId, FeedbackData? feedbackData)
        {
            if (assistantMessageId != m_MessageId)
                return;

            if (feedbackData == null)
                return;

            if (feedbackData.Value.Sentiment == Sentiment.Positive)
            {
                m_FeedbackMode = FeedbackEditMode.UpVote;
                m_FeedbackPlaceholder.text = AssistantUIConstants.FeedbackUpVotePlaceholder;
            }
            else
            {
                m_FeedbackMode = FeedbackEditMode.DownVote;
                m_FeedbackPlaceholder.text = AssistantUIConstants.FeedbackDownVotePlaceholder;
            }

            m_FeedbackText.value = string.Empty;

            RefreshFeedbackParameters(true);
        }

        static void SetCurrentConversation(AssistantConversationId conversationId)
        {
            if (s_CurrentStoredConversationFeedbacks == conversationId)
                return;

            k_StoredFeedbackUIState.Clear();

            s_CurrentStoredConversationFeedbacks = conversationId;
        }

        void StoreFeedbackUIState(AssistantMessageId assistantMessageId, FeedbackData feedbackData)
        {
            k_StoredFeedbackUIState[assistantMessageId] = feedbackData;
        }
    }
}
