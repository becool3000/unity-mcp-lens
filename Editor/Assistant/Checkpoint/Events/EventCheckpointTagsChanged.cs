using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor.Utils.Event;

namespace Unity.AI.Assistant.Editor.Checkpoint.Events
{
    class EventCheckpointTagsChanged : IAssistantEvent
    {
        public EventCheckpointTagsChanged(AssistantConversationId conversationId, string fragmentId)
        {
            ConversationId = conversationId;
            FragmentId = fragmentId;
        }

        public AssistantConversationId ConversationId { get; }
        public string FragmentId { get; }
    }
}
