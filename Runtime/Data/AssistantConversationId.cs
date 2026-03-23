using System;
using System.Diagnostics;

namespace Unity.AI.Assistant.Data
{
    [DebuggerDisplay("{k_Value}")]
    internal struct AssistantConversationId: IEquatable<AssistantConversationId>
    {
        private const string k_InternalIdPrefix = "INT_";
        private static int s_NextInternalId = 1;

        public static AssistantConversationId Invalid => default;

        private bool m_IsInternal;

        private readonly string k_Value;

        // -------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------
        public AssistantConversationId(string value)
        {
            k_Value = value;
            m_IsInternal = false;
        }

        public static AssistantConversationId GetNextInternalId()
        {
            return new AssistantConversationId($"{k_InternalIdPrefix}{s_NextInternalId++}") { m_IsInternal = true };
        }

        // -------------------------------------------------------------------
        // Public
        // -------------------------------------------------------------------
        public string Value => m_IsInternal ? null : k_Value;

        public static bool operator ==(AssistantConversationId value1, AssistantConversationId value2)
        {
            return value1.Equals(value2);
        }

        public static bool operator !=(AssistantConversationId value1, AssistantConversationId value2)
        {
            return !(value1 == value2);
        }

        public override bool Equals(object obj)
        {
            return obj is AssistantConversationId other && Equals(other);
        }

        public bool Equals(AssistantConversationId other)
        {
            return k_Value == other.k_Value && m_IsInternal == other.m_IsInternal;
        }

        public override int GetHashCode()
        {
            return string.IsNullOrEmpty(k_Value) ? 0 : k_Value.GetHashCode();
        }

        public override string ToString()
        {
            return k_Value ?? string.Empty;
        }

        public bool IsValid => !string.IsNullOrEmpty(k_Value) && !m_IsInternal;
    }
}
