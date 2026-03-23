using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Assistant.Data;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.UI.Editor.Scripts
{
    internal class AssistantUISessionState : ScriptableSingleton<AssistantUISessionState>
    {
        const string k_Prefix = "AssistantUserSession_";

        const string k_HistoryOpen = k_Prefix + "HistoryOpen";
        const string k_LastActiveConversationId = k_Prefix + "LastActiveConversationId";
        const string k_LastActiveProviderId = k_Prefix + "LastActiveProviderId";
        const string k_LastActiveMode = k_Prefix + "LastActiveMode";
        const string k_IncompleteMessageId = k_Prefix + "IncompleteMessageId";
        const string k_Prompt = k_Prefix + "Prompt";
        const string k_Command = k_Prefix + "Command";
        const string k_Context = k_Prefix + "Context";
        const string k_AvailableCommands = k_Prefix + "AvailableCommands_";

        [Serializable]
        class AvailableCommandsCache
        {
            public List<CommandEntry> commands = new();
        }

        [Serializable]
        struct CommandEntry
        {
            public string name;
            public string description;
        }

        public bool IsHistoryOpen
        {
            get => SessionState.GetBool(k_HistoryOpen, false);
            set => SessionState.SetBool(k_HistoryOpen, value);
        }

        public string LastActiveConversationId
        {
            get => SessionState.GetString(k_LastActiveConversationId, null);
            set => SessionState.SetString(k_LastActiveConversationId, value);
        }

        public string LastActiveProviderId
        {
            get => SessionState.GetString(k_LastActiveProviderId, null);
            set => SessionState.SetString(k_LastActiveProviderId, value);
        }

        public AssistantMode LastActiveMode
        {
            get
            {
                var stored = SessionState.GetString(k_LastActiveMode, null);
                if (Enum.TryParse<AssistantMode>(stored, out var mode))
                    return mode;

                return AssistantMode.Agent;
            }
            set => SessionState.SetString(k_LastActiveMode, value.ToString());
        }

        public string IncompleteMessageId
        {
            get => SessionState.GetString(k_IncompleteMessageId, null);
            set => SessionState.SetString(k_IncompleteMessageId, value);
        }

        public string Context
        {
            get => SessionState.GetString(k_Context, null);
            set => SessionState.SetString(k_Context, value);
        }

        public string Prompt
        {
            get => SessionState.GetString(k_Command, null);
            set => SessionState.SetString(k_Command, value);
        }

        public string Command
        {
            get => SessionState.GetString(k_Prompt, null);
            set => SessionState.SetString(k_Prompt, value);
        }

        public void SetAvailableCommands(string providerId, IReadOnlyList<(string name, string description)> commands)
        {
            if (string.IsNullOrEmpty(providerId))
                return;

            var key = k_AvailableCommands + providerId;
            if (commands == null || commands.Count == 0)
            {
                SessionState.EraseString(key);
                return;
            }

            var cache = new AvailableCommandsCache
            {
                commands = commands
                    .Select(c => new CommandEntry { name = c.name, description = c.description })
                    .ToList()
            };

            SessionState.SetString(key, JsonUtility.ToJson(cache));
        }

        public (string name, string description)[] GetAvailableCommands(string providerId)
        {
            if (string.IsNullOrEmpty(providerId))
                return null;

            var json = SessionState.GetString(k_AvailableCommands + providerId, null);
            if (string.IsNullOrEmpty(json))
                return null;

            var cache = JsonUtility.FromJson<AvailableCommandsCache>(json);
            if (cache?.commands == null || cache.commands.Count == 0)
                return null;

            return cache.commands
                .Select(c => (c.name, c.description))
                .ToArray();
        }
    }
}
