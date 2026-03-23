using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.History
{
    class HistoryPanelHeaderEntry : ManagedTemplate
    {
        VisualElement m_HeaderRoot;
        Label m_HeaderText;

        public HistoryPanelHeaderEntry()
            : base(AssistantUIConstants.UIModulePath)
        {
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_HeaderRoot = view.Q<VisualElement>("historyPanelHeaderRoot");
            m_HeaderText = view.Q<Label>("historyPanelHeaderText");
        }

        public void SetText(string text)
        {
            if (m_HeaderText.text == text)
            {
                // No change
                return;
            }

            m_HeaderText.text = text;
        }
    }
}
