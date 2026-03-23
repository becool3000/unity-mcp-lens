
using Unity.AI.Assistant.ApplicationModels;
using Unity.AI.Assistant.Data;

namespace Unity.AI.Assistant.Backend
{
    struct MessageFeedback
    {
        public AssistantMessageId MessageId;
        public bool FlagInappropriate;
        public Category Type;
        public string Message;
        public Sentiment Sentiment;
    }
}
