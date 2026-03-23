using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Unity.AI.Sound.Components;
using Unity.AI.Sound.Services.Stores.Actions;
using Unity.AI.Sound.Services.Stores.States;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Sound.Services.Utilities
{
    static class GridViewExtensions
    {
        public static void BindTo<T>(this GridView gridView, List<GenerationTile> itemPool, Func<bool> replaceAssetOnSelect = null) where T: AudioClipResult
        {
            gridView.selectionType = SelectionType.Single;
            gridView.selectOnPointerUp = true; // for drag and drop
            gridView.makeItem = () => new GenerationTileItem();
            gridView.bindItem = (element, i) =>
            {
                if (element is not GenerationTileItem tileItem)
                    return;

                var result = ((BindingList<T>)gridView.itemsSource)[i];
                if (itemPool.Count > 0)
                {
                    var found = itemPool.FindIndex(item => item.audioClipResult == result);
                    if (found < 0)
                        found = itemPool.Count - 1;
                    tileItem.tile = itemPool[found];
                    itemPool.RemoveAt(found);
                }
                else
                    tileItem.tile = new GenerationTile();

                tileItem.tile.SetGeneration(result);
                gridView.MarkDirtyRepaint(); // bind does not await so image might not have been valid in previous calls
            };
            gridView.unbindItem = (element, i) =>
            {
                if (element is not GenerationTileItem { tile: not null } tileItem)
                    return;
                itemPool.Add(tileItem.tile);
                tileItem.tile = null;
            };
            gridView.selectedIndicesChanged += async indexes =>
            {
                var textures = (BindingList<T>)gridView.itemsSource;
                var values = indexes.ToList();
                if (gridView.GetAsset() == null || values.Count <= 0 || textures.Count <= values[0] || textures[values[0]] == null ||
                    textures[values[0]] is AudioClipSkeleton)
                    return;
                var replaceAsset = replaceAssetOnSelect?.Invoke() ?? false;
                await gridView.Dispatch(GenerationResultsActions.selectGeneration, new(gridView.GetAsset(), textures[values[0]], replaceAsset, true));
            };
            gridView.itemsSource = new BindingList<T>();
        }

        public static void MakeOscillogramGrid(this GridView gridView, Func<float> horizontalItemCount)
        {
            var contentContainer = gridView.Q<VisualElement>("unity-content-container");
            if (contentContainer != null)
                contentContainer.RegisterCallback<GeometryChangedEvent>(_ => OscillogramGridGeometryChanged(new GeometryChangedEvent { target = gridView }, horizontalItemCount()));
            else
                gridView.RegisterCallback<GeometryChangedEvent>(evt => OscillogramGridGeometryChanged(evt, horizontalItemCount()));
        }

        public static void OscillogramTileSizeChanged(this GridView gridView, float horizontalItemCount) => OscillogramGridGeometryChanged(new GeometryChangedEvent { target = gridView }, horizontalItemCount);

        // Minimum width (in pixels) allowed for each grid item.
        const int k_MinItemWidth = 80;

        static void OscillogramGridGeometryChanged(GeometryChangedEvent evt, float horizontalItemCount)
        {
            var gridView = (GridView)evt.target;
            if (gridView.resolvedStyle.display == DisplayStyle.None)
                return;

            var scrollView = gridView.Q<ScrollView>();
            if (scrollView == null)
                return;

            var width = scrollView.contentViewport.layout.width;
            if (float.IsNaN(width))
                return;

            // Use the provided horizontal item count, rounded to a whole number.
            var computedCount = Mathf.RoundToInt(horizontalItemCount);
            // Determine a candidate fixed width.
            var newFixedWidth = Mathf.FloorToInt(width / computedCount);

            // If the computed width is less than the minimum,
            // recalculate the horizontal item count based on the minimum allowed width.
            if (newFixedWidth < k_MinItemWidth)
            {
                computedCount = Mathf.Max(1, Mathf.FloorToInt(width / k_MinItemWidth));
                newFixedWidth = Mathf.FloorToInt(width / computedCount);
            }

            // Only rebuild if the new fixed size differs from the stored value by 1 or more.
            if (Mathf.Abs(gridView.fixedItemWidth - newFixedWidth) < 1)
                return;

            // Update the grid.
            gridView.Rebuild(newFixedWidth, gridView.fixedItemHeight);
        }

        public static int GetOscillogramGridMaxItemsInElement(this VisualElement element, float fixedItemHeight, float horizontalItemCount)
        {
            var width = element.layout.width;
            var height = element.layout.height;
            if (float.IsNaN(width) || float.IsNaN(height))
                return 0;

            var itemHeight = Mathf.FloorToInt(fixedItemHeight);
            // Calculate the number of rows required to cover the viewport.
            // Use CeilToInt to count partially visible rows.
            var verticalItemCount = Mathf.CeilToInt(height / itemHeight);

            // Use the provided horizontal item count, rounded to a whole number.
            var computedCount = Mathf.RoundToInt(horizontalItemCount);
            return computedCount * verticalItemCount;
        }
    }
}
