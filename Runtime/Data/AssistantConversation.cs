using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.AI.Assistant.Data
{
    [Serializable]
    class AssistantConversation
    {
        public string Title;
        public AssistantConversationId Id;
        public readonly List<AssistantMessage> Messages = new();

        // ACP-specific fields for session management and persistence
        public string AgentSessionId;
        public string ProviderId;
        public long CreatedTimestamp;
        public long LastMessageTimestamp;
        public bool IsFavorite;
        public int Version = 1;

        /// <summary>
        /// Serializes the conversation to a JSON string for persistent storage.
        /// </summary>
        public string ToJson()
        {
            var json = new JObject
            {
                ["agentSessionId"] = AgentSessionId,
                ["providerId"] = ProviderId,
                ["title"] = Title,
                ["createdTimestamp"] = CreatedTimestamp,
                ["lastMessageTimestamp"] = LastMessageTimestamp,
                ["isFavorite"] = IsFavorite,
                ["version"] = Version,
                ["messages"] = SerializeMessages()
            };

            return json.ToString(Formatting.Indented);
        }

        /// <summary>
        /// Deserializes a conversation from a JSON string.
        /// </summary>
        public static AssistantConversation FromJson(string json)
        {
            var jObject = JObject.Parse(json);

            var conversation = new AssistantConversation
            {
                AgentSessionId = jObject["agentSessionId"]?.ToString(),
                ProviderId = jObject["providerId"]?.ToString(),
                Title = jObject["title"]?.ToString(),
                CreatedTimestamp = jObject["createdTimestamp"]?.Value<long>() ?? 0,
                LastMessageTimestamp = jObject["lastMessageTimestamp"]?.Value<long>() ?? 0,
                IsFavorite = jObject["isFavorite"]?.Value<bool>() ?? false,
                Version = jObject["version"]?.Value<int>() ?? 1
            };

            var messagesArray = jObject["messages"] as JArray;
            if (messagesArray != null)
            {
                foreach (var msgToken in messagesArray)
                {
                    var message = AssistantMessage.FromJson(msgToken as JObject);
                    if (message != null)
                    {
                        conversation.Messages.Add(message);
                    }
                }
            }

            return conversation;
        }

        private JArray SerializeMessages()
        {
            var array = new JArray();
            foreach (var message in Messages)
            {
                array.Add(message.ToJson());
            }
            return array;
        }
    }
}
