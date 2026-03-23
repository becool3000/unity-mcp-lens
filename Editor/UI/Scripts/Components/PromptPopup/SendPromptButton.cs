using System;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.PromptPopup
{
    [UxmlElement]
    partial class SendPromptButton : ManagedTemplate
    {
        public event Action OnClick;

        const string k_SubmitImage = "arrow-up";
        const string k_ChatActionEnabledClass = "mui-submit-enabled";

        Button m_Button;

        public SendPromptButton() : base(AssistantUIConstants.UIModulePath)
        {
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_Button = view.SetupButton("actionButton", OnClicked);
            view.SetupImage("actionButtonImage", k_SubmitImage);
            SetButtonEnabled(false);
        }

        void OnClicked(PointerUpEvent evt) => OnClick?.Invoke();

        public void SetButtonEnabled(bool enabled)
        {
            m_Button.EnableInClassList(k_ChatActionEnabledClass, enabled);
            m_Button.SetEnabled(enabled);
        }
    }
}
