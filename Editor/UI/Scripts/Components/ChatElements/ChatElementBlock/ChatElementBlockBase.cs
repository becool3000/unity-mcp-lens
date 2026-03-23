using Unity.AI.Assistant.UI.Editor.Scripts.Data.MessageBlocks;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    abstract class ChatElementBlockBase<T> : ChatElementBlock where T : IMessageBlockModel
    {
        protected T BlockModel { get; private set; }

        public override void SetBlockModel(IMessageBlockModel data)
        {
            BlockModel = (T)data;
            OnBlockModelChanged();
        }

        protected virtual void OnBlockModelChanged() { }
    }
}
