using System.Collections;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    internal class ChatElementList : ManagedTemplate
    {
        Label m_leftBulletElement;
        VisualElement m_rightContainerElement;

        private const int k_IndentationPerLevel = 20;
        private const int k_IndentationOffset = 12;

        public ChatElementList()
            : base(AssistantUIConstants.UIModulePath)
        {
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_leftBulletElement = view.Q<Label>("leftElement");
            m_rightContainerElement = view.Q<VisualElement>("rightElement");
        }

        public void SetBulletSymbol(string bullet)
        {
            m_leftBulletElement.text = bullet;
        }

        public void AddRightElement(VisualElement element)
        {
            m_rightContainerElement.Add(element);
        }

        public IEnumerable NestedElements()
        {
            return m_rightContainerElement.Children();
        }

        public void SetIndentation(int depth)
        {
            style.marginLeft = k_IndentationOffset + (depth * k_IndentationPerLevel - k_IndentationPerLevel);
        }
    }
}
