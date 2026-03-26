using Unity.AI.Assistant.Editor.Utils.Event;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Events
{
    class EventExpandedViewRequested : IAssistantEvent
    {
        public string Title { get; }
        public VisualElement Element { get; }

        public EventExpandedViewRequested(string title, VisualElement element)
        {
            Title = title;
            Element = element;
        }
    }
}
