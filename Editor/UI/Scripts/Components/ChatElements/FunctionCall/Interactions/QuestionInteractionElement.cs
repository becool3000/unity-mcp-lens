using System;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class QuestionInteractionElement : UserInteractionElement<string>
    {
        readonly string k_Question;
        readonly string[] k_Buttons;

        public QuestionInteractionElement() : this("", Array.Empty<string>()) { }

        public QuestionInteractionElement(string question, string[] buttons)
        {
            k_Question  = question;
            k_Buttons = buttons;
        }

        protected override void InitializeView(TemplateContainer view)
        {
            var label = new Label(k_Question);
            Add(label);

            if (k_Buttons != null)
            {
                foreach (var buttonText in k_Buttons)
                {
                    var button = new Button { text = buttonText };
                    button.RegisterCallback<ClickEvent>(_ => CompleteInteraction(buttonText));
                    Add(button);
                }
            }
        }
    }
}
