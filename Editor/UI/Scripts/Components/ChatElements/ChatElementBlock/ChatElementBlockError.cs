using Unity.AI.Assistant.UI.Editor.Scripts.Data.MessageBlocks;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class ChatElementBlockError : ChatElementBlockBase<ErrorBlockModel>
    {
        VisualElement m_ErrorTitle;
        Label m_ErrorText;

        protected override void InitializeView(TemplateContainer view)
        {
            base.InitializeView(view);

            m_ErrorTitle = view.Q("errorTitle");
            m_ErrorText = view.Q<Label>("errorText");
            m_ErrorText.selection.isSelectable = true;
        }

        protected override void OnBlockModelChanged()
        {
            RefreshContent();
        }

        void RefreshContent()
        {
            m_ErrorText.text = BlockModel.Error;
        }
    }
}
