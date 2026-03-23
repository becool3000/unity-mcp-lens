using System;
using System.Diagnostics;

namespace Unity.AI.Assistant.Data
{
    [DebuggerDisplay("{Type}:{ConversationId} #{FragmentId}")]
    struct AssistantMessageId
    {
        const string k_InternalIdPrefix = "INT_";
        const string k_IncompletePrefix = "INC_";

        static int k_NextInternalId = 1;
        static int k_NextIncompleteId = 1;

        internal static readonly AssistantMessageId Invalid = default;

        // -------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------
        public AssistantMessageId(AssistantConversationId conversationId, string fragmentId, AssistantMessageIdType type)
        {
            ConversationId = conversationId;
            FragmentId = fragmentId;
            Type = type;
        }

        // -------------------------------------------------------------------
        // Public
        // -------------------------------------------------------------------
        public readonly AssistantConversationId ConversationId;
        public readonly string FragmentId;
        public readonly AssistantMessageIdType Type;

        public static AssistantMessageId GetNextInternalId(AssistantConversationId conversationId)
        {
            return new AssistantMessageId(conversationId, $"{k_InternalIdPrefix}{k_NextInternalId++}", AssistantMessageIdType.Internal);
        }

        public static AssistantMessageId GetNextIncompleteId(AssistantConversationId conversationId)
        {
            return new AssistantMessageId(conversationId, $"{k_IncompletePrefix}{k_NextIncompleteId++}", AssistantMessageIdType.Incomplete);
        }

        public static bool operator ==(AssistantMessageId value1, AssistantMessageId value2)
        {
            return value1.Equals(value2);
        }

        public static bool operator !=(AssistantMessageId value1, AssistantMessageId value2)
        {
            return !(value1 == value2);
        }

        public override bool Equals(object obj)
        {
            return obj is AssistantMessageId other && Equals(other);
        }

        public bool Equals(AssistantMessageId other)
        {
            return Type == other.Type && ConversationId == other.ConversationId && FragmentId == other.FragmentId;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Type, ConversationId, FragmentId ?? string.Empty);
        }

        public override string ToString()
        {
            return $"{Type}:{ConversationId} #{FragmentId ?? string.Empty}";
        }
    }
}
