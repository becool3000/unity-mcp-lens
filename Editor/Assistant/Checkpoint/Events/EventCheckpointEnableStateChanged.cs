using Unity.AI.Assistant.Editor.Utils.Event;

namespace Unity.AI.Assistant.Editor.Checkpoint.Events
{
    class EventCheckpointEnableStateChanged : IAssistantEvent
    {
        public EventCheckpointEnableStateChanged(bool state)
        {
            this.Enabled = state;
        }
        
        public bool Enabled { get; private set; }
    }
}
