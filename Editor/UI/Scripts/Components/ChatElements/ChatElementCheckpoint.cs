using System;
using System.Linq;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Checkpoint;
using Unity.AI.Assistant.Editor.Checkpoint.Events;
using Unity.AI.Assistant.Editor.Utils.Event;
using Unity.AI.Assistant.UI.Editor.Scripts.Data;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    [UxmlElement]
    partial class ChatElementCheckpoint : ManagedTemplate
    {
        static readonly string k_RevertDialogPrefsKey = AssistantEditorPreferences.k_SettingsPrefix + "RestoreCheckpointDialog_NeverAskAgain";

        const string k_ToolTipCheckpointsDisabled = "Enable Checkpoints in Unity Preferences to roll back changes.";
        const string k_ToolTipLatestCheckpoint = "You're caught up! No further changes.";
        const string k_ToolTipRestoreCheckpoint = "Restore this checkpoint";
        
        VisualElement m_CheckpointSection;
        VisualElement m_CheckpointTextSection;

        Button m_CheckpointButton;
        AssistantImage m_CheckpointButtonImage;
        
        AssistantMessageId m_AnchorMessageId;
        int m_MessageOffset;
        AssistantMessageId m_TargetMessageId;
        AssistantMessageId m_ResponseMessageId;
        
        BaseEventSubscriptionTicket m_CheckpointsChangedSubscription;

        public ChatElementCheckpoint() : base(AssistantUIConstants.UIModulePath) { }

        public bool CheckpointValid { get; private set;}
        public bool ShowCheckpointLabel { get; set; } = false;
        
        public event Action OnValidated;

        protected override void InitializeView(TemplateContainer view)
        {
            m_CheckpointSection = view.Q("checkpointElementSection");
            m_CheckpointTextSection = view.Q("checkpointTextSection");
            m_CheckpointButton = view.SetupButton("checkpointButton", OnCheckpointClicked);
            m_CheckpointButtonImage = m_CheckpointButton.SetupImage("checkpointButtonImage", "checkpoint");

            m_CheckpointSection.SetDisplay(false);
            CheckpointValid = false;
            m_CheckpointTextSection.SetDisplay(ShowCheckpointLabel);
            RegisterAttachEvents(OnAttach, OnDetach);
        }

        void OnAttach(AttachToPanelEvent evt)
        {
            m_CheckpointsChangedSubscription = AssistantEvents.Subscribe<EventCheckpointsChanged>(OnCheckpointsChanged);
        }

        void OnDetach(DetachFromPanelEvent evt)
        {
            AssistantEvents.Unsubscribe(ref m_CheckpointsChangedSubscription);
        }

        public void SetCheckpointData(AssistantMessageId messageId, int offset = 0)
        {
            m_AnchorMessageId = messageId;
            m_MessageOffset = offset;

            ValidateCheckpoint();
        }
        
        void OnCheckpointsChanged(EventCheckpointsChanged eventData)
        {
            if (CheckpointValid)
                return;
            
            ValidateCheckpoint();
        }

        void ValidateCheckpoint()
        {
            CheckpointValid = false;
            m_TargetMessageId = default;
            m_ResponseMessageId = default;
            m_CheckpointSection.SetEnabled(false);
            m_CheckpointSection.SetDisplay(false);
            
            if (!AssistantProjectPreferences.CheckpointEnabled)
            {
                m_CheckpointSection.SetDisplay(true);
                m_CheckpointSection.SetEnabled(false);
                m_CheckpointButton.tooltip = k_ToolTipCheckpointsDisabled;
                return;
            }
            
            if (IsLastNonRevertedMessage())
            {
                m_CheckpointSection.SetDisplay(true);
                m_CheckpointSection.SetEnabled(false);
                m_CheckpointButton.tooltip = k_ToolTipLatestCheckpoint;
                return;
            }

            var hasMessageIds = TryUpdateTargetMessage(out var outOfRange);

            if (m_ResponseMessageId.Type != AssistantMessageIdType.External)
            {
                return;
            }
            
            if (!AssistantCheckpoints.HasCheckpointForMessage(m_ResponseMessageId.ConversationId, m_ResponseMessageId.FragmentId))
            {
                return;
            }

            m_CheckpointSection.SetDisplay(true);

            CheckpointValid = true;
            OnValidated?.Invoke();
            
            m_CheckpointSection.SetEnabled(hasMessageIds);
            m_CheckpointButton.tooltip = k_ToolTipRestoreCheckpoint;
        }

        bool TryGetConversationAndMessageIndex(out ConversationModel conv, out int messageIndex)
        {
            conv = Context.Blackboard.Conversations.Where(c => c.Id == m_AnchorMessageId.ConversationId).FirstOrDefault();
            messageIndex = -1;

            if (conv == null)
                return false;

            messageIndex = conv.Messages.FindIndex(msg => msg.Id == m_AnchorMessageId);
            return messageIndex != -1;
        }

        int FindNextNonRevertedUserMessage(ConversationModel conv, int startIndex)
        {
            var currentIndex = startIndex;
            while (currentIndex < conv.Messages.Count)
            {
                var message = conv.Messages[currentIndex];
                if (message.RevertedTimeStamp == 0 && message.Role == MessageModelRole.User)
                {
                    return currentIndex;
                }
                currentIndex++;
            }
            return -1;
        }

        bool TryUpdateTargetMessage(out bool outOfRange)
        {
            outOfRange = false;

            if (!TryGetConversationAndMessageIndex(out var conv, out var messageIndex))
                return false;

            var currentMessageIndex = messageIndex;

            // Go to next user message according to offset
            if (m_MessageOffset > 0)
            {
                currentMessageIndex++;
                var offsetRemaining = m_MessageOffset;

                while (currentMessageIndex < conv.Messages.Count && offsetRemaining > 0)
                {
                    var currentMsg = conv.Messages[currentMessageIndex];
                    if (currentMsg.Role != MessageModelRole.User)
                    {
                        currentMessageIndex++;
                        continue;
                    }
                    offsetRemaining--;
                    if (offsetRemaining == 0)
                    {
                        break;
                    }
                }
                
            }

            currentMessageIndex = FindNextNonRevertedUserMessage(conv, currentMessageIndex);

            outOfRange = (currentMessageIndex == -1);
            if (!outOfRange)
            {
                m_TargetMessageId = conv.Messages[currentMessageIndex].Id;

                if (currentMessageIndex + 1 >= conv.Messages.Count)
                    return false;

                m_ResponseMessageId = conv.Messages[currentMessageIndex + 1].Id;
            }

            return !outOfRange;
        }

        bool IsLastNonRevertedMessage()
        {
            if (!TryGetConversationAndMessageIndex(out var conv, out var messageIndex))
                return false;

            // Check if there are any non-reverted user messages after this one
            var nextUserIndex = FindNextNonRevertedUserMessage(conv, messageIndex);
            return nextUserIndex == -1;
        }
        
        async void OnCheckpointClicked(PointerUpEvent evt)
        {
            if (!CheckpointValid)
            {
                return;
            }

            var userChoice = EditorUtility.DisplayDialog(
                "Restore Editor to this checkpoint?",
                "This action will discard all changes made after this checkpoint, even if done outside the Unity AI Assistant.",
                "Yes, discard changes",
                "Cancel",
                DialogOptOutDecisionType.ForThisMachine,
                k_RevertDialogPrefsKey
            );

            if (!userChoice)
                return;
            
            // Trigger the local rollback
            AssistantEvents.Send(new EventCheckpointRestoreRequested(m_ResponseMessageId));

            // Revert and wait for completion, then refresh; handle exceptions inside those two calls
            await Context.API.RevertMessage(m_TargetMessageId);
            Context.API.ConversationLoad(Context.Blackboard.ActiveConversationId);
        }
    }
}
