using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor.Utils.Event;

namespace Unity.AI.Assistant.Editor.Checkpoint.Events
{
    class EventCheckpointRestoreCompleted : IAssistantEvent
    {
        public EventCheckpointRestoreCompleted(AssistantMessageId messageId, bool success, string message)
        {
            MessageId = messageId;
            Success = success;
            Message = message;
        }

        public AssistantMessageId MessageId { get; }
        public bool Success { get; }
        public string Message { get; }
    }
}
