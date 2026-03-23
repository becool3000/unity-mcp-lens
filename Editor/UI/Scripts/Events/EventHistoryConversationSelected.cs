using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor.Utils.Event;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Events
{
    class EventHistoryConversationSelected : IAssistantEvent
    {
        public EventHistoryConversationSelected(AssistantConversationId id)
        {
            Id = id;
        }
        
        public AssistantConversationId Id { get; }
    }
}
