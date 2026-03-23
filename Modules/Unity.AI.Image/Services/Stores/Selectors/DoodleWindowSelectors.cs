using System.Collections.Generic;
using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Image.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Redux;
using Unity.AI.Toolkit.Asset;
using UnityEngine;

namespace Unity.AI.Image.Services.Stores.Selectors
{
    static class DoodleWindowSelectors
    {
        public static DoodleWindowState SelectDoodleAppData(this IState state) => state.SelectState() with { };

        public static DoodleWindowState SelectState(this IState state) =>
            state.Get<DoodleWindowState>(DoodleWindowActions.slice);

        public static DoodleTool SelectDoodleTool(this IState  state) => state.SelectState().doodleTool;

        public static Vector2Int SelectDoodleSize(this IState state) => state.SelectState().size;

        public static float SelectBrushSize(this IState state) => state.SelectState().brushSize;

        public static DoodleLayer SelectDoodleLayer(this IState state, int index)
        {
            var layers = state.SelectState().layers;
            return layers.Count <= index ? null : layers[index];
        }

        public static byte[] SelectDoodleLayerData(this IState state, int index) => state.SelectDoodleLayer(0)?.data;

        public static byte[] SelectMergedDoodleLayers(this IState state) => state.SelectDoodleLayer(0)?.data;

        public static ImageReferenceType SelectImageReferenceType(this IState state) => state.SelectState().imageReferenceType;

        public static AssetReference SelectAssetReference(this IState state) => state.SelectState().assetReference;

        public static List<DoodleLayer> SelectDoodleLayers(this IState state) => state.SelectState().layers;

        public static bool SelectShowBaseImage(this IState state) => state.SelectState().showBaseImage;

        public static float SelectBaseImageOpacity(this IState state) => state.SelectState().baseImageOpacity;
    }
}
