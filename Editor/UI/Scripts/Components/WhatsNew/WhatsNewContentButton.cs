using System;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.WhatsNew
{
    class WhatsNewContentButton : ManagedTemplate
    {
        Label m_Header;
        Label m_Description;

        public WhatsNewContentButton()
            : base(AssistantUIConstants.UIModulePath)
        {
        }

        public event Action Clicked;

        protected override void InitializeView(TemplateContainer view)
        {
            m_Header = view.Q<Label>("whatsNewPageHeader");
            m_Description = view.Q<Label>("whatsNewPageDescription");

            RegisterCallback<PointerUpEvent>(_ => Clicked?.Invoke());
        }

        public void SetTargetPage(WhatsNewContent content)
        {
            m_Header.text = content.Title;
            m_Description.text = content.Description;
        }
    }
}
