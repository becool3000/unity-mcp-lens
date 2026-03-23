using Unity.AI.Assistant.Editor.Utils.Event;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Events
{
    class EventGetRevertedTimeStampFilter : IAssistantEvent
    {
        public long Timestamp { get; set; }
    }
}
