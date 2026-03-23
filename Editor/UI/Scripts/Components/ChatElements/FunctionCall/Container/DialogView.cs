using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class DialogView : ManagedTemplate
    {
        VisualElement m_ContentContainer;

        public DialogView() : base(AssistantUIConstants.UIModulePath) { }

        public void SetContent(VisualElement content)
        {
            m_ContentContainer.Clear();
            m_ContentContainer.Add(content);
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_ContentContainer = view.Q<VisualElement>("contentContainer");
            InitializeThemeAndStyle(view);
        }

        void InitializeThemeAndStyle(VisualElement root)
        {
            LoadStyle(root, EditorGUIUtility.isProSkin ? AssistantUIConstants.AssistantSharedStyleDark : AssistantUIConstants.AssistantSharedStyleLight);
            LoadStyle(root, AssistantUIConstants.AssistantBaseStyle, true);
        }
    }
}
