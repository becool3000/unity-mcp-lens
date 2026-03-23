using System;
using System.Collections.Generic;
using Unity.AI.Image.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Toolkit.Asset;
using UnityEngine;

namespace Unity.AI.Image.Services.Stores.States
{
    enum DoodleTool
    {
        Brush,
        Eraser,
        Fill,

        None = 255,
    }

    [Serializable]
    record DoodleLayer
    {
        public byte[] data = Array.Empty<byte>();
    }

    [Serializable]
    record DoodleWindowState
    {
        public ImageReferenceType imageReferenceType;
        public AssetReference assetReference = new();
        public DoodleTool doodleTool = DoodleTool.None;
        public float brushSize = 10f;
        public List<DoodleLayer> layers = new();
        public Vector2Int size = new(256, 256);
        public float baseImageOpacity = 0.1f;
        public bool showBaseImage;
    }

    static class DoodleWindowStateExtensions
    {
        public static DoodleLayer EnsureLayer(this DoodleWindowState state, int layerIndex)
        {
            var documentSize = state.size;
            while (state.layers.Count <= layerIndex)
            {
                var tex = new Texture2D(documentSize.x, documentSize.y);
                state.layers.Add(new DoodleLayer { data = tex.EncodeToPNG() });
            }
            return state.layers[layerIndex];
        }
    }

    static class DoodleLayerExtensions
    {
        public static void SetData(this DoodleLayer layer, byte[] data) => layer.data = data;
    }
}
