using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor.Utils.Event;

namespace Unity.AI.Assistant.Editor.Checkpoint.Events
{
    class EventCheckpointRestoreRequested : IAssistantEvent
    {
        public EventCheckpointRestoreRequested(AssistantMessageId messageId)
        {
            MessageId = messageId;
        }

        public AssistantMessageId MessageId { get; }
    }
}
