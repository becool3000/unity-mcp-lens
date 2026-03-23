using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Unity.AI.Animate.Services.Stores.Actions;
using Unity.AI.Animate.Services.Stores.Selectors;
using Unity.AI.Animate.Services.Stores.States;
using Unity.AI.Animate.Services.Utilities;
using Unity.AI.Animate.Windows;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Contexts;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Generators.UI.Selectors;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Animate.Components
{
    [UxmlElement]
    partial class GenerationSelector : VisualElement
    {
        readonly GridView m_GridView;
        readonly Button m_OpenGeneratorsWindowButton;
        readonly VisualElement m_PrecacheSpinner;
        readonly Label m_PrecacheLabel;
        readonly SpinnerManipulator m_SpinnerManipulator;

        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Animate/Components/GenerationSelector/GenerationSelector.uxml";

        GenerationFileSystemWatcher m_GenerationFileSystemWatcher;
        float m_PreviewSizeFactor = 1;

        float GetPreviewSize() => Mathf.NextPowerOfTwo(127) * m_PreviewSizeFactor;

        [UxmlAttribute]
        public bool assetMonitor { get; set; } = true;

        readonly List<GenerationTile> m_TilePool = new();

        string m_ElementID;
        bool m_PruneScheduled;
        bool m_HasActiveSkeletons;

        public GenerationSelector()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            this.SetupInfoIcon();

            // Set up the precache spinner and label defined in UXML
            m_PrecacheSpinner = this.Q<VisualElement>("precache-spinner");
            m_PrecacheSpinner.AddManipulator(m_SpinnerManipulator = new SpinnerManipulator());
            m_PrecacheLabel = this.Q<Label>("precache-label");

            m_GridView = this.Q<GridView>();

            m_GridView.BindTo<AnimationClipResult>(m_TilePool, () => true);
            m_GridView.MakeTileGrid(GetPreviewSize);
            this.UseAsset(SetAsset);
            this.Use(state => state.SelectPreviewSizeFactor(this), OnPreviewSizeChanged);
            this.Use(state => state.CalculateSelectorHash(this), OnGeneratedAnimationsChanged);

            this.Use(state => state.SelectSelectedGeneration(this), OnGenerationSelected);
            this.UseArray(state => state.SelectGenerationProgress(this), OnGenerationProgressChanged);
            this.UseContext<ScreenScaleFactor>(OnScreenScaleFactorChanged, false);

            RegisterCallback<GeometryChangedEvent>(_ => OnItemViewMaxCountChanged(this.GetTileGridMaxItemsInElement(GetPreviewSize())));
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                OnItemViewMaxCountChanged(0);
                this.RemoveManipulator(m_GenerationFileSystemWatcher);
                GenerationResultsActions.PrecachingStateChanged -= OnPrecachingStateChanged;
            });
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                this.AddManipulator(m_GenerationFileSystemWatcher);
                m_GenerationFileSystemWatcher?.SetActivePolling(m_HasActiveSkeletons);
                if (string.IsNullOrEmpty(m_ElementID))
                    m_ElementID = this.GetElementIdentifier().ToString();
                GenerationResultsActions.PrecachingStateChanged += OnPrecachingStateChanged;
            });

            m_OpenGeneratorsWindowButton = this.Q<Button>("open-generator-window");
            m_OpenGeneratorsWindowButton.clicked += () =>
            {
                var asset = this.GetAsset();
                if (!asset.IsValid())
                    return;
                AnimateGeneratorWindow.Display(asset.GetPath());
            };
        }

        void OnPrecachingStateChanged(AssetReference asset, bool isPrecaching)
        {
            // Only show spinner for our asset
            if (asset != this.GetAsset())
                return;

            m_PrecacheSpinner.SetShown(isPrecaching);
            m_PrecacheLabel.SetShown(isPrecaching);
            if (isPrecaching)
                m_SpinnerManipulator.Start();
            else
                m_SpinnerManipulator.Stop();
        }

        void OnScreenScaleFactorChanged(ScreenScaleFactor factor)
        {
            var tiles = this.Query<GenerationTile>();
            tiles.ForEach(tile => tile.OnScreenScaleFactorChanged(factor));
        }

        void OnGenerationProgressChanged(List<GenerationProgressData> data)
        {
            var tiles = this.Query<GenerationTile>();
            tiles.ForEach(tile => tile.SetGenerationProgress(data));
        }

        void OnGenerationSelected(AnimationClipResult result)
        {
            var tiles = this.Query<GenerationTile>();
            tiles.ForEach(tile => tile.SetSelectedGeneration(result));
        }

        void OnPreviewSizeChanged(float sizeFactor)
        {
            m_PreviewSizeFactor = sizeFactor;
            m_GridView.TileSizeChanged(GetPreviewSize());
            OnItemViewMaxCountChanged(this.GetTileGridMaxItemsInElement(GetPreviewSize()));
        }

        void OnItemViewMaxCountChanged(int count)
        {
            var asset = this.GetAsset();
            if (!asset.IsValid())
                return;
            this.Dispatch(GenerationResultsActions.setGeneratedResultVisibleCount,
                new(asset, m_ElementID, m_GridView.IsElementShown() ? count : 0));
        }

        void OnGeneratedAnimationsChanged(int _) => UpdateItems(this.GetState().SelectGeneratedAnimationsAndSkeletons(this));

        void UpdateItems(IEnumerable<AnimationClipResult> textures)
        {
            m_HasActiveSkeletons = textures.Any(result => result is AnimationClipSkeleton);
            m_GenerationFileSystemWatcher?.SetActivePolling(m_HasActiveSkeletons);

            ((BindingList<AnimationClipResult>)m_GridView.itemsSource).ReplaceRangeUnique(textures, result => result is AnimationClipSkeleton);
            m_GridView.Rebuild();

            var asset = this.GetAsset();
            if (!asset.IsValid())
                return;
            if (assetMonitor)
            {
                // Defer pruning to avoid nested dispatch issue: if pruneFulfilledSkeletons changes state
                // while we're inside OnGeneratedAnimationsChanged, the selector may not re-fire.
                if (!m_PruneScheduled)
                {
                    m_PruneScheduled = true;
                    EditorApplication.delayCall += () =>
                    {
                        m_PruneScheduled = false;
                        if (panel == null) return;
                        
                        var currentAsset = this.GetAsset();
                        if (currentAsset.IsValid() && currentAsset == asset)
                            this.Dispatch(Generators.UI.Actions.GenerationActions.pruneFulfilledSkeletons, new(currentAsset));
                    };
                }
            }
        }

        void SetAsset(AssetReference asset)
        {
            // Reset spinner state when switching assets
            m_PrecacheSpinner.SetShown(false);
            m_PrecacheLabel.SetShown(false);
            m_SpinnerManipulator.Stop();

            OnItemViewMaxCountChanged(this.GetTileGridMaxItemsInElement(GetPreviewSize()));

            this.RemoveManipulator(m_GenerationFileSystemWatcher);
            m_GenerationFileSystemWatcher = null;

            UpdateItems(this.GetState().SelectGeneratedAnimationsAndSkeletons(this));

            if (!asset.IsValid() || !assetMonitor)
                return;

            m_GenerationFileSystemWatcher = new GenerationFileSystemWatcher(asset,
                new[] { AssetUtils.poseAssetExtension, AssetUtils.defaultAssetExtension, AssetUtils.fbxAssetExtension },
                (files, isInitialLoad) =>
                {
                    if (this.SelectWindowSettingsDisablePrecaching())
                        this.Dispatch(GenerationResultsActions.setGeneratedAnimations,
                            new(asset, files.Select(AnimationClipResult.FromPath).ToList(), isInitialLoad));
                    else
                        this.GetStoreApi().Dispatch(GenerationResultsActions.setGeneratedAnimationsAsync,
                            new(asset, files.Select(AnimationClipResult.FromPath).ToList(), isInitialLoad));
                });
            this.AddManipulator(m_GenerationFileSystemWatcher);
            m_GenerationFileSystemWatcher?.SetActivePolling(m_HasActiveSkeletons);
        }
    }
}
