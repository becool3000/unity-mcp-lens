using Unity.AI.Assistant.Editor.Utils.Event;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Events
{
    class EventExpandedPanelOpened : IAssistantEvent
    {
        public string Title { get; }

        public EventExpandedPanelOpened(string title)
        {
            Title = title;
        }
    }
}
