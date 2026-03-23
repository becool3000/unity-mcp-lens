using System;
using System.Collections.Generic;
using Unity.AI.Assistant.Bridge.Editor;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Context;
using Unity.AI.Assistant.UI.Editor.Scripts.Data;
using Unity.AI.Assistant.Utils;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.UI.Editor.Scripts
{
    class AssistantBlackboard
    {
        public delegate void ActiveConversationChangedDelegate(AssistantConversationId previousConversationId, AssistantConversationId currentConversationId);
        public delegate void ActiveModeChangedDelegate(AssistantMode previousMode, AssistantMode currentMode);

        ConversationModel m_ActiveConversation;
        AssistantMode m_ActiveMode = AssistantMode.Agent;
        bool m_ConversationChangeLocked = false;

        readonly IDictionary<AssistantConversationId, bool> k_FavoriteCache = new Dictionary<AssistantConversationId, bool>();
        readonly IDictionary<AssistantConversationId, ConversationModel> k_ConversationCache = new Dictionary<AssistantConversationId, ConversationModel>();

        readonly List<Object> k_ObjectAttachments = new();
        readonly List<VirtualAttachment> k_VirtualAttachments = new();
        readonly List<LogData> k_ConsoleAttachments = new();

        bool m_StateSaveSuspended;

        public IReadOnlyCollection<Object> ObjectAttachments => k_ObjectAttachments.AsReadOnly();
        public IReadOnlyCollection<VirtualAttachment> VirtualAttachments => k_VirtualAttachments.AsReadOnly();
        public IReadOnlyCollection<LogData> ConsoleAttachments => k_ConsoleAttachments.AsReadOnly();

        public ICollection<ConversationModel> Conversations => k_ConversationCache.Values;

        public AssistantConversationId ActiveConversationId { get; private set; }

        public event ActiveConversationChangedDelegate ActiveConversationChanged;
        public event ActiveModeChangedDelegate ActiveModeChanged;

        public bool IsAPIWorking;
        public bool IsAPIRepairing;
        public bool IsAPIStreaming;
        public bool IsAPICanceling;
        public bool IsAPIReadyForPrompt = true;

        public void SetActiveConversation(AssistantConversationId newConversationId)
        {
            if (m_ConversationChangeLocked)
            {
                // Conversation Change is locked, we are not ready yet
                InternalLog.LogWarning("Ignoring Conversation Change, Currently Locked: " + newConversationId);
                return;
            }

            if (ActiveConversationId.IsValid && ActiveConversationId == newConversationId)
            {
                // Same valid conversation, ignore
                return;
            }

            var previousId = ActiveConversationId;
            ActiveConversationId = newConversationId;
            m_ActiveConversation = null;

            ActiveConversationChanged?.Invoke(previousId, newConversationId);

            SaveActiveConversationState();
        }
        
        public void SetFavorite(AssistantConversationId id, bool state)
        {
            k_FavoriteCache[id] = state;
        }

        public bool GetFavorite(AssistantConversationId id)
        {
            if (k_FavoriteCache.TryGetValue(id, out bool state))
            {
                return state;
            }

            return false;
        }

        public void UpdateConversation(AssistantConversationId id, ConversationModel conversation)
        {
            k_ConversationCache[id] = conversation;
        }

        public ConversationModel GetConversation(AssistantConversationId id)
        {
            if (k_ConversationCache.TryGetValue(id, out var conversation))
            {
                return conversation;
            }

            return null;
        }

        public bool RemoveConversation(AssistantConversationId id)
        {
            return k_ConversationCache.Remove(id);
        }

        public ConversationModel ActiveConversation
        {
            get
            {
                if (!ActiveConversationId.IsValid)
                {
                    return null;
                }

                if (m_ActiveConversation != null)
                {
                    return m_ActiveConversation;
                }

                m_ActiveConversation = GetConversation(ActiveConversationId);
                return m_ActiveConversation;
            }
        }

        public AssistantMode ActiveMode
        {
            get => m_ActiveMode;
            set
            {
                if (m_ActiveMode == value)
                    return;

                var prevValue = m_ActiveMode;
                m_ActiveMode = value;
                ActiveModeChanged?.Invoke(prevValue, value);

                SaveActiveModeState();
            }
        }

        public void ClearActiveConversation(bool lockChange = false)
        {
            ActiveConversationId = AssistantConversationId.Invalid;
            m_ActiveConversation = null;

            if (lockChange)
            {
                m_ConversationChangeLocked = lockChange;
            }

            SaveActiveConversationState();
        }

        public void ClearConversations()
        {
            k_ConversationCache.Clear();
            k_FavoriteCache.Clear();
        }

        public void UnlockConversationChange()
        {
            m_ConversationChangeLocked = false;
        }

        public void ClearAttachments()
        {
            k_ObjectAttachments.Clear();
            k_VirtualAttachments.Clear();
            k_ConsoleAttachments.Clear();

            SaveContextState();
        }

        public void AddVirtualAttachment(VirtualAttachment attachment)
        {
            k_VirtualAttachments.Add(attachment);

            SaveContextState();
        }

        public void RemoveVirtualAttachment(VirtualAttachment attachment)
        {
            k_VirtualAttachments.Remove(attachment);

            SaveContextState();
        }

        public void ReplaceVirtualAttachment(VirtualAttachment oldAttachment, VirtualAttachment newAttachment)
        {
            var index = k_VirtualAttachments.IndexOf(oldAttachment);
            if (index >= 0)
            {
                k_VirtualAttachments[index] = newAttachment;
                SaveContextState();
            }
        }

        public void AddConsoleAttachment(LogData data)
        {
            k_ConsoleAttachments.Add(data);

            SaveContextState();
        }

        public void AddObjectAttachment(Object obj)
        {
            if (obj == null)
            {
                return;
            }

            k_ObjectAttachments.Add(obj);

            SaveContextState();
        }

        void SaveContextState()
        {
            if (m_StateSaveSuspended)
            {
                return;
            }

            AssistantUISessionState.instance.Context =
                JsonUtility.ToJson(ContextSerializationHelper.BuildPromptSelectionContext(k_ObjectAttachments, k_VirtualAttachments, k_ConsoleAttachments));
        }

        void SaveActiveConversationState()
        {
            if (m_StateSaveSuspended)
            {
                return;
            }

            // For non-Unity providers (ACP), the provider saves the correct agent session ID
            // in OnAgentSessionIdReceived. Saving here would persist the Unity routing ID,
            // which can't be used to look up the conversation in storage.
            if (!ProviderStateObserver.IsUnityProvider)
            {
                return;
            }

            AssistantUISessionState.instance.LastActiveConversationId = ActiveConversationId.Value;
        }

        void SaveActiveModeState()
        {
            if (m_StateSaveSuspended)
            {
                return;
            }

            AssistantUISessionState.instance.LastActiveMode = ActiveMode;
        }

        public void SuspendStateSave()
        {
            m_StateSaveSuspended = true;
        }

        public void ResumeStateSave()
        {
            m_StateSaveSuspended = false;
        }

        public void SetIncompleteMessageId(string messageId)
        {
            if (m_StateSaveSuspended)
            {
                return;
            }

            AssistantUISessionState.instance.IncompleteMessageId = messageId;
        }

        public void ClearIncompleteMessageId()
        {
            if (m_StateSaveSuspended)
            {
                return;
            }

            AssistantUISessionState.instance.IncompleteMessageId = null;
        }
    }
}
