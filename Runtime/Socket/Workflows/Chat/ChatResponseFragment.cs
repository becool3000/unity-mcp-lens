namespace Unity.AI.Assistant.Socket.Workflows.Chat
{
    struct ChatResponseFragment
    {
        public string Fragment { get; set; }
        public string Id { get; set; }
        public bool IsLastFragment { get; set; }
    }
}
