using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.AI.Animate.Services.Stores.Actions;
using Unity.AI.Animate.Services.Stores.Actions.Payloads;
using Unity.AI.Animate.Services.Stores.Selectors;
using Unity.AI.Animate.Services.Stores.States;
using Unity.AI.Animate.Services.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Contexts;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Animate.Components
{
    [UxmlElement]
    partial class GenerationTile : VisualElement
    {
        public VisualElement image { get; }
        public VisualElement progress { get; }

        public AnimationClipResult animationClipResult { get; private set; }

        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Animate/Components/GenerationSelector/GenerationTile.uxml";

        float m_AnimationTime;
        double m_LastEditorTime;

        readonly DragExternalFileManipulator m_DragManipulator;
        readonly SpinnerManipulator m_SpinnerManipulator;
        readonly ContextualMenuManipulator m_ContextualMenu;
        readonly ContextualMenuManipulator m_FailedContextualMenu;

        RenderTexture m_AnimationTexture;

        bool m_IsHovered;
        IVisualElementScheduledItem m_Scheduled;
        RenderTexture m_SkeletonTexture;
        readonly Label m_Label;
        readonly Label m_Type;
        GenerationMetadata m_Metadata;

        public GenerationTile()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            AddToClassList("generation-tile");

            m_Label = this.Q<Label>("label");
            m_Type = this.Q<Label>("type");
            image = this.Q<VisualElement>("border");
            RegisterCallback<AttachToPanelEvent>(_ => {
                if (m_SkeletonTexture)
                    progress.style.backgroundImage = Background.FromRenderTexture(m_SkeletonTexture); });
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            RegisterCallback<MouseEnterEvent>(MouseEnter);
            RegisterCallback<MouseLeaveEvent>(MouseLeave);
            RegisterCallback<MouseOverEvent>(_ => {
                var unused = UpdateTooltip();
            });

            m_ContextualMenu = new ContextualMenuManipulator(BuildContextualMenu);
            m_FailedContextualMenu = new ContextualMenuManipulator(BuildFailedContextualMenu);
            m_DragManipulator = new DragExternalFileManipulator
            {
                newFileName = AssetUtils.defaultNewAssetName + AssetUtils.defaultAssetExtension, // we need to force the extension because our artifacts are json poses
                copyFunction = data => SessionActions.promoteGenerationUnsafe((new DragAndDropGenerationData(this.GetAsset(),
                    AnimationClipResult.FromPath(data.sourcePath), data.destinationPath), this.GetStoreApi())).GetPath()
            };

            progress = this.Q<VisualElement>("progress");
            progress.AddManipulator(m_SpinnerManipulator = new SpinnerManipulator());

            this.AddManipulator(new DelayedCleanupManipulator(() =>
            {
                if (m_AnimationTexture)
                {
                    RenderTexture.ReleaseTemporary(m_AnimationTexture);
                    m_AnimationTexture = null;
                }
            }));
        }

        void MouseLeave(MouseLeaveEvent evt)
        {
            m_IsHovered = false;
            m_Scheduled?.Pause();
        }

        void MouseEnter(MouseEnterEvent evt)
        {
            m_IsHovered = true;
            m_LastEditorTime = EditorApplication.timeSinceStartup;

            if (m_Scheduled == null)
                m_Scheduled = schedule.Execute(UpdateAnimationTime).Every(20);
            else
                m_Scheduled.Resume();
        }

        void UpdateAnimationTime()
        {
            if (!m_IsHovered)
                return;

            // Advance our time based on how much real time has passed
            var currentEditorTime = EditorApplication.timeSinceStartup;
            var delta = currentEditorTime - m_LastEditorTime;
            m_LastEditorTime = currentEditorTime;
            m_AnimationTime += (float)delta;

            // Repaint with updated time
            OnGeometryChanged(null);
        }

        public void SetGenerationProgress(IEnumerable<GenerationProgressData> data)
        {
            if (animationClipResult is not AnimationClipSkeleton)
                return;

            var progressReport = data.FirstOrDefault(d => d.taskID == ((AnimationClipSkeleton)animationClipResult).taskID);
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

        public void SetSelectedGeneration(AnimationClipResult result) => this.SetSelected(animationClipResult != null && result != null && animationClipResult.uri == result.uri);

        void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            evt.menu.MenuItems().Clear();
            if (animationClipResult is AnimationClipSkeleton)
                return;

            evt.menu.AppendAction("Select", _ =>
            {
                if (this.GetAsset() == null || animationClipResult == null)
                    return;
                var asset = this.GetAsset();
                this.GetStoreApi().Dispatch(GenerationResultsActions.selectGeneration, new(asset, animationClipResult, false, false));
            }, DropdownMenuAction.AlwaysEnabled);
            evt.menu.AppendAction("Promote to current asset", _ =>
            {
                var asset = this.GetAsset();
                this.GetStoreApi().Dispatch(GenerationResultsActions.selectGeneration, new(asset, animationClipResult, true, false));
            }, DropdownMenuAction.AlwaysEnabled);
            evt.menu.AppendAction("Promote to new asset", _ =>
            {
                var asset = this.GetAsset();
                this.GetStoreApi().Dispatch(SessionActions.promoteGeneration, new (asset, animationClipResult));
            }, DropdownMenuAction.AlwaysEnabled);

            evt.menu.AppendAction(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Show in Explorer" :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Reveal in Finder" : "Show in File Manager", _ =>
            {
                EditorUtility.RevealInFinder(File.Exists(animationClipResult.uri.GetLocalPath())
                    ? animationClipResult.uri.GetLocalPath()
                    : Path.GetDirectoryName(animationClipResult.uri.GetLocalPath()));
            }, DropdownMenuAction.AlwaysEnabled);

            var showGenerationDataStatus = DropdownMenuAction.Status.Normal;
            if (this.GetAsset() == null || animationClipResult == null || !File.Exists(animationClipResult.uri.GetLocalPath()))
            {
                showGenerationDataStatus = DropdownMenuAction.Status.Disabled;
            }
            evt.menu.AppendAction("Show Generation Data", _ =>
            {
                if (this.GetAsset() == null || animationClipResult == null)
                    return;
                var asset = this.GetAsset();
                this.GetStoreApi().Dispatch(GenerationSettingsActions.openGenerationDataWindow, new GenerationDataWindowArgs(asset, this, animationClipResult));
            }, showGenerationDataStatus);

            if (Unsupported.IsDeveloperMode() && HasFbxDownloadOption())
            {
                evt.menu.AppendSeparator();
                evt.menu.AppendAction("Download FBX", async _ =>
                {
                    var path = animationClipResult.uri.GetLocalPath();
                    var defaultFileName = Path.GetFileNameWithoutExtension(path) + AssetUtils.fbxAssetExtension;
                    var projectPath = Path.Combine(Application.dataPath, "..");
                    var saveFilePath = EditorUtility.SaveFilePanel(
                        "Save FBX File",
                        projectPath,
                        defaultFileName,
                        "fbx");

                    if (string.IsNullOrEmpty(saveFilePath) || !File.Exists(path))
                        return;

                    await FileIO.CopyFileAsync(path, saveFilePath, true);
                    AssetDatabaseExtensions.ImportGeneratedAsset(saveFilePath);
                }, DropdownMenuAction.AlwaysEnabled);
            }
        }

        void BuildFailedContextualMenu(ContextualMenuPopulateEvent evt)
        {
            evt.menu.MenuItems().Clear();
            if (animationClipResult is AnimationClipSkeleton)
                return;

            evt.menu.AppendAction(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Show in Explorer" :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Reveal in Finder" : "Show in File Manager", _ =>
                {
                    EditorUtility.RevealInFinder(File.Exists(animationClipResult.uri.GetLocalPath())
                        ? animationClipResult.uri.GetLocalPath()
                        : Path.GetDirectoryName(animationClipResult.uri.GetLocalPath()));
                }, DropdownMenuAction.AlwaysEnabled);

            var showGenerationDataStatus = DropdownMenuAction.Status.Normal;
            if (this.GetAsset() == null || animationClipResult == null || !File.Exists(animationClipResult.uri.GetLocalPath()))
            {
                showGenerationDataStatus = DropdownMenuAction.Status.Disabled;
            }
            evt.menu.AppendAction("Show Generation Data", _ =>
            {
                var asset = this.GetAsset();
                if (asset == null || animationClipResult == null)
                    return;
                this.GetStoreApi().Dispatch(GenerationSettingsActions.openGenerationDataWindow, new GenerationDataWindowArgs(asset, this, animationClipResult));
            }, showGenerationDataStatus);
        }

        async void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (animationClipResult is AnimationClipSkeleton or null)
            {
                image.style.backgroundImage = null;
                return;
            }

            var width = resolvedStyle.width;
            var height = resolvedStyle.height;

            if (width is <= 0 or float.NaN)
                return;

            if (animationClipResult.IsValid() && !AnimationClipCache.Peek(animationClipResult.uri))
                await EditorTask.Yield();

            var animationClip = await animationClipResult.GetAnimationClip();
            if (animationClip == null)
                return;

            RenderTexture rt = null;

            var screenScaleFactor = this.GetContext<ScreenScaleFactor>()?.value ?? 1f;
            if (!animationClipResult.IsFailed())
            {
                rt = m_AnimationTexture = animationClip.GetTemporary(m_AnimationTime, (int)(width * screenScaleFactor), (int)(height * screenScaleFactor), m_AnimationTexture);
                image.style.backgroundImage = Background.FromRenderTexture(m_AnimationTexture);
                image.MarkDirtyRepaint();
            }
            else
            {
                rt = await TextureCache.GetPreview(new Uri(Path.GetFullPath(ImageFileUtilities.failedDownloadIcon), UriKind.Absolute), (int)(width * screenScaleFactor));
                image.style.backgroundImage = Background.FromRenderTexture(rt);
            }

            if (!rt)
                return;

            image.EnableInClassList("image-scale-to-fit", resolvedStyle.width <= rt.width || resolvedStyle.height <= rt.height);
            image.EnableInClassList("image-scale-initial", resolvedStyle.width > rt.width && resolvedStyle.height > rt.height);
        }

        public void OnScreenScaleFactorChanged(ScreenScaleFactor _) => OnGeometryChanged(null);

        public void SetGeneration(AnimationClipResult result)
        {
            if (animationClipResult == result)
            {
                SetSelectedGeneration(this.GetState().SelectSelectedGeneration(this));
                return;
            }

            m_SpinnerManipulator.Stop();

            m_Label.SetShown(result is AnimationClipSkeleton);
            _ = UpdateTypeLabel();

            image.style.backgroundImage = null;

            animationClipResult = result;

            m_DragManipulator.externalFilePath = animationClipResult?.uri.GetLocalPath();
            m_DragManipulator.newFileName = Path.GetFileNameWithoutExtension(this.GetAsset().GetPath()) + AssetUtils.defaultAssetExtension; // we need to force the extension because our artifacts are lists of json

            if (animationClipResult.IsFailed())
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

            if (result is AnimationClipSkeleton)
            {
                SetGenerationProgress(new [] { this.GetState().SelectGenerationProgress(this, result) });
                return;
            }

            OnGeometryChanged(null);

            return;

            async Task UpdateTypeLabel()
            {
                m_Type.text = "";
                if (Unsupported.IsDeveloperMode() && IsFbxResult(result))
                {
                    m_Type.text = "FBX";
                    m_Type.SetShown();
                    return;
                }

                var metadata = await result.GetMetadata();
                if (metadata.isTrimmed)
                {
                    m_Type.text = "TRIM";
                    m_Type.SetShown();
                    return;
                }

                m_Type.SetShown(false);
            }
        }

        static bool IsFbxResult(AnimationClipResult result)
        {
            if (result == null || result is AnimationClipSkeleton || result.IsFailed())
                return false;

            return result.IsFbx();
        }

        bool HasFbxDownloadOption()
        {
            if (animationClipResult == null || animationClipResult is AnimationClipSkeleton || animationClipResult.IsFailed())
                return false;

            return animationClipResult.IsFbx();
        }

        async Task UpdateTooltip()
        {
            tooltip = this.GetState()?.SelectTooltipModelSettings(null);
            if (animationClipResult is null or AnimationClipSkeleton)
                return;

            m_Metadata = await animationClipResult.GetMetadata();
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
