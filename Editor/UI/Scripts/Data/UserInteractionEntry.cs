using System;
using Unity.AI.Assistant.UI.Editor.Scripts.Components.UserInteraction;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Data
{
    class UserInteractionEntry
    {
        public UserInteractionId Id { get; internal set; }
        public string Title { get; set; }
        public string Detail { get; set; }

        public InteractionContentView ContentView { get; set; }

        public Action OnCancel { get; set; }
    }
}
