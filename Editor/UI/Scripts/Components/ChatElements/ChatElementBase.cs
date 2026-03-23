using Unity.AI.Assistant.UI.Editor.Scripts.Data;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    abstract class ChatElementBase : ManagedTemplate
    {
        protected ChatElementBase() : base(AssistantUIConstants.UIModulePath) { }

        public abstract void SetData(MessageModel message);
    }
}
