using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Contexts;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Actions;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Mesh.Services.Stores.Actions;
using Unity.AI.Mesh.Services.Stores.Actions.Payloads;
using Unity.AI.Mesh.Services.Stores.Selectors;
using Unity.AI.Mesh.Services.Stores.States;
using Unity.AI.Mesh.Services.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Mesh.Components
{
    [UxmlElement]
    partial class GenerationTile : VisualElement
    {
        public VisualElement image { get; }
        public VisualElement progress { get; }

        public MeshResult meshResult { get; private set; }

        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Mesh/Components/GenerationSelector/GenerationTile.uxml";

        float m_RotationY;
        double m_LastEditorTime;

        readonly DragExternalFileManipulator m_DragManipulator;
        readonly SpinnerManipulator m_SpinnerManipulator;
        readonly ContextualMenuManipulator m_ContextualMenu;
        readonly ContextualMenuManipulator m_FailedContextualMenu;

        bool m_IsHovered;
        IVisualElementScheduledItem m_Scheduled;
        RenderTexture m_SkeletonTexture;
        readonly Label m_Label;
        readonly Label m_Type;
        GenerationMetadata m_Metadata;
        readonly GenerationFeedbackManipulator m_FeedbackManipulator;

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
            m_DragManipulator = new DragExternalFileManipulator { newFileName = AssetUtils.defaultNewAssetName };

            progress = this.Q<VisualElement>("progress");
            progress.AddManipulator(m_SpinnerManipulator = new SpinnerManipulator());

            // Initialize feedback manipulator
            m_FeedbackManipulator = new GenerationFeedbackManipulator(
                FeedbackActions.MeshGenerationDialogType,
                () => meshResult?.uri,
                this.GetAsset,
                this.GetStoreApi,
                () => this.GetState()?.SelectSubmittedFeedbackSentiment(this.GetAsset(), meshResult?.uri?.AbsoluteUri),
                () => meshResult != null && meshResult is not MeshSkeleton && !meshResult.IsFailed());
            this.AddManipulator(m_FeedbackManipulator);
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
                m_Scheduled = schedule.Execute(UpdateRotation).Every(20);
            else
                m_Scheduled.Resume();
        }

        void UpdateRotation()
        {
            if (!m_IsHovered)
                return;

            // Advance our rotation based on how much real time has passed
            var currentEditorTime = EditorApplication.timeSinceStartup;
            var deltaTimeInSeconds = currentEditorTime - m_LastEditorTime;
            m_LastEditorTime = currentEditorTime;
            m_RotationY += (float)deltaTimeInSeconds * 60f;

            // Repaint with updated rotation
            OnGeometryChanged(null);
        }

        public void SetGenerationProgress(IEnumerable<GenerationProgressData> data)
        {
            if (meshResult is not MeshSkeleton)
                return;

            var progressReport = data.FirstOrDefault(d => d.taskID == ((MeshSkeleton)meshResult).taskID);
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

        public void SetSelectedGeneration(MeshResult result) => this.SetSelected(meshResult != null && result != null && meshResult.uri == result.uri);

        void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            evt.menu.MenuItems().Clear();
            if (meshResult is MeshSkeleton)
                return;

            evt.menu.AppendAction("Select", _ =>
            {
                if (this.GetAsset() == null || meshResult == null)
                    return;
                var asset = this.GetAsset();
                this.GetStoreApi().Dispatch(GenerationResultsActions.selectGeneration, new(asset, meshResult, false, false));
            }, DropdownMenuAction.AlwaysEnabled);
            evt.menu.AppendAction("Promote to current asset", _ =>
            {
                var asset = this.GetAsset();
                this.GetStoreApi().Dispatch(GenerationResultsActions.selectGeneration, new(asset, meshResult, true, false));
            }, DropdownMenuAction.AlwaysEnabled);
            evt.menu.AppendAction("Promote to new asset", _ =>
            {
                var asset = this.GetAsset();
                this.GetStoreApi().Dispatch(SessionActions.promoteGeneration, new (asset, meshResult));
            }, DropdownMenuAction.AlwaysEnabled);

            evt.menu.AppendAction(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Show in Explorer" :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Reveal in Finder" : "Show in File Manager", _ =>
            {
                EditorUtility.RevealInFinder(File.Exists(meshResult.uri.GetLocalPath())
                    ? meshResult.uri.GetLocalPath()
                    : Path.GetDirectoryName(meshResult.uri.GetLocalPath()));
            }, DropdownMenuAction.AlwaysEnabled);

            var showGenerationDataStatus = DropdownMenuAction.Status.Normal;
            if (this.GetAsset() == null || meshResult == null || !File.Exists(meshResult.uri.GetLocalPath()))
            {
                showGenerationDataStatus = DropdownMenuAction.Status.Disabled;
            }
            evt.menu.AppendAction("Show Generation Data", _ =>
            {
                if (this.GetAsset() == null || meshResult == null)
                    return;
                var asset = this.GetAsset();
                this.GetStoreApi().Dispatch(GenerationSettingsActions.openGenerationDataWindow, new GenerationDataWindowArgs(asset, this, meshResult));
            }, showGenerationDataStatus);

            if (Unsupported.IsDeveloperMode() && HasDownloadOption())
            {
                evt.menu.AppendSeparator();
                evt.menu.AppendAction("Download 3D Model", async _ =>
                {
                    var path = meshResult.uri.GetLocalPath();
                    var defaultFileName = Path.GetFileNameWithoutExtension(path) + Path.GetExtension(path);
                    var projectPath = Path.Combine(Application.dataPath, "..");
                    var saveFilePath = EditorUtility.SaveFilePanel(
                        "Save 3D Model",
                        projectPath,
                        defaultFileName,
                        Path.GetExtension(path));

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
            if (meshResult is MeshSkeleton)
                return;

            evt.menu.AppendAction(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Show in Explorer" :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Reveal in Finder" : "Show in File Manager", _ =>
                {
                    EditorUtility.RevealInFinder(File.Exists(meshResult.uri.GetLocalPath())
                        ? meshResult.uri.GetLocalPath()
                        : Path.GetDirectoryName(meshResult.uri.GetLocalPath()));
                }, DropdownMenuAction.AlwaysEnabled);

            var showGenerationDataStatus = DropdownMenuAction.Status.Normal;
            if (this.GetAsset() == null || meshResult == null || !File.Exists(meshResult.uri.GetLocalPath()))
            {
                showGenerationDataStatus = DropdownMenuAction.Status.Disabled;
            }
            evt.menu.AppendAction("Show Generation Data", _ =>
            {
                var asset = this.GetAsset();
                if (asset == null || meshResult == null)
                    return;
                this.GetStoreApi().Dispatch(GenerationSettingsActions.openGenerationDataWindow, new GenerationDataWindowArgs(asset, this, meshResult));
            }, showGenerationDataStatus);
        }

        async void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (meshResult is MeshSkeleton or null)
            {
                image.style.backgroundImage = null;
                return;
            }

            var width = resolvedStyle.width;
            var height = resolvedStyle.height;
            if (width is <= 0 or float.NaN)
                return;

            RenderTexture rt = null;
            var screenScaleFactor = this.GetContext<ScreenScaleFactor>()?.value ?? 1f;
            var scaledWidth = (int)(width * screenScaleFactor);
            var scaledHeight = (int)(height * screenScaleFactor);

            if (meshResult.IsFailed())
            {
                rt = await TextureCache.GetPreview(new Uri(Path.GetFullPath(ImageFileUtilities.failedDownloadIcon), UriKind.Absolute), scaledWidth);
            }
            else
            {
                var cacheKey = MeshPreviewCache.GetTurntableCacheKey(meshResult, MeshPreviewCacheExtensions.defaultSize, MeshPreviewCacheExtensions.defaultFrameCount);

                // Check if the full turntable animation is ready without blocking.
                if (MeshPreviewCache.Instance.Peek(cacheKey, MeshPreviewCacheExtensions.defaultFrameCount))
                {
                    // If the cache is ready, get the correct animated frame for the turntable.
                    rt = await meshResult.GetPreviewAsync(m_RotationY, MeshPreviewCacheExtensions.defaultSize, MeshPreviewCacheExtensions.defaultFrameCount);
                }
                else
                {
                    // Immediately show a static, single-frame preview as a fallback.
                    rt = await TextureCache.GetPreview(meshResult.uri, scaledWidth);

                    // If the cache is NOT ready and Rotation is not 0, kick off the rendering process in the background (fire-and-forget).
                    if (!Mathf.Approximately(m_RotationY, 0))
                        _ = MeshPreviewCache.Instance.GetOrRenderFramesAsync(cacheKey, meshResult, MeshPreviewCacheExtensions.defaultSize, MeshPreviewCacheExtensions.defaultFrameCount);
                }
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
        }

        public void OnScreenScaleFactorChanged(ScreenScaleFactor _) => OnGeometryChanged(null);

        public void SetGeneration(MeshResult result)
        {
            if (meshResult == result)
            {
                SetSelectedGeneration(this.GetState().SelectSelectedGeneration(this));
                return;
            }

            m_SpinnerManipulator.Stop();

            m_Label.SetShown(result is MeshSkeleton);
            _ = UpdateTypeLabel();

            image.style.backgroundImage = null;

            meshResult = result;

            m_DragManipulator.externalFilePath = meshResult?.uri.GetLocalPath();
            m_DragManipulator.newFileName = Path.GetFileNameWithoutExtension(this.GetAsset().GetPath());

            if (meshResult.IsFailed())
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

            if (result is MeshSkeleton)
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

                if (Unsupported.IsDeveloperMode() && IsGlbResult(result))
                {
                    m_Type.text = "GLTF";
                    m_Type.SetShown();
                    return;
                }

                var metadata = await result.GetMetadata();
                // NOTE: Mesh-specific metadata checks (e.g., isDecimated) could be added here.
                // For now, we only check for FBX type.

                m_Type.SetShown(false);
            }
        }

        static bool IsFbxResult(MeshResult result)
        {
            if (result == null || result is MeshSkeleton || result.IsFailed())
                return false;

            return result.IsFbx();
        }

        static bool IsGlbResult(MeshResult result)
        {
            if (result == null || result is MeshSkeleton || result.IsFailed())
                return false;

            return result.IsGlb();
        }

        bool HasDownloadOption()
        {
            if (meshResult == null || meshResult is MeshSkeleton || meshResult.IsFailed())
                return false;

            return meshResult.IsFbx() || meshResult.IsGlb();
        }

        async Task UpdateTooltip()
        {
            tooltip = this.GetState()?.SelectTooltipModelSettings(null);
            if (meshResult is null or MeshSkeleton)
                return;

            m_Metadata = await meshResult.GetMetadata();
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
