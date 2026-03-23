using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.WhatsNew.Pages
{
    class WhatsNewContentAssistant : WhatsNewContent
    {
        protected override void InitializeView(TemplateContainer view)
        {
            base.InitializeView(view);

            view.SetupButton("openAssistantButton", OnOpenAssistant);

            RegisterPage(view.Q<VisualElement>("page1"), "Assistant_1_AskAgentFlipper");
            RegisterPage(view.Q<VisualElement>("page2"), "Assistant_2_Orchestration");
            RegisterPage(view.Q<VisualElement>("page3"), "Assistant_3_Checkpoints");
            RegisterPage(view.Q<VisualElement>("page4"), "Assistant_4_Scene_Generation");
            RegisterPage(view.Q<VisualElement>("page5"), "Assistant_5_MCP_Client");
            RegisterPage(view.Q<VisualElement>("page6"), "Assistant_6_CodeDiff");
            void OnOpenAssistant(PointerUpEvent evt)
            {
                AssistantWindow.ShowWindow();
            }
        }

        public override string Title => "Unity AI Assistant";
        public override string Description => "Free up focus for creative work by automating repetitive tasks.";
    }
}
