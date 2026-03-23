using Unity.AI.Assistant.UI.Editor.Scripts.Components;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts
{
    [UxmlElement]
    partial class AskAssistantButton : ManagedTemplate
    {
        public event System.Action OnClicked;

        public AskAssistantButton() : base(AssistantUIConstants.UIModulePath)
        {
        }

        protected override void InitializeView(TemplateContainer view)
        {
            var button = view.Q<Button>("askAssistantButton");
            button.clicked += OnClicked;
        }
    }
}
