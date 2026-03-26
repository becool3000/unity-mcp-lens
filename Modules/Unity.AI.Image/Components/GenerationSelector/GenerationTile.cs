using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.Actions.Payloads;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Image.Services.Utilities;
using Unity.AI.Generators.Contexts;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Actions;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class GenerationTile : VisualElement
    {
        public VisualElement image { get; }
        public VisualElement progress { get; }

        public TextureResult textureResult { get; private set; }

        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/GenerationSelector/GenerationTile.uxml";

        float m_VideoTime;
        double m_LastEditorTime;
        bool m_IsHovered;
        IVisualElementScheduledItem m_Scheduled;
        RenderTexture m_SheetFrameTexture;

        readonly DragExternalFileManipulator m_DragManipulator;
        readonly SpinnerManipulator m_SpinnerManipulator;
        readonly ContextualMenuManipulator m_ContextualMenu;
        readonly ContextualMenuManipulator m_FailedContextualMenu;

        RenderTexture m_SkeletonTexture;
        readonly Label m_Label;
        readonly Label m_Type;
        GenerationMetadata m_Metadata;
        readonly GenerationFeedbackManipulator m_FeedbackManipulator;

        public GenerationTile()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            m_Label = this.Q<Label>("label");
            m_Type = this.Q<Label>("type");
            image = this.Q<VisualElement>("image");

            // --- Event & Manipulator Setup ---
            RegisterCallback<AttachToPanelEvent>(_ => {
                if (m_SkeletonTexture)
                    progress.style.backgroundImage = Background.FromRenderTexture(m_SkeletonTexture); });
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            RegisterCallback<MouseOverEvent>(_ => UpdateTooltip());
            RegisterCallback<MouseEnterEvent>(OnMouseEnter);
            RegisterCallback<MouseLeaveEvent>(OnMouseLeave);

            m_ContextualMenu = new ContextualMenuManipulator(BuildContextualMenu);
            m_FailedContextualMenu = new ContextualMenuManipulator(BuildFailedContextualMenu);
            m_DragManipulator = new DragExternalFileManipulator { newFileName = AssetUtils.defaultNewAssetName };

            progress = this.Q<VisualElement>("progress");
            progress.AddManipulator(m_SpinnerManipulator = new SpinnerManipulator());

            // Initialize feedback manipulator
            m_FeedbackManipulator = new GenerationFeedbackManipulator(
                FeedbackActions.ImageGenerationDialogType,
                () => textureResult?.uri,
                this.GetAsset,
                this.GetStoreApi,
                () => this.GetState()?.SelectSubmittedFeedbackSentiment(this.GetAsset(), textureResult?.uri?.AbsoluteUri),
                () => textureResult != null && textureResult is not TextureSkeleton && !textureResult.IsFailed());
            this.AddManipulator(m_FeedbackManipulator);

            this.AddManipulator(new DelayedCleanupManipulator(() =>
            {
                EditorApplication.playModeStateChanged -= OnPlayModeStateChangedForGetSpriteSheetFrameAsync;

                if (m_SheetFrameTexture)
                {
                    m_SheetFrameTexture.Release();
                    m_SheetFrameTexture.SafeDestroy();
                    m_SheetFrameTexture = null;
                }
            }));
        }

        void OnMouseLeave(MouseLeaveEvent evt)
        {
            m_IsHovered = false;
            m_Scheduled?.Pause();
        }

        void OnMouseEnter(MouseEnterEvent evt)
        {
            m_IsHovered = true;

            if (!textureResult.IsVideoClip() && !textureResult.IsSpriteSheet()) return;

            m_LastEditorTime = EditorApplication.timeSinceStartup;

            if (m_Scheduled == null)
                m_Scheduled = schedule.Execute(UpdateAnimation).Every(33); // Approx 30 FPS
            else
                m_Scheduled.Resume();
        }

        void UpdateAnimation()
        {
            if (!m_IsHovered || (!textureResult.IsVideoClip() && !textureResult.IsSpriteSheet()))
                return;

            var currentEditorTime = EditorApplication.timeSinceStartup;
            var deltaTimeInSeconds = currentEditorTime - m_LastEditorTime;
            m_LastEditorTime = currentEditorTime;

            m_VideoTime += (float)deltaTimeInSeconds;

            // Repaint with the new frame.
            OnGeometryChanged(null);
        }

        public void SetGenerationProgress(IEnumerable<GenerationProgressData> data)
        {
            if (textureResult is not TextureSkeleton)
                return;

            var progressReport = data.FirstOrDefault(d => d.taskID == ((TextureSkeleton)textureResult).taskID);
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

        public void SetSelectedGeneration(TextureResult result) => this.SetSelected(this.textureResult != null && result != null && this.textureResult.uri == result.uri);

        void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            evt.menu.MenuItems().Clear();
            if (textureResult is TextureSkeleton)
                return;

            var isSelected = this.IsSelected();

            evt.menu.AppendAction(isSelected ? "Deselect" : "Select", _ =>
            {
                if (this.GetAsset() == null || textureResult == null)
                    return;
                var asset = this.GetAsset();
                this.GetStoreApi().Dispatch(GenerationResultsActions.selectGeneration, new(asset, isSelected ? new TextureResult() : textureResult, false, false));
            }, DropdownMenuAction.AlwaysEnabled);
            evt.menu.AppendAction("Promote to current asset", _ =>
            {
                var asset = this.GetAsset();
                this.GetStoreApi().Dispatch(GenerationResultsActions.selectGeneration, new(asset, textureResult, true, false));
            }, DropdownMenuAction.AlwaysEnabled);
            evt.menu.AppendAction("Promote to new asset", _ =>
            {
                var asset = this.GetAsset();
                this.GetStoreApi().Dispatch(SessionActions.promoteFocusedGeneration, new (asset, textureResult));
            }, DropdownMenuAction.AlwaysEnabled);
            evt.menu.AppendAction(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Show in Explorer" :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Reveal in Finder" : "Show in File Manager", _ =>
            {
                EditorUtility.RevealInFinder(File.Exists(textureResult.uri.GetLocalPath())
                    ? textureResult.uri.GetLocalPath()
                    : Path.GetDirectoryName(textureResult.uri.GetLocalPath()));
            }, DropdownMenuAction.AlwaysEnabled);

            var showGenerationDataStatus = DropdownMenuAction.Status.Normal;
            if (this.GetAsset() == null || textureResult == null || !File.Exists(textureResult.uri.GetLocalPath()))
            {
                showGenerationDataStatus = DropdownMenuAction.Status.Disabled;
            }
            evt.menu.AppendAction("Show Generation Data", _ =>
            {
                var asset = this.GetAsset();
                if (asset == null || textureResult == null)
                    return;
                this.GetStoreApi().Dispatch(GenerationSettingsActions.openGenerationDataWindow, new GenerationDataWindowArgs(asset, this, textureResult));
            }, showGenerationDataStatus);
        }

        void BuildFailedContextualMenu(ContextualMenuPopulateEvent evt)
        {
            evt.menu.MenuItems().Clear();
            if (textureResult is TextureSkeleton)
                return;

            evt.menu.AppendAction(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Show in Explorer" :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Reveal in Finder" : "Show in File Manager", _ =>
            {
                EditorUtility.RevealInFinder(File.Exists(textureResult.uri.GetLocalPath())
                    ? textureResult.uri.GetLocalPath()
                    : Path.GetDirectoryName(textureResult.uri.GetLocalPath()));
            }, DropdownMenuAction.AlwaysEnabled);

            var showGenerationDataStatus = DropdownMenuAction.Status.Normal;
            if (this.GetAsset() == null || textureResult == null || !File.Exists(textureResult.uri.GetLocalPath()))
            {
                showGenerationDataStatus = DropdownMenuAction.Status.Disabled;
            }
            evt.menu.AppendAction("Show Generation Data", _ =>
            {
                var asset = this.GetAsset();
                if (asset == null || textureResult == null)
                    return;

                this.GetStoreApi().Dispatch(GenerationSettingsActions.openGenerationDataWindow, new GenerationDataWindowArgs(asset, this, textureResult));
            }, showGenerationDataStatus);
        }

        async void OnPlayModeStateChangedForGetSpriteSheetFrameAsync(PlayModeStateChange obj)
        {
            // Small delay to ensure that the source image can be fetched correctly from the cache after entering play mode as it comes back from a SerializableSingleton and is then blit to our m_SheetFrameTexture on the GPU.
            await EditorTask.Delay(50);
            OnGeometryChanged(null);
        }

        public void SetGeneration(TextureResult result)
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChangedForGetSpriteSheetFrameAsync;
            if (!result.IsVideoClip() && result.IsSpriteSheet())
                EditorApplication.playModeStateChanged += OnPlayModeStateChangedForGetSpriteSheetFrameAsync;

            if (textureResult == result)
            {
                SetSelectedGeneration(this.GetState().SelectSelectedGeneration(this));
                return;
            }

            // Reset animation state for the new result
            m_VideoTime = 0f;
            m_IsHovered = false;
            m_Scheduled?.Pause();

            m_SpinnerManipulator.Stop();
            m_Label.SetShown(result is TextureSkeleton);
            m_Type.SetShown(false);
            image.style.backgroundImage = null;
            textureResult = result;

            m_DragManipulator.externalFilePath = textureResult?.uri.GetLocalPath();
            m_DragManipulator.newFileName = Path.GetFileNameWithoutExtension(this.GetAsset().GetPath());

            if (textureResult.IsFailed())
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

            if (textureResult is TextureSkeleton)
            {
                SetGenerationProgress(new [] { this.GetState().SelectGenerationProgress(this, textureResult) });
                return;
            }

            OnGeometryChanged(null);

            async Task SetBadgeAsync()
            {
                if (result.IsVideoClip())
                {
                    m_Type.SetShown();
                    m_Type.text = "SOURCE";
                    return;
                }

                if (result.IsSpriteSheet())
                {
                    m_Type.SetShown();
                    m_Type.text = "SHEET";
                    return;
                }

                using var stream = await ImageFileUtilities.GetCompatibleImageStreamAsync(result.uri);
                ImageFileUtilities.TryGetImageDimensions(stream, out var width, out var height);
                SetBadgeIfUpscaled(width, height);
            }
            _ = SetBadgeAsync();
        }

        async void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (textureResult is TextureSkeleton or null)
            {
                image.style.backgroundImage = null;
                return;
            }

            var width = resolvedStyle.width;
            if (width is <= 0 or float.NaN)
                return;

            RenderTexture rt = null;
            var screenScaleFactor = this.GetContext<ScreenScaleFactor>()?.value ?? 1f;
            var scaledWidth = Mathf.RoundToInt(width * screenScaleFactor);

            var reusableBufferInUse = false;
            if (textureResult.IsFailed())
            {
                rt = await TextureCache.GetPreview(new Uri(Path.GetFullPath(ImageFileUtilities.failedDownloadIcon), UriKind.Absolute), scaledWidth);
            }
            else if (textureResult.IsVideoClip())
            {
                var cacheKey = VideoResultFrameCache.GetVideoCacheKey(textureResult);

                // Check if the full animation cache is ready without blocking.
                if (VideoResultFrameCache.Instance.Peek(cacheKey, VideoResultFrameCache.FrameCount))
                {
                    // If the cache is ready, get the correct animated frame.
                    rt = await textureResult.GetVideoFrameAsync(scaledWidth, scaledWidth, m_VideoTime);
                }
                else
                {
                    // Immediately show the first frame of the video as a static fallback preview.
                    rt = await TextureCache.GetPreview(textureResult.uri, scaledWidth);

                    // If the cache is NOT ready and VideoTime is not 0, kick off the rendering process in the background (fire-and-forget).
                    if (!Mathf.Approximately(m_VideoTime, 0))
                        _ = VideoResultFrameCache.Instance.GetOrRenderFramesAsync(cacheKey, textureResult, VideoFrameCacheExtensions.defaultSize, VideoResultFrameCache.FrameCount);
                }
            }
            else if (textureResult.IsSpriteSheet()) // but !IsVideoClip
            {
                reusableBufferInUse = true;
                // Use the GetFrameAsync extension to get the correct frame based on normalized time.
                // Pass m_SheetFrameTexture as the reusable buffer to avoid allocations.
                rt = m_SheetFrameTexture = await textureResult.GetSpriteSheetFrameAsync(scaledWidth, scaledWidth, m_SheetFrameTexture, m_VideoTime);
            }
            else
            {
                rt = await TextureCache.GetPreview(textureResult.uri, scaledWidth);
            }

            if (rt)
            {
                image.style.backgroundImage = Background.FromRenderTexture(rt);
                image.EnableInClassList("image-scale-to-fit", resolvedStyle.width <= rt.width || resolvedStyle.height <= rt.height);
                image.EnableInClassList("image-scale-initial", resolvedStyle.width > rt.width && resolvedStyle.height > rt.height);
            }
            else
            {
                image.style.backgroundImage = null;
            }
            image.MarkDirtyRepaint();

            if (!reusableBufferInUse && m_SheetFrameTexture)
            {
                m_SheetFrameTexture.Release();
                m_SheetFrameTexture.SafeDestroy();
                m_SheetFrameTexture = null;
            }
        }

        public void OnScreenScaleFactorChanged(ScreenScaleFactor _) => OnGeometryChanged(null);

        internal static string GetTextureSizeBadge(int width, int height)
        {
            // Take the biggest size to determine the badge
            var maxSize = Math.Max(width, height);
            var badgeNumber = Mathf.Floor(maxSize / 1024f);
            return badgeNumber <= 1 ? string.Empty : $"{badgeNumber}K";
        }

        void SetBadgeIfUpscaled(int width, int height)
        {
            var badgeText = GetTextureSizeBadge(width, height);
            if (!string.IsNullOrEmpty(badgeText))
            {
                m_Type.SetShown();
                m_Type.text = badgeText;
            }
        }

        async void UpdateTooltip()
        {
            tooltip = this.GetState()?.SelectTooltipModelSettings(null);
            if (textureResult is null or TextureSkeleton)
                return;

            m_Metadata = await textureResult.GetMetadataAsync();
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
