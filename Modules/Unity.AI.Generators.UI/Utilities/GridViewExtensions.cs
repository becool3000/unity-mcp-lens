using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.UI.Utilities
{
    static class GridViewExtensions
    {
        static readonly HashSet<GridView> k_EquirectGridViews = new();
        static readonly HashSet<GridView> k_RegisteredGridViews = new();

        // Define the single-side padding (2px) of each individual tile. This likely must match the border-width of the GenerationTile USS for cubemaps (equirects).
        const float k_EquirectTileLikelyUssPadding = 2f;

        public static void MakeTileGrid(this GridView gridView, Func<float> preferredSize) =>
            MakeGrid(gridView, preferredSize, false);

        public static void TileSizeChanged(this GridView gridView, float preferredSize) =>
            TileGridGeometryChanged(new GeometryChangedEvent { target = gridView }, preferredSize);

        public static void MakeEquirectTileGrid(this GridView gridView, Func<float> preferredSize) =>
            MakeGrid(gridView, preferredSize, true);

        static void MakeGrid(GridView gridView, Func<float> preferredSize, bool isEquirect)
        {
            if (isEquirect)
                k_EquirectGridViews.Add(gridView);
            else
                k_EquirectGridViews.Remove(gridView);

            var width = Mathf.NextPowerOfTwo((int)TextureSizeHint.Generation);
            gridView.fixedItemWidth = width;
            gridView.fixedItemHeight = isEquirect ? (width / 2f) + k_EquirectTileLikelyUssPadding : width;

            if (!k_RegisteredGridViews.Add(gridView))
                return;

            var contentContainer = gridView.Q<VisualElement>("unity-content-container");
            if (contentContainer != null)
                contentContainer.RegisterCallback<GeometryChangedEvent>(_ =>
                    TileGridGeometryChanged(new GeometryChangedEvent { target = gridView }, preferredSize()));
            else
                gridView.RegisterCallback<GeometryChangedEvent>(evt => TileGridGeometryChanged(evt, preferredSize()));
        }

        static void TileGridGeometryChanged(GeometryChangedEvent evt, float preferredSize)
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

            // Decide how many items should fit horizontally based on the preferred size.
            var horizontalItemCount = Mathf.Max(1, Mathf.FloorToInt(width / preferredSize));
            var newFixedWidth = Mathf.FloorToInt(width / horizontalItemCount);
            var isEquirect = k_EquirectGridViews.Contains(gridView);
            var newFixedHeight = isEquirect ? (newFixedWidth / 2f) + k_EquirectTileLikelyUssPadding : newFixedWidth;

            // Only rebuild if the new fixed size differs from the stored value by 1 or more.
            if (Mathf.Abs(gridView.fixedItemWidth - newFixedWidth) < 1 && Mathf.Abs(gridView.fixedItemHeight - newFixedHeight) < 1)
                return;

            // Store the new value and update the grid.
            gridView.Rebuild(newFixedWidth, newFixedHeight);
        }

        public static int GetTileGridMaxItemsInElement(this VisualElement element, float preferredSize)
        {
            var width = element.layout.width;
            var height = element.layout.height;
            if (float.IsNaN(width) || float.IsNaN(height))
                return 0;

            // Calculate the number of columns (tiles that fit horizontally)
            var horizontalItemCount = Mathf.Max(1, Mathf.FloorToInt(width / preferredSize));
            // Derive the actual tile size based on the computed column count.
            var tileWidth = Mathf.FloorToInt(width / horizontalItemCount);
            if (tileWidth == 0)
                return 0;

            var gridView = element as GridView;
            var isEquirect = gridView != null && k_EquirectGridViews.Contains(gridView);
            var tileHeight = isEquirect ? (tileWidth / 2f) + k_EquirectTileLikelyUssPadding : tileWidth;

            if (tileHeight <= 0)
                return 0;

            // Calculate the number of rows required to cover the viewport.
            // Use CeilToInt to count partially visible rows.
            var verticalItemCount = Mathf.CeilToInt(height / tileHeight);

            return horizontalItemCount * verticalItemCount;
        }
    }
}
