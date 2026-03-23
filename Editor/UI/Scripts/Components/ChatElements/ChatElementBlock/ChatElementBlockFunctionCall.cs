using System;
using Unity.AI.Assistant.UI.Editor.Scripts.Data.MessageBlocks;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class ChatElementBlockFunctionCall : ChatElementBlockBase<FunctionCallBlockModel>
    {
        VisualElement m_RootContainer;
        FunctionCallElement m_FunctionCallElement;

        public Guid CallId => BlockModel.Call.CallId;
        public bool IsDone => BlockModel.Call.Result.IsDone;

        public override void OnConversationCancelled() => m_FunctionCallElement?.OnConversationCancelled();

        protected override void InitializeView(TemplateContainer view)
        {
            base.InitializeView(view);

            m_RootContainer = view.Q<VisualElement>("functionCallRoot");
        }

        protected override void OnBlockModelChanged()
        {
            RefreshContent();
        }

        public void PushInteraction(VisualElement interactionElement)
        {
            Add(interactionElement);
        }

        public void PopInteraction(VisualElement interactionElement)
        {
            Remove(interactionElement);
        }

        void RefreshContent()
        {
            var functionCall = BlockModel.Call;

            if (m_FunctionCallElement == null)
            {
                var renderer = FunctionCallRendererFactory.CreateFunctionCallRenderer(functionCall.FunctionId);
                m_FunctionCallElement = new FunctionCallElement(renderer);
                m_FunctionCallElement.Initialize(Context);
                m_RootContainer.Add(m_FunctionCallElement);
            }

            m_FunctionCallElement.UpdateData(functionCall);
        }
    }
}
