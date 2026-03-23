using System.Collections;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class ChatElementQuote : ManagedTemplate
    {
        VisualElement m_rightElement;

        public ChatElementQuote()
            : base(AssistantUIConstants.UIModulePath)
        {
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_rightElement = view.Q<VisualElement>("rightElement");
        }

        public void AddElement(VisualElement element)
        {
            m_rightElement.Add(element);
        }

        public IEnumerable NestedElements()
        {
            return m_rightElement.Children();
        }
    }
}
