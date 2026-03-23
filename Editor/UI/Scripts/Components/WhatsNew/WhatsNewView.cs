using System;
using System.Collections.Generic;
using System.IO;
using Unity.AI.Assistant.UI.Editor.Scripts.Components.WhatsNew.Pages;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.WhatsNew
{
    class WhatsNewView : ManagedTemplate
    {
        readonly IList<WhatsNewContent> k_Contents = new List<WhatsNewContent>();

        VisualElement m_LandingPageRoot;
        VisualElement m_ContentRoot;
        VisualElement m_RootPanel;
        ScrollView m_ContentButtonRoot;

        VideoPlayer m_VideoPlayer;
        IVisualElementScheduledItem m_CurrentVideoAnim;
        VisualElement m_CurrentVideoTargetElement;
        bool m_VideoFullMode = false;

        string m_ActiveVideoName;
        VisualElement m_VideoFullView;
        VisualElement m_VideoFullContent;

        WhatsNewWindow m_Window;

        public event Action CloseRequested;
        public event Action RepaintWindow;

        public WhatsNewView()
            : base(AssistantUIConstants.UIModulePath)
        {}

        public void SetWindow(WhatsNewWindow window)
        {
            m_Window = window;
        }

        /// <summary>
        /// Setup the default theme, only used when this is hosted by a separate window (not assistant window)
        /// </summary>
        public void InitializeThemeAndStyle()
        {
            LoadStyle(m_RootPanel,
                EditorGUIUtility.isProSkin
                    ? AssistantUIConstants.AssistantSharedStyleDark
                    : AssistantUIConstants.AssistantSharedStyleLight);
            LoadStyle(m_RootPanel, AssistantUIConstants.AssistantBaseStyle, true);
        }

        protected override void InitializeView(TemplateContainer view)
        {
            view.style.flexGrow = 1;

            m_LandingPageRoot = view.Q<VisualElement>("landingPage");
            m_LandingPageRoot.SetDisplay(true);
            m_RootPanel = view.Q<VisualElement>("rootPanel");
            m_ContentButtonRoot = view.Q<ScrollView>("contentButtonRoot");
            m_ContentRoot = view.Q<VisualElement>("contentRoot");
            m_ContentRoot.SetDisplay(false);

            view.SetupButton("closeButton", _ => CloseRequested?.Invoke());

            m_VideoFullView = view.Q<VisualElement>("videoFullView");
            m_VideoFullView.SetDisplay(false);
            m_VideoFullContent = view.Q<VisualElement>("videoFullViewContent");
            m_VideoFullContent.RegisterCallback<PointerUpEvent>(ToggleVideoFullView);

            RegisterContent<WhatsNewContentAssistant>(m_Window);
            RegisterContent<WhatsNewContentGenerators>(m_Window);
            RegisterContent<WhatsNewContentPoints>(m_Window);

            RefreshContentButtons();

            RestoreLastContent();
        }

        private void RestoreLastContent()
        {
            if (m_Window && !string.IsNullOrEmpty(m_Window.ContentType))
            {
                foreach (var content in k_Contents)
                {
                    if (content.GetType().AssemblyQualifiedName == m_Window.ContentType)
                    {
                        OnActivateContent(content, m_Window.ContentPageIndex);
                        return;
                    }
                }
            }
        }

        void RegisterContent<T>(WhatsNewWindow window)
            where T : WhatsNewContent, new()
        {
            var page = new T();
            page.SetParent(this);
            page.SetState(window);
            page.Initialize(Context);
            page.style.flexGrow = 1;
            k_Contents.Add(page);
        }

        void RefreshContentButtons()
        {
            m_ContentButtonRoot.Clear();
            foreach (var content in k_Contents)
            {
                var button = new WhatsNewContentButton();
                button.Initialize(Context);
                button.Clicked += () => OnActivateContent(content);
                button.SetTargetPage(content);
                m_ContentButtonRoot.Add(button);
            }
        }

        public void ActivateLandingPage()
        {
            m_LandingPageRoot.SetDisplay(true);
            m_ContentRoot.SetDisplay(false);
            m_ContentRoot.Clear();

            if (m_Window)
                m_Window.ContentType = default;
        }

        void OnActivateContent(WhatsNewContent content, int contentPageIndex = 0)
        {
            m_LandingPageRoot.SetDisplay(false);

            m_ContentRoot.Clear();
            m_ContentRoot.SetDisplay(true);
            m_ContentRoot.Add(content);

            content.BrowseTo(contentPageIndex);

            if (m_Window)
                m_Window.ContentType = content.GetType().AssemblyQualifiedName;
        }

        public void PlayVideoInto(VisualElement targetElement, string videoName)
        {
            m_ActiveVideoName = videoName;
            m_CurrentVideoTargetElement = targetElement;
            PlayVideo(targetElement);
        }

        void PlayVideo(VisualElement targetElement)
        {
            StopVideo(false);

            if (targetElement == null || string.IsNullOrEmpty(m_ActiveVideoName))
            {
                return;
            }

            if (m_VideoPlayer != null)
            {
                m_VideoPlayer.Stop();
            }

            if (m_VideoPlayer == null)
            {
                var videoPlayerGo = new GameObject("AIWhatsNewVideoPlayer") { hideFlags = HideFlags.HideAndDontSave };
                m_VideoPlayer = videoPlayerGo.AddComponent<VideoPlayer>();
                m_VideoPlayer.renderMode = VideoRenderMode.APIOnly;
                m_VideoPlayer.isLooping = true;
                m_VideoPlayer.playOnAwake = false;
                m_VideoPlayer.audioOutputMode = VideoAudioOutputMode.None;
                m_VideoPlayer.skipOnDrop = false;
            }

            var videoFile = Path.Combine(AssistantUIConstants.VideoPath, m_ActiveVideoName + AssistantUIConstants.VideoExtension);

            var clipPath = videoFile;
            var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(clipPath);

            if (clip == null)
            {
                InternalLog.LogError($"Cannot find video clip at path: {clipPath}");
                return;
            }

            targetElement.RegisterCallback<PointerUpEvent>(ToggleVideoFullView);

            m_VideoPlayer.prepareCompleted += PrepareCompleted;
            m_VideoPlayer.clip = clip;
            m_VideoPlayer.Prepare();

            m_VideoPlayer.Play();

            m_CurrentVideoAnim = schedule.Execute(() =>
            {
                if (m_VideoPlayer == null)
                {
                    return;
                }

                m_VideoPlayer.texture?.IncrementUpdateCount();

                RepaintWindow?.Invoke();
            }).Every(33);

            void PrepareCompleted(VideoPlayer source)
            {
                var value = targetElement.style.backgroundImage.value;
                value.renderTexture = (RenderTexture) m_VideoPlayer.texture;
                targetElement.style.backgroundImage = value;

                m_VideoPlayer.Play();

                m_VideoPlayer.prepareCompleted -= PrepareCompleted;
            }
        }

        public void StopVideo(bool release = true)
        {
            m_CurrentVideoAnim?.Pause();
            m_CurrentVideoAnim = null;

            m_CurrentVideoTargetElement?.UnregisterCallback<PointerUpEvent>(ToggleVideoFullView);

            if (release && m_VideoPlayer != null)
            {
                UnityEngine.Object.DestroyImmediate(m_VideoPlayer.gameObject);
                m_VideoPlayer = null;
            }
        }

        void ToggleVideoFullView(PointerUpEvent evt)
        {
            m_VideoFullMode = !m_VideoFullMode;

            m_ContentRoot.SetDisplay(!m_VideoFullMode);
            m_VideoFullView.SetDisplay(m_VideoFullMode);
            PlayVideo(m_VideoFullMode ? m_VideoFullContent : m_CurrentVideoTargetElement);
        }
    }
}
