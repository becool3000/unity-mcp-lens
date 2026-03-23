using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class CodeEditTabbedContent : ManagedTemplate
    {
        const string k_TabActiveClass = "tab-active";

        Button m_CodeTab;
        Button m_PreviewTab;
        VisualElement m_CodePane;
        VisualElement m_PreviewPane;

        public CodeEditTabbedContent() : base(AssistantUIConstants.UIModulePath) { }

        public void SetContent(VisualElement codeBlock, VisualElement preview)
        {
            m_CodePane.Add(codeBlock);
            m_PreviewPane.Add(preview);
        }

        protected override void InitializeView(TemplateContainer view)
        {
            view.AddToClassList("code-edit-tabs");

            m_CodeTab = view.SetupButton("codeEditCodeTab", OnCodeClicked);
            m_PreviewTab = view.SetupButton("codeEditPreviewTab", OnPreviewClicked);
            m_CodePane = view.Q<VisualElement>("codeEditCodePane");
            m_PreviewPane = view.Q<VisualElement>("codeEditPreviewPane");
        }

        void OnCodeClicked(PointerUpEvent evt)
        {
            m_CodeTab.AddToClassList(k_TabActiveClass);
            m_PreviewTab.RemoveFromClassList(k_TabActiveClass);
            m_CodePane.SetDisplay(true);
            m_PreviewPane.SetDisplay(false);
        }

        void OnPreviewClicked(PointerUpEvent evt)
        {
            m_CodeTab.RemoveFromClassList(k_TabActiveClass);
            m_PreviewTab.AddToClassList(k_TabActiveClass);
            m_CodePane.SetDisplay(false);
            m_PreviewPane.SetDisplay(true);
        }
    }
}
