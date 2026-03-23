using System;
using Unity.AI.Assistant.Data;

namespace Unity.AI.Assistant.Editor.Checkpoint
{
    readonly struct CheckpointInfo : IEquatable<CheckpointInfo>
    {
        readonly string m_Hash;

        public string Hash => m_Hash;
        public string Message { get; }
        public long Timestamp { get; }
        public string ConversationId { get; }
        public string FragmentId { get; }

        public bool HasMessageId => !string.IsNullOrEmpty(ConversationId) && !string.IsNullOrEmpty(FragmentId);

        public CheckpointInfo(string hash, string message, long timestamp, string conversationId = null, string fragmentId = null)
        {
            m_Hash = hash;
            Message = message;
            Timestamp = timestamp;
            ConversationId = conversationId;
            FragmentId = fragmentId;
        }

        public AssistantMessageId GetMessageId()
        {
            if (!HasMessageId)
            {
                return AssistantMessageId.Invalid;
            }

            return new AssistantMessageId(
                new AssistantConversationId(ConversationId),
                FragmentId,
                AssistantMessageIdType.External);
        }

        public bool Equals(CheckpointInfo other) => string.Equals(m_Hash, other.m_Hash, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is CheckpointInfo other && Equals(other);
        public override int GetHashCode() => m_Hash?.GetHashCode() ?? 0;

        public static bool operator ==(CheckpointInfo left, CheckpointInfo right) => left.Equals(right);
        public static bool operator !=(CheckpointInfo left, CheckpointInfo right) => !left.Equals(right);
    }
}
