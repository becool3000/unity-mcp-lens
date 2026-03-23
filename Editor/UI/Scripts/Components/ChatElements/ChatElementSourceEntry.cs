using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Editor.Analytics;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class ChatElementSourceEntry : ManagedTemplate
    {
        Button m_SourceLink;
        Label m_SourceLinkHint;
        Label m_SourceLinkText;
        Label m_SourceLinkNumber;
        Label m_SourceLinkTextElement;

        /// <summary>
        /// Create a new shared chat element
        /// </summary>
        public ChatElementSourceEntry()
            : base(AssistantUIConstants.UIModulePath)
        {
        }

        /// <summary>
        /// Set the data for this source element
        /// </summary>
        /// <param name="index">the index of the source</param>
        /// <param name="sourceBlock">the source block defining the URL and title</param>
        public void SetData(int index, SourceBlock sourceBlock)
        {
            Index = index;
            SourceBlock = sourceBlock;
            RefreshDisplay();
        }

        public int Index { get; private set; }

        public SourceBlock SourceBlock { get; private set; }

        protected override void InitializeView(TemplateContainer view)
        {
            m_SourceLink = view.SetupButton("sourceLink", OnSourceClicked);

            m_SourceLinkNumber = view.Q<Label>("sourceLinkNumber");
            m_SourceLinkNumber.text = m_SourceLinkNumber.ToString();

            m_SourceLinkText = view.Q<Label>("sourceLinkLabel");

            m_SourceLink.RegisterCallback<GeometryChangedEvent>(_ => RefreshDisplay());

            m_SourceLinkHint = view.Q<Label>("sourceLinkHint");
        }

        void OnSourceClicked(PointerUpEvent evt)
        {
            Application.OpenURL(SourceBlock.source);

            AIAssistantAnalytics.ReportUITriggerLocalOpenReferenceUrlEvent(SourceBlock.source);
        }

        void RefreshDisplay()
        {
            m_SourceLinkNumber.text = $"[{Index + 1}]";

            m_SourceLinkText.text = SourceBlock.reason;
            m_SourceLink.tooltip = SourceBlock.source;

            m_SourceLinkHint.text = SourceBlock.source;
        }
    }
}
