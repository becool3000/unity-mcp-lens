using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    class AssistantExpandedPanel : ManagedTemplate
    {
        ScrollView m_Content;

        public AssistantExpandedPanel() : base(AssistantUIConstants.UIModulePath)
        {
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_Content = view.Q<ScrollView>("expandedPanelContent");
        }

        internal bool IsVisible => resolvedStyle.display != DisplayStyle.None;

        internal void ShowPanel(VisualElement element)
        {
            m_Content.Clear();
            m_Content.Add(element);
            this.SetDisplay(true);
        }

        internal void HidePanel()
        {
            m_Content.Clear();
            this.SetDisplay(false);
        }
    }
}