using System;
using Unity.AI.Animate.Services.Utilities;
using Unity.AI.Generators.Contexts;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Utilities;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Animate.Components
{
    [UxmlElement]
    partial class LoopPreview : VisualElement
    {
        public VisualElement image { get; }
        public VisualElement progress { get; }

        public Action<float> animationTimeChanged;

        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Animate/Components/LoopPreview/LoopPreview.uxml";

        float m_AnimationTime;
        double m_LastEditorTime;
        RenderTexture m_AnimationTexture;
        AnimationClip m_AnimationClip;

        bool m_IsPlaying;
        IVisualElementScheduledItem m_Scheduled;
        RenderTexture m_SkeletonTexture;
        readonly SpinnerManipulator m_SpinnerManipulator;
        readonly Label m_ProgressLabel;

        ~LoopPreview()
        {
            try
            {
                if (m_AnimationTexture)
                    RenderTexture.ReleaseTemporary(m_AnimationTexture);
                m_AnimationTexture = null;
            }
            catch
            { }
        }

        public LoopPreview()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            AddToClassList("loop-preview");

            m_ProgressLabel = this.Q<Label>("label");
            image = this.Q<VisualElement>("border");
            progress = this.Q<VisualElement>("progress");
            RegisterCallback<AttachToPanelEvent>(_ => {
                if (m_SkeletonTexture)
                    progress.style.backgroundImage = Background.FromRenderTexture(m_SkeletonTexture); });
            RegisterCallback<DetachFromPanelEvent>(_ => {
                // Pause animation updates when detached
                if (m_IsPlaying && m_Scheduled != null)
                    m_Scheduled.Pause();
            });
            progress.AddManipulator(m_SpinnerManipulator = new SpinnerManipulator());

            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            this.UseContext<ScreenScaleFactor>(OnScreenScaleFactorChanged, false);

            isPlaying = true;
        }

        public void ShowProgress(float progressValue)
        {
            if (m_ProgressLabel == null || panel == null)
                return;

            m_ProgressLabel.text = $"{progressValue * 100:0} %";
            m_ProgressLabel.style.display = DisplayStyle.Flex;

            var screenScaleFactor = this.GetContext<ScreenScaleFactor>()?.value ?? 1f;
            var previousSkeletonTexture = m_SkeletonTexture;
            var width = float.IsNaN(resolvedStyle.width) ? (int)TextureSizeHint.Carousel : (int)resolvedStyle.width;
            m_SkeletonTexture = SkeletonRenderingUtils.GetCached(progressValue, width, width, screenScaleFactor);
            if (previousSkeletonTexture == m_SkeletonTexture)
                return;

            progress.style.backgroundImage = Background.FromRenderTexture(m_SkeletonTexture);
            MarkDirtyRepaint();
        }

        public void StartSpinner()
        {
            m_SpinnerManipulator.Start();
            if (m_ProgressLabel != null)
                m_ProgressLabel.style.display = DisplayStyle.Flex;
            image.style.opacity = 0.25f;
        }

        public void StopSpinner()
        {
            m_SpinnerManipulator.Stop();
            if (m_ProgressLabel != null)
                m_ProgressLabel.style.display = DisplayStyle.None;
            image.style.opacity = 1.0f;
        }

        void UpdateAnimationTime()
        {
            if (!m_IsPlaying || panel == null)
                return;

            var currentEditorTime = EditorApplication.timeSinceStartup;
            var delta = currentEditorTime - m_LastEditorTime;
            m_LastEditorTime = currentEditorTime;

            var oldTime = m_AnimationTime;
            m_AnimationTime += (float)delta;

            if (m_AnimationClip != null && m_AnimationTime > m_AnimationClip.length)
                m_AnimationTime %= Mathf.Max(m_AnimationClip.length, 0.001f);

            if (!Mathf.Approximately(oldTime, m_AnimationTime))
                animationTimeChanged?.Invoke(m_AnimationTime);

            OnGeometryChanged(null);
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (panel == null)
                return;

            var width = resolvedStyle.width;
            var height = resolvedStyle.height;

            if (width is <= 0 or float.NaN)
                return;

            if (animationClip == null)
                return;

            RenderTexture rt = null;

            var screenScaleFactor = this.GetContext<ScreenScaleFactor>()?.value ?? 1f;

            rt = m_AnimationTexture = animationClip.GetTemporary(m_AnimationTime, (int)(width * screenScaleFactor), (int)(height * screenScaleFactor), m_AnimationTexture);
            image.style.backgroundImage = Background.FromRenderTexture(m_AnimationTexture);
            image.MarkDirtyRepaint();

            if (!rt)
                return;

            image.EnableInClassList("image-scale-to-fit", resolvedStyle.width <= rt.width || resolvedStyle.height <= rt.height);
            image.EnableInClassList("image-scale-initial", resolvedStyle.width > rt.width && resolvedStyle.height > rt.height);
        }

        void OnScreenScaleFactorChanged(ScreenScaleFactor _) => OnGeometryChanged(null);

        [UxmlAttribute]
        public bool isPlaying
        {
            get => m_IsPlaying;
            set
            {
                if (m_IsPlaying == value)
                    return;

                m_IsPlaying = value;

                if (m_IsPlaying)
                {
                    m_LastEditorTime = EditorApplication.timeSinceStartup;
                    if (m_Scheduled == null)
                        m_Scheduled = schedule.Execute(UpdateAnimationTime).Every(20);
                    else if (panel != null)  // Only resume if attached to a panel
                        m_Scheduled.Resume();
                }
                else
                {
                    m_Scheduled?.Pause();
                }
            }
        }

        public AnimationClip animationClip
        {
            get => m_AnimationClip;
            set
            {
                if (m_AnimationClip == value)
                    return;
                m_AnimationClip = value;
                image.style.backgroundImage = null;
                OnGeometryChanged(null);
            }
        }

        public float currentTime
        {
            get => m_AnimationTime;
            set
            {
                if (Mathf.Approximately(m_AnimationTime, value))
                    return;

                m_AnimationTime = value;

                if (m_AnimationClip != null && m_AnimationTime > m_AnimationClip.length)
                    m_AnimationTime %= m_AnimationClip.length;

                animationTimeChanged?.Invoke(m_AnimationTime);

                OnGeometryChanged(null);
            }
        }
    }
}
