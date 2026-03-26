using System;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Accounts.Services;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    /// <summary>
    /// Overlay shown when a third-party AI Gateway provider is selected and the user
    /// has not yet accepted the legal disclaimer. Manages its own event subscriptions
    /// via AttachToPanelEvent / DetachFromPanelEvent.
    /// </summary>
    class GatewayDisclaimerView : VisualElement
    {
        readonly Button m_AcceptButton;
        readonly Label m_SignInLabel;

        /// <summary>
        /// Fired whenever the disclaimer active state changes.
        /// True  = disclaimer is showing (caller should disable chat input).
        /// False = disclaimer is hidden  (caller should re-enable chat input).
        /// </summary>
        public event Action<bool> IsActiveChanged;

        public GatewayDisclaimerView()
        {
            AddToClassList("mui-gateway-disclaimer-root");

            var scroll = new ScrollView();
            scroll.AddToClassList("mui-gateway-disclaimer-scroll");
            scroll.Add(MakeLabel("Important: Using Third-Party AI Agents", "mui-gateway-disclaimer-heading"));
            scroll.Add(MakeLabel("By selecting a third-party AI agent, you acknowledge and agree to the following:", "mui-gateway-disclaimer-intro"));
            scroll.Add(MakeRichLabel("<b>Direct Third-Party Relationship:</b> You are using an AI agent that is not owned, operated, or modified by Unity. Your use of the agent is governed solely by your existing license and service agreements with the third-party provider, and you assume all risks related to security vulnerabilities, intellectual property infringement, and the accuracy of the content generated.", "mui-gateway-disclaimer-bullet"));
            scroll.Add(MakeRichLabel("<b>Data Training and Privacy:</b> The third-party provider's ability to train their models on your project data or inputs is determined strictly by your own contract with them. Please review your provider's terms to ensure they meet your privacy requirements. You are responsible for ensuring the security and integrity of the data sent to and received from the third-party agent.", "mui-gateway-disclaimer-bullet"));
            scroll.Add(MakeRichLabel("<b>Unity Logging:</b> Unity logs usage metadata (such as session timestamps and connection status) for operational purposes. Unity does not log or store the content of your prompts or the third-party provider's outputs.", "mui-gateway-disclaimer-bullet"));
            scroll.Add(MakeLabel("By proceeding, you authorize Unity to facilitate the connection to your chosen third-party provider.", "mui-gateway-disclaimer-footer"));
            Add(scroll);

            m_AcceptButton = new Button(OnAcceptClicked) { text = "Accept" };
            m_AcceptButton.AddToClassList("mui-gateway-disclaimer-accept-button");
            Add(m_AcceptButton);

            m_SignInLabel = new Label("Please sign in to a user account to use the AI Gateway");
            m_SignInLabel.AddToClassList("mui-gateway-disclaimer-signin-label");
            Add(m_SignInLabel);

            RegisterCallback<AttachToPanelEvent>(_ => OnAttach());
            RegisterCallback<DetachFromPanelEvent>(_ => OnDetach());
        }

        /// <summary>
        /// Forces an immediate re-evaluation of the disclaimer state.
        /// Call this after restoring UI state to ensure correct initial display.
        /// </summary>
        public void Refresh() => UpdateState();

        void OnAttach()
        {
            ProviderStateObserver.OnProviderChanged += OnProviderChanged;
            Account.session.OnChange += OnSessionChanged;
            AssistantEditorPreferences.AiGatewayDisclaimerAcceptedChanged += UpdateState;
            UpdateState();
        }

        void OnDetach()
        {
            ProviderStateObserver.OnProviderChanged -= OnProviderChanged;
            Account.session.OnChange -= OnSessionChanged;
            AssistantEditorPreferences.AiGatewayDisclaimerAcceptedChanged -= UpdateState;
        }

        void OnProviderChanged(string _) => UpdateState();

        void OnSessionChanged() => EditorTask.delayCall += UpdateState;

        void OnAcceptClicked()
        {
            AssistantEditorPreferences.SetAiGatewayDisclaimerAccepted(true);
            // UpdateState is triggered via AiGatewayDisclaimerAcceptedChanged
        }

        void UpdateState()
        {
            var needsDisclaimer = !ProviderStateObserver.IsUnityProvider
                                  && !AssistantEditorPreferences.GetAiGatewayDisclaimerAccepted();

            this.SetDisplay(needsDisclaimer);

            if (needsDisclaimer)
            {
                var hasUserId = !string.IsNullOrEmpty(CloudProjectSettings.userId);
                m_AcceptButton.SetDisplay(hasUserId);
                m_SignInLabel.SetDisplay(!hasUserId);
            }

            IsActiveChanged?.Invoke(needsDisclaimer);
        }

        static Label MakeLabel(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            return label;
        }

        static Label MakeRichLabel(string text, string className)
        {
            var label = new Label(text);
            label.enableRichText = true;
            label.AddToClassList(className);
            return label;
        }
    }
}
