using Unity.AI.Assistant.Editor.Utils.Event;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Events
{
    class EventRevertedTimeStampFilterRequested : IAssistantEvent
    {
        public EventRevertedTimeStampFilterRequested(long timestamp)
        {
            Timestamp = timestamp;
        }

        public long Timestamp { get; }
    }
}
