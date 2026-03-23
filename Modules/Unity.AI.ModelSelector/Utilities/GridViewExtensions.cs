using System;
using System.Collections.Generic;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.ModelSelector.Components;
using Unity.AI.ModelSelector.Services.Stores.States;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.ModelSelector.Services.Utilities
{
    static class GridViewExtensions
    {
        public static void BindToModels(this GridView gridView, List<ModelTile> itemPool, List<ModelSettings> models, Func<ModelSettings, bool> isModelBroken = null,
            Action<ModelSettings> onShowModelDetails = null, string selectedModelId = null)
        {
            gridView.selectionType = SelectionType.Single;
            gridView.makeItem = () => new ModelTileItem();
            gridView.bindItem = (element, i) =>
            {
                if (element is not ModelTileItem tileItem)
                    return;
                var model = gridView.itemsSource[i] as ModelSettings;
                if (itemPool.Count > 0)
                {
                    var found = itemPool.FindIndex(item => model != null && item.model != null && item.model.id == model.id);
                    if (found < 0)
                        found = itemPool.Count - 1;
                    tileItem.tile = itemPool[found];
                    itemPool.RemoveAt(found);
                }
                else
                {
                    tileItem.tile = new ModelTile();
                    if (onShowModelDetails != null)
                        tileItem.tile.showModelDetails += onShowModelDetails;
                }
                tileItem.tile.SetModel(model);
                var isDisabled = isModelBroken != null && isModelBroken(model);
                tileItem.tile.SetEnabled(!isDisabled);
                tileItem.tile.tooltip = !tileItem.tile.enabledSelf ? "Temporarily not available." : null;
            };
            gridView.unbindItem = (element, i) =>
            {
                if (element is not ModelTileItem { tile: not null } tileItem)
                    return;
                tileItem.tile.SetEnabled(true);
                itemPool.Add(tileItem.tile);
                tileItem.tile = null;
            };
            gridView.itemsSource = models;

            // Set initial selection if needed
            if (selectedModelId != null && models.Count > 0)
            {
                var selectedIndex = models.FindIndex(m => m.id == selectedModelId);
                if (selectedIndex >= 0)
                {
                    gridView.SetSelectionWithoutNotify(new[] { selectedIndex });
                }
            }
        }

        public static void BindToModelDetails(this GridView gridView, List<string> thumbnails)
        {
            gridView.MakeTileGrid(() => (float)TextureSizeHint.Carousel);
            gridView.selectionType = SelectionType.None;
            gridView.makeItem = () => new Image();
            gridView.bindItem = async (element, i) =>
            {
                if (element is not Image image)
                    return;
                image.image = await TextureCache.GetPreview(
                    new Uri(thumbnails[i]),
                    (int)TextureSizeHint.Carousel);
            };
            gridView.unbindItem = (element, _) =>
            {
                if (element is not Image image)
                    return;
                image.image = null;
            };
            gridView.itemsSource = thumbnails;
        }
    }
}
