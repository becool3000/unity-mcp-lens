using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.AI.Sound.Services.Stores.Actions;
using Unity.AI.Sound.Services.Stores.Actions.Payloads;
using Unity.AI.Sound.Services.Stores.States;
using Unity.AI.Sound.Services.Stores.Selectors;
using Unity.AI.Sound.Services.Utilities;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Generators.Contexts;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Sound.Components
{
    [UxmlElement]
    partial class GenerationTile : VisualElement
    {
        public VisualElement progress { get; }

        public AudioClipResult audioClipResult { get; private set; }

        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Sound/Components/GenerationSelector/GenerationTile.uxml";

        readonly DragExternalFileManipulator m_DragManipulator;
        readonly SpinnerManipulator m_SpinnerManipulator;
        readonly ContextualMenuManipulator m_ContextualMenu;
        readonly ContextualMenuManipulator m_FailedContextualMenu;

        readonly VisualElement m_Image;
        RenderTexture m_WaveformTexture;
        RenderTexture m_CompositeTexture;
        readonly Button m_PlayButton;
        readonly PlayManipulator m_PlayManipulator;
        float m_CurrentTime;
        RenderTexture m_SkeletonTexture;
        readonly Label m_Label;
        GenerationMetadata m_Metadata;

        public GenerationTile()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            m_Label = this.Q<Label>("label");
            m_Image = this.Q<VisualElement>("image");
            m_PlayButton = this.Q<Button>(className: "play-button");
            RegisterCallback<AttachToPanelEvent>(_ => {
                if (m_SkeletonTexture)
                    progress.style.backgroundImage = Background.FromRenderTexture(m_SkeletonTexture); });
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            RegisterCallback<MouseEnterEvent>(_ => m_PlayButton.EnableInClassList("hide", audioClipResult is AudioClipSkeleton || audioClipResult.IsFailed()));
            RegisterCallback<MouseLeaveEvent>(_ => m_PlayButton.EnableInClassList("hide", true));
            RegisterCallback<MouseOverEvent>(_ => UpdateTooltip());
            m_PlayButton.clickable = m_PlayManipulator = new PlayManipulator(() => null,
                () => AudioClipCache.TryGetAudioClip(audioClipResult.uri, out var audioClip) ? audioClip : null, () => false)
            {
                timeUpdate = time => {
                    m_CurrentTime = time;
                    OnGeometryChanged(null);
                }
            };

            m_ContextualMenu = new ContextualMenuManipulator(BuildContextualMenu);
            m_FailedContextualMenu = new ContextualMenuManipulator(BuildFailedContextualMenu);
            m_DragManipulator = new DragExternalFileManipulator { newFileName = AssetUtils.defaultNewAssetName };

            progress = this.Q<VisualElement>("progress");
            progress.AddManipulator(m_SpinnerManipulator = new SpinnerManipulator());

            this.AddManipulator(new DelayedCleanupManipulator(() =>
            {
                m_PlayManipulator?.Cancel();
                if (m_WaveformTexture)
                {
                    RenderTexture.ReleaseTemporary(m_WaveformTexture);
                    m_WaveformTexture = null;
                }
                if (m_CompositeTexture)
                {
                    RenderTexture.ReleaseTemporary(m_CompositeTexture);
                    m_CompositeTexture = null;
                }
            }));
        }

        public void SetGenerationProgress(IEnumerable<GenerationProgressData> data)
        {
            if (audioClipResult is not AudioClipSkeleton)
                return;

            var progressReport = data.FirstOrDefault(d => d.taskID == ((AudioClipSkeleton)audioClipResult).taskID);
            if (progressReport == null)
                return;

            m_SpinnerManipulator.Start();

            m_Label.text = $"{progressReport.progress * 100:0} %";

            var screenScaleFactor = this.GetContext<ScreenScaleFactor>()?.value ?? 1f;
            var previousSkeletonTexture = m_SkeletonTexture;
            var width = float.IsNaN(resolvedStyle.width) ? (int)TextureSizeHint.Carousel : (int)resolvedStyle.width;
            m_SkeletonTexture = SkeletonRenderingUtils.GetCached(progressReport.progress, width, width, screenScaleFactor);
            if (previousSkeletonTexture == m_SkeletonTexture)
                return;

            progress.style.backgroundImage = Background.FromRenderTexture(m_SkeletonTexture);
            MarkDirtyRepaint();
        }

        public void SetSelectedGeneration(AudioClipResult result) => this.SetSelected(audioClipResult != null && audioClipResult.Equals(result));

        void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            evt.menu.MenuItems().Clear();
            if (audioClipResult is AudioClipSkeleton)
                return;

            evt.menu.AppendAction("Select", _ =>
            {
                var asset = this.GetAsset();
                if (asset == null || audioClipResult == null)
                    return;
                this.GetStoreApi().Dispatch(GenerationResultsActions.selectGeneration, new(asset, audioClipResult, true, true));
            }, DropdownMenuAction.AlwaysEnabled);
            /*evt.menu.AppendAction("Play", _ =>
            {
                if (m_AudioClipResult == null)
                    return;
                m_AudioClipResult.Play();
            }, DropdownMenuAction.AlwaysEnabled);*/
            evt.menu.AppendAction("Promote to new asset", _ =>
            {
                var asset = this.GetAsset();
                this.GetStoreApi().Dispatch(SessionActions.promoteFocusedGeneration, new (asset, audioClipResult));
            }, DropdownMenuAction.AlwaysEnabled);
            evt.menu.AppendAction(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Show in Explorer" :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Reveal in Finder" : "Show in File Manager", _ =>
            {
                EditorUtility.RevealInFinder(File.Exists(audioClipResult.uri.GetLocalPath())
                    ? audioClipResult.uri.GetLocalPath()
                    : Path.GetDirectoryName(audioClipResult.uri.GetLocalPath()));
            }, DropdownMenuAction.AlwaysEnabled);

            var showGenerationDataStatus = DropdownMenuAction.Status.Normal;
            if (this.GetAsset() == null || audioClipResult == null || !File.Exists(audioClipResult.uri.GetLocalPath()))
            {
                showGenerationDataStatus = DropdownMenuAction.Status.Disabled;
            }
            evt.menu.AppendAction("Show Generation Data", _ =>
            {
                var asset = this.GetAsset();
                if (asset == null || audioClipResult == null)
                    return;
                this.GetStoreApi().Dispatch(GenerationSettingsActions.openGenerationDataWindow, new GenerationDataWindowArgs(asset, this, audioClipResult));
            }, showGenerationDataStatus);
        }

        void BuildFailedContextualMenu(ContextualMenuPopulateEvent evt)
        {
            evt.menu.MenuItems().Clear();
            if (audioClipResult is AudioClipSkeleton)
                return;

            evt.menu.AppendAction(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Show in Explorer" :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Reveal in Finder" : "Show in File Manager", _ =>
                {
                    EditorUtility.RevealInFinder(File.Exists(audioClipResult.uri.GetLocalPath())
                        ? audioClipResult.uri.GetLocalPath()
                        : Path.GetDirectoryName(audioClipResult.uri.GetLocalPath()));
                }, DropdownMenuAction.AlwaysEnabled);

            var showGenerationDataStatus = DropdownMenuAction.Status.Normal;
            if (this.GetAsset() == null || audioClipResult == null || !File.Exists(audioClipResult.uri.GetLocalPath()))
            {
                showGenerationDataStatus = DropdownMenuAction.Status.Disabled;
            }
            evt.menu.AppendAction("Show Generation Data", _ =>
            {
                var asset = this.GetAsset();
                if (asset == null || audioClipResult == null)
                    return;
                this.GetStoreApi().Dispatch(GenerationSettingsActions.openGenerationDataWindow, new GenerationDataWindowArgs(asset, this, audioClipResult));
            }, showGenerationDataStatus);
        }

        async void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (audioClipResult is AudioClipSkeleton or null)
            {
                m_Image.style.backgroundImage = null;
                return;
            }

            var width = resolvedStyle.width;
            var height = resolvedStyle.height;

            if (width is <= 0 or float.NaN)
                return;

            var audioClip = await audioClipResult.GetAudioClip();
            if (audioClip == null)
                return;

            var screenScaleFactor = this.GetContext<ScreenScaleFactor>()?.value ?? 1.0f;

            RenderTexture rt;

            if (!audioClipResult.IsFailed())
            {
                var sampleReferenceTexture = audioClip.MakeSampleReference((int)width);
                if (sampleReferenceTexture == null)
                    return;

                var markerSettings = new SoundEnvelopeMarkerSettings
                {
                    envelopeSettings = new SoundEnvelopeSettings(),
                    panOffset = 0,
                    padding = 4,
                    playbackPosition = m_CurrentTime / Mathf.Max(audioClip.length, 0.01f),
                    selectedPointIndex = -1,
                    showControlLines = false,
                    showControlPoints = false,
                    showCursor = m_CurrentTime != 0,
                    showMarker = false,
                    zoomScale = 1,
                    width = width,
                    height = height,
                    screenScaleFactor = screenScaleFactor,
                };

                m_WaveformTexture = AudioClipOscillogramRenderingUtils.GetTemporary(sampleReferenceTexture, markerSettings, m_WaveformTexture);
                if (m_WaveformTexture == null)
                    return;

                rt = m_CompositeTexture = AudioClipMarkerRenderingUtils.GetTemporary(m_WaveformTexture, markerSettings, m_CompositeTexture);

                // Set the result to the image
                m_Image.style.backgroundImage = Background.FromRenderTexture(rt);
                m_Image.MarkDirtyRepaint();
            }
            else
            {
                rt = await TextureCache.GetPreview(new Uri(Path.GetFullPath(ImageFileUtilities.failedDownloadIcon), UriKind.Absolute), (int)(height * screenScaleFactor));
                m_Image.style.backgroundImage = Background.FromRenderTexture(rt);
            }

            if (!rt)
                return;

            m_Image.EnableInClassList("image-stretch-to-fill", !audioClipResult.IsFailed() && (resolvedStyle.width <= rt.width || resolvedStyle.height <= rt.height));
            m_Image.EnableInClassList("image-scale-to-fit", audioClipResult.IsFailed() && (resolvedStyle.width <= rt.width || resolvedStyle.height <= rt.height));
            m_Image.EnableInClassList("image-scale-initial", resolvedStyle.width > rt.width && resolvedStyle.height > rt.height);
        }

        public void OnScreenScaleFactorChanged(ScreenScaleFactor _) => OnGeometryChanged(null);

        public void SetGeneration(AudioClipResult result)
        {
            if (audioClipResult == result)
            {
                SetSelectedGeneration(this.GetState().SelectSelectedGeneration(this));
                return;
            }

            m_SpinnerManipulator.Stop();

            m_Label.SetShown(result is AudioClipSkeleton);

            m_Image.style.backgroundImage = null;

            m_PlayManipulator?.Cancel();

            audioClipResult = result;

            m_DragManipulator.externalFilePath = audioClipResult?.uri.GetLocalPath();
            m_DragManipulator.newFileName = Path.GetFileNameWithoutExtension(this.GetAsset().GetPath());

            if (audioClipResult.IsFailed())
            {
                this.RemoveManipulator(m_ContextualMenu);
                this.RemoveManipulator(m_DragManipulator);
                this.AddManipulator(m_FailedContextualMenu);
            }
            else
            {
                this.RemoveManipulator(m_FailedContextualMenu);
                this.AddManipulator(m_ContextualMenu);
                this.AddManipulator(m_DragManipulator);
            }

            SetSelectedGeneration(this.GetState().SelectSelectedGeneration(this));

            if (result is AudioClipSkeleton)
            {
                SetGenerationProgress(new [] { this.GetState().SelectGenerationProgress(this, result) });
                return;
            }

            OnGeometryChanged(null);
        }

        async void UpdateTooltip()
        {
            tooltip = this.GetState()?.SelectTooltipModelSettings(null);
            if (audioClipResult is null or AudioClipSkeleton)
                return;

            m_Metadata = await audioClipResult.GetMetadata();
            tooltip = this.GetState()?.SelectTooltipModelSettings(m_Metadata);
        }
    }

    class GenerationTileItem : VisualElement
    {
        GenerationTile m_Tile;

        public GenerationTile tile
        {
            get => m_Tile;
            set
            {
                if (m_Tile != null)
                    Remove(m_Tile);
                if (value != null)
                    Add(value);
                m_Tile = value;
            }
        }
    }
}
