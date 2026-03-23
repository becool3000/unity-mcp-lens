namespace Unity.AI.Assistant.Data
{
    internal struct AssistantConversationInfo
    {
        public AssistantConversationId Id;
        public string Title;
        public long LastMessageTimestamp;
        public bool IsContextual;
        public bool IsFavorite;
    }
}
