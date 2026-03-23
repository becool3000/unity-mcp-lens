using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.WhatsNew.Pages
{
    class WhatsNewContentPoints : WhatsNewContent
    {
        public override string Title => "How Points Work in Unity AI";
        public override string Description => "Unity AI points are free during the beta, but limited to manage resources and performance.";

        protected override void InitializeView(TemplateContainer view)
        {
            base.InitializeView(view);

            RegisterPage(view.Q<VisualElement>("page1"), "Points_1_Introducing_Points");
            RegisterPage(view.Q<VisualElement>("page2"), "Points_2_Assistant_Point_Cost_Shown");
            RegisterPage(view.Q<VisualElement>("page3"), "Points_3_Managing_Points");
        }
    }
}
