using System;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.UserInteraction
{
    abstract class InteractionContentView : ManagedTemplate
    {
        public event Action Completed;

        public new bool IsInitialized => base.IsInitialized;

        protected InteractionContentView()
            : base(AssistantUIConstants.UIModulePath)
        {
        }

        protected void InvokeCompleted()
        {
            Completed?.Invoke();
        }
    }
}
