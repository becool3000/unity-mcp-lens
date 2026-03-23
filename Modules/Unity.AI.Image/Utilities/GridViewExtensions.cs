using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Unity.AI.Image.Components;
using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Selectors;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Services.Utilities
{
    static class GridViewExtensions
    {
        public static void BindTo<T>(this GridView gridView, List<GenerationTile> itemPool, Func<bool> replaceAssetOnSelect = null) where T: TextureResult
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
                    var found = itemPool.FindIndex(item => item.textureResult == result);
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
                    textures[values[0]] is TextureSkeleton)
                    return;
                var replaceAsset = replaceAssetOnSelect?.Invoke() ?? false;
                if (!replaceAsset)
                    replaceAsset = gridView.SelectWindowSettingsReplaceAssetOnSelect();
                await gridView.Dispatch(GenerationResultsActions.selectGeneration, new(gridView.GetAsset(), textures[values[0]], replaceAsset, true));
            };
            gridView.itemsSource = new BindingList<T>();
        }
    }
}
