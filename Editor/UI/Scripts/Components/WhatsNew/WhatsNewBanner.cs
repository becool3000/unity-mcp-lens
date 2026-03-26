using System.Collections.Generic;
using System.Linq;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.WhatsNew
{
    class WhatsNewBanner : ManagedTemplate
    {
        internal const string k_LearnMoreUrl = "https://discussions.unity.com/t/release-notes-2-3-0-pre-1/1713487";

        static readonly string[] k_Bullets =
        {
            "Dedicated 2D scene capture (top-down orthographic); 3D multi-angle capture can now focus on specific objects",
            "Assistant can query the project dependency graph without loading assets",
            "Assistant can fetch and read content from the web",
            "Generator feedback (sprites, textures) now accepts written comments alongside thumbs up/down",
            "MCP: faster connections and a new batch mode auto-approve toggle",
            "Bug fixes across the chat window, sprite generation, generator model selection, editor focus handling, and MCP stability",
        };

        static readonly string k_CollapsedSummary = string.Join(", ", k_Bullets.Take(3).Select(MessageUtils.StripRichTextTags));
        static readonly string k_DismissedPrefKey = "muse.whats_new_banner.dismissed." + string.Concat(k_Bullets).GetHashCode();

        VisualElement m_CollapsedRow;
        Label m_CollapsedLabel;
        VisualElement m_ExpandedContent;
        VisualElement m_HeaderRow;
        Label m_HeaderLabel;
        Button m_LearnMoreButton;
        ScrollView m_BulletList;
        VisualElement m_BulletsFade;

        bool m_IsExpanded;
        string m_LearnMoreUrl;
        Texture2D m_FadeTexture;

        public WhatsNewBanner()
            : base(AssistantUIConstants.UIModulePath)
        {
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_CollapsedRow = view.Q<VisualElement>("collapsedRow");
            m_CollapsedLabel = view.Q<Label>("collapsedLabel");
            m_ExpandedContent = view.Q<VisualElement>("expandedContent");
            m_HeaderRow = view.Q<VisualElement>("headerRow");
            m_HeaderLabel = view.Q<Label>("headerLabel");
            m_BulletList = view.Q<ScrollView>("bulletList");
            m_BulletsFade = view.Q<VisualElement>("bulletsFade");

            m_CollapsedRow.RegisterCallback<PointerUpEvent>(_ => Toggle());
            m_HeaderRow.RegisterCallback<PointerUpEvent>(_ => Toggle());
            m_LearnMoreButton = view.SetupButton("learnMoreButton", evt => evt.StopPropagation());
            m_LearnMoreButton.clicked += () =>
            {
                if (!string.IsNullOrEmpty(m_LearnMoreUrl))
                    Application.OpenURL(m_LearnMoreUrl);
            };

            var closeCollapsed = view.Q<VisualElement>("closeButtonCollapsed");
            closeCollapsed.RegisterCallback<PointerUpEvent>(evt => { evt.StopPropagation(); Dismiss(); });
            var closeExpanded = view.Q<VisualElement>("closeButtonExpanded");
            closeExpanded.RegisterCallback<PointerUpEvent>(evt => { evt.StopPropagation(); Dismiss(); });

            RegisterAttachEvents(OnAttachToPanel, OnDetachFromPanel);

            CreateFadeGradient();

            m_IsExpanded = false;

            Show();
            var version = CredentialsContext.GetPackageVersion();
            SetContent(version, k_Bullets, k_LearnMoreUrl);

            RefreshState();
        }

        void SetContent(string version, IReadOnlyList<string> bullets, string learnMoreUrl)
        {
            m_LearnMoreUrl = learnMoreUrl;

            if (bullets.Count == 0)
            {
                Hide();
                return;
            }

            if (EditorPrefs.GetBool(k_DismissedPrefKey, false))
            {
                Hide();
                return;
            }

            Show();

            m_CollapsedLabel.enableRichText = true;
            m_CollapsedLabel.text = $"<b>New in {version}:</b> {k_CollapsedSummary}";
            m_HeaderLabel.text = $"New in {version}";

            m_BulletList.Clear();
            foreach (var bullet in bullets)
            {
                var label = new Label($"\u2022 {bullet}");
                label.AddToClassList("mui-whats-new-banner-bullet");
                label.enableRichText = true;
                m_BulletList.Add(label);
            }
        }

        void Dismiss()
        {
            EditorPrefs.SetBool(k_DismissedPrefKey, true);
            Hide();
        }

        public void Collapse()
        {
            m_IsExpanded = false;
            RefreshState();
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            m_BulletList.verticalScroller.valueChanged += _ => UpdateFadeVisibility();
            m_BulletList.RegisterCallback<GeometryChangedEvent>(_ => UpdateFadeVisibility());
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            if (m_FadeTexture != null)
                Object.DestroyImmediate(m_FadeTexture);
        }

        void Toggle()
        {
            m_IsExpanded = !m_IsExpanded;
            RefreshState();
        }

        void RefreshState()
        {
            m_CollapsedRow.SetDisplay(!m_IsExpanded);
            m_ExpandedContent.SetDisplay(m_IsExpanded);
            UpdateFadeVisibility();
        }

        void CreateFadeGradient()
        {
            const int height = 32;
            m_FadeTexture = new Texture2D(1, height, TextureFormat.RGBA32, false);
            m_FadeTexture.hideFlags = HideFlags.HideAndDontSave;
            m_FadeTexture.filterMode = FilterMode.Bilinear;

            // Match var(--muse-chat-backgrounds-100) for each theme
            var bg = EditorGUIUtility.isProSkin
                ? new Color(0x28 / 255f, 0x28 / 255f, 0x28 / 255f) // dark: #282828
                : new Color(0xC0 / 255f, 0xC0 / 255f, 0xC0 / 255f); // light: #c0c0c0
            var bgOpaque = new Color(bg.r, bg.g, bg.b, 1f);
            var bgTransparent = new Color(bg.r, bg.g, bg.b, 0f);
            var pixels = new Color[height];

            // pixels[0] = bottom of texture = bottom of element → opaque (hides clipped text)
            // pixels[height-1] = top of texture = top of element → transparent
            for (var i = 0; i < height; i++)
            {
                var t = (float)i / (height - 1); // 0 at bottom, 1 at top
                pixels[i] = Color.Lerp(bgOpaque, bgTransparent, t);
            }

            m_FadeTexture.SetPixels(pixels);
            m_FadeTexture.Apply();
            m_BulletsFade.style.backgroundImage = new StyleBackground(m_FadeTexture);
        }

        void UpdateFadeVisibility()
        {
            var scroller = m_BulletList.verticalScroller;
            var atBottom = scroller.highValue <= 0 || scroller.value >= scroller.highValue - 1f;
            m_BulletsFade.SetDisplay(!atBottom);
        }

    }
}
