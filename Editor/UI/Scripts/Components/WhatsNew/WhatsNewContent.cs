using System.Collections.Generic;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.WhatsNew
{
    abstract class WhatsNewContent : ManagedTemplate
    {
        const string k_BackToStartText = "Back to start";

        readonly IList<VisualElement> k_Pages = new List<VisualElement>();
        readonly IDictionary<VisualElement, string> k_PageVideos = new Dictionary<VisualElement, string>();

        Button m_BackButton;
        Button m_NextButton;
        Label m_PageControlLabel;

        int m_CurrentPageIndex;

        WhatsNewView m_ParentView;
        WhatsNewWindow m_Window;

        protected WhatsNewContent()
            : base(AssistantUIConstants.UIModulePath)
        {
        }

        public abstract string Title { get; }
        public abstract string Description { get; }

        private int CurrentPageIndex
        {
            get => m_CurrentPageIndex;
            set
            {
                m_CurrentPageIndex = value;
                m_Window.ContentPageIndex = value;
            }
        }

        public void SetParent(WhatsNewView newParent)
        {
            m_ParentView = newParent;
        }

        public void SetState(WhatsNewWindow whatsNewState)
        {
            m_Window = whatsNewState;
        }

        public void BrowseTo(int page)
        {
            CurrentPageIndex = Mathf.Clamp(page, 0, k_Pages.Count - 1);
            RefreshPageDisplay();
        }

        protected override void InitializeView(TemplateContainer view)
        {
            view.style.flexGrow = 1;

            m_BackButton = view.SetupButton("pageControlBack", OnBackPressed);
            m_NextButton = view.SetupButton("pageControlNext", OnNextPressed);

            m_PageControlLabel = view.Q<Label>("pageControlLabel");
        }

        protected void RegisterPage(VisualElement page, string videoFile = null)
        {
            k_Pages.Add(page);

            if (!string.IsNullOrEmpty(videoFile))
            {
                k_PageVideos.Add(page, videoFile);
            }
        }

        void ExitContent()
        {
            m_ParentView.ActivateLandingPage();
        }

        void OnNextPressed(PointerUpEvent evt)
        {
            if (CurrentPageIndex < k_Pages.Count - 1)
            {
                CurrentPageIndex++;
            }
            else
            {
                ExitContent();
                return;
            }

            RefreshPageDisplay();
        }

        void OnBackPressed(PointerUpEvent evt)
        {
            if (CurrentPageIndex > 0)
            {
                CurrentPageIndex--;
            }
            else
            {
                ExitContent();
                return;
            }

            RefreshPageDisplay();
        }

        void RefreshPageDisplay()
        {
            PlayVideo();

            for(var i = 0; i < k_Pages.Count; i++)
            {
                k_Pages[i].SetDisplay(i == CurrentPageIndex);
            }

            m_NextButton.text = CurrentPageIndex == k_Pages.Count - 1 ? k_BackToStartText : "Next";
            m_BackButton.text = CurrentPageIndex == 0 ? k_BackToStartText : "Previous";
            m_PageControlLabel.text = $"{CurrentPageIndex + 1} / {k_Pages.Count}";
        }

        void PlayVideo()
        {
            if (m_ParentView == null)
            {
                return;
            }

            var targetPage = k_Pages[CurrentPageIndex];

            if (!k_PageVideos.TryGetValue(targetPage, out var videoFileName))
            {
                m_ParentView.StopVideo();
                return;
            }

            var targetElement = targetPage.Q<VisualElement>($"page{CurrentPageIndex + 1}Video");
            if (targetElement == null)
            {
                m_ParentView.StopVideo();
                return;
            }

            var videoElement = SetupTargetElement(targetElement);

            m_ParentView.PlayVideoInto(videoElement, videoFileName);
        }

        VisualElement SetupTargetElement(VisualElement container)
        {
            var existingMainElement = container.Q<VisualElement>("mainVideoElement");
            if (existingMainElement != null)
            {
                return existingMainElement;
            }

            // Element to play the video in
            var videoElement = new VisualElement();
            videoElement.name = "mainVideoElement";
            videoElement.AddToClassList("mui-whats-new-video-element");

            // Create an overlay icon element to enlarge
            var iconElement = new VisualElement();
            iconElement.AddToClassList("mui-whats-new-video-icon");
            iconElement.AddToClassList("mui-icon-enlarge");
            iconElement.pickingMode = PickingMode.Ignore;

            container.Add(videoElement);
            container.Add(iconElement);

            return videoElement;
        }
    }
}
