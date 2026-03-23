using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Editor.Acp;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class PermissionElement : UserInteractionElement<ToolPermissions.UserAnswer>
    {
        readonly string k_Action;
        readonly string k_Question;
        readonly string k_Code;
        readonly long k_PointCount;
        readonly AcpPermissionOption[] k_Options;
        readonly JObject k_RawInput;

        const string k_PermissionGrantedText = "Permission Granted";
        const string k_PermissionDeniedText = "Permission Denied";
        const string k_PermissionCanceledText = "Canceled";

        VisualElement m_PermissionInteraction;
        VisualElement m_PermissionAnswer;
        VisualElement m_QuestionContainer;
        Label m_QuestionLabel;
        VisualElement m_ButtonContainer;
        Label m_AnswerLabel;
        Label m_AnswerActionLabel;
        Label m_SessionMessageLabel;

        // Track if we have content rendered via a content renderer (should stay visible after answering)
        bool m_HasRenderedContent;

        public PermissionElement() : this("") { }

        public PermissionElement(string action, string question = null, string code = null, long pointCount = 0, AcpPermissionOption[] options = null, JObject rawInput = null)
        {
            k_Action = action;
            k_Question = question;
            k_Code = code;
            k_PointCount = pointCount;
            k_Options = options;
            k_RawInput = rawInput;
        }

        string GetButtonLabel(string kind, string defaultLabel)
        {
            if (k_Options == null) return defaultLabel;
            var option = k_Options.FirstOrDefault(o => o?.Kind == kind);
            return !string.IsNullOrEmpty(option?.Name) ? option.Name : defaultLabel;
        }

        protected override void InitializeView(TemplateContainer view)
        {
            style.flexShrink = 0;

            m_PermissionInteraction = view.Q<VisualElement>("permissionInteraction");
            m_QuestionContainer = view.Q<VisualElement>("questionContainer");
            m_QuestionLabel = view.Q<Label>("questionLabel");
            m_ButtonContainer = view.Q<VisualElement>("buttonContainer");

            var actionLabel = view.Q<Label>("actionLabel");
            actionLabel.text = k_Action;

            if (!string.IsNullOrEmpty(k_Question))
                m_QuestionLabel.text = k_Question;
            else
                m_QuestionLabel.SetDisplay(false);

            var codeContainer = view.Q("codeContainer");
            if (!string.IsNullOrEmpty(k_Code))
            {
                var codePreview = new CodeBlockElement();
                codePreview.Initialize(Context);

                codePreview.SetCode(k_Code);
                codePreview.ShowSaveButton(false);

                codeContainer.Add(codePreview);
            }
            else
            {
                // Try to render special content from rawInput
                var contentRenderer = PermissionContentRendererRegistry.GetRenderer(k_RawInput);
                if (contentRenderer != null)
                {
                    var contentElement = contentRenderer.Render(k_RawInput, Context);
                    if (contentElement != null)
                    {
                        codeContainer.Add(contentElement);
                        m_HasRenderedContent = true;
                    }
                    else
                    {
                        codeContainer.SetDisplay(false);
                    }
                }
                else
                {
                    codeContainer.SetDisplay(false);
                }
            }

            var yesButton = view.Q<Button>("yesButton");
            yesButton.RegisterCallback<ClickEvent>(_ => OnAnswerSelected(ToolPermissions.UserAnswer.AllowOnce));

            var yesAlwaysButton = view.Q<Button>("yesAlwaysButton");
            yesAlwaysButton.RegisterCallback<ClickEvent>(_ => OnAnswerSelected(ToolPermissions.UserAnswer.AllowAlways));

            var noButton = view.Q<Button>("noButton");
            noButton.RegisterCallback<ClickEvent>(_ => OnAnswerSelected(ToolPermissions.UserAnswer.DenyOnce));

            // Set button labels from ACP options if provided, otherwise use UXML defaults
            view.Q<Label>("yesLabel").text = GetButtonLabel(AcpPermissionMapping.AllowOnceKind, "Allow");
            view.Q<Label>("yesAlwaysLabel").text = GetButtonLabel(AcpPermissionMapping.AllowAlwaysKind, "Allow for conversation");
            view.Q<Label>("noLabel").text = GetButtonLabel(AcpPermissionMapping.RejectOnceKind, "Don't allow");

            // Set up point count badge
            var pointCountLabel = view.Q<Label>("pointCountLabel");
            var sparkleBadge = view.Q<VisualElement>("sparkleBadge");

            if (k_PointCount > 0)
            {
                pointCountLabel.text = k_PointCount.ToString();
                yesButton.AddToClassList("has-badge");
                yesAlwaysButton.AddToClassList("has-badge");
            }
            else
            {
                pointCountLabel.SetDisplay(false);
                sparkleBadge.SetDisplay(false);
            }

            // Initially hide the permission answer section
            m_PermissionAnswer = view.Q<VisualElement>("permissionAnswer");
            m_PermissionAnswer.SetDisplay(false);

            // Cache answer display elements
            m_AnswerLabel = m_PermissionAnswer.Q<Label>("answerLabel");
            m_AnswerActionLabel = m_PermissionAnswer.Q<Label>("answerActionLabel");
            m_SessionMessageLabel = m_PermissionAnswer.Q<Label>("sessionMessageLabel");
        }

        void OnAnswerSelected(ToolPermissions.UserAnswer answer)
        {
            ApplyAnsweredState(answer, completeInteraction: true);
        }

        protected override void OnCanceled()
        {
            ApplyCanceledState();
        }

        public void ShowAnsweredState(ToolPermissions.UserAnswer answer)
        {
            ApplyAnsweredState(answer, completeInteraction: false);
        }

        public void ShowCanceledState()
        {
            ApplyCanceledState();
        }

        void ApplyAnsweredState(ToolPermissions.UserAnswer answer, bool completeInteraction)
        {
            // If we have rendered content (e.g., a plan), keep it visible by only hiding
            // the question/button parts. Otherwise, hide the entire interaction container.
            if (m_HasRenderedContent)
            {
                m_QuestionContainer.SetDisplay(false);
                m_QuestionLabel.SetDisplay(false);
                m_ButtonContainer.SetDisplay(false);
            }
            else
            {
                m_PermissionInteraction.SetDisplay(false);
            }

            m_PermissionAnswer.SetDisplay(true);

            bool granted = answer is ToolPermissions.UserAnswer.AllowOnce or ToolPermissions.UserAnswer.AllowAlways;

            m_AnswerLabel.text = granted ? k_PermissionGrantedText : k_PermissionDeniedText;
            m_AnswerActionLabel.text = k_Action;

            // Show session message if "Don't ask again for this session" was selected
            if (answer == ToolPermissions.UserAnswer.AllowAlways)
            {
                m_SessionMessageLabel.enableRichText = true;
                m_SessionMessageLabel.text = $"Assistant won't ask permission to <b>{k_Action}</b> this conversation.";
                m_SessionMessageLabel.SetDisplay(true);
            }
            else
            {
                m_SessionMessageLabel.SetDisplay(false);
            }

            if (completeInteraction)
                CompleteInteraction(answer);
        }

        void ApplyCanceledState()
        {
            // If we have rendered content (e.g., a plan), keep it visible by only hiding
            // the question/button parts. Otherwise, hide the entire interaction container.
            if (m_HasRenderedContent)
            {
                m_QuestionContainer.SetDisplay(false);
                m_QuestionLabel.SetDisplay(false);
                m_ButtonContainer.SetDisplay(false);
            }
            else
            {
                m_PermissionInteraction.SetDisplay(false);
            }

            m_PermissionAnswer.SetDisplay(true);

            m_AnswerLabel.text = k_PermissionCanceledText;
            m_SessionMessageLabel.SetDisplay(false);
        }
    }
}
