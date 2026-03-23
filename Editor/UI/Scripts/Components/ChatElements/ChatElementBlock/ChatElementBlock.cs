using Unity.AI.Assistant.UI.Editor.Scripts.Data.MessageBlocks;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    abstract class ChatElementBlock : ManagedTemplate
    {
        protected ChatElementBlock() : base(AssistantUIConstants.UIModulePath) { }

        public abstract void SetBlockModel(IMessageBlockModel data);

        public virtual void OnConversationCancelled() { }

        protected override void InitializeView(TemplateContainer view)
        {
        }
    }
}
