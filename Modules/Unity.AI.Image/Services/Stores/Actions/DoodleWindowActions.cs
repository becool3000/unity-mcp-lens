using System.Collections.Generic;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Image.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Redux;
using Unity.AI.Toolkit.Asset;
using UnityEngine;

namespace Unity.AI.Image.Services.Stores.Actions
{
    static class DoodleWindowActions
    {
        public const string slice = "doodle";

        public static readonly Creator<DoodleWindowState> init = new($"{slice}/init");
        public static readonly Creator<(int index, byte[] data)> setLayer = new($"{slice}/setLayer");
        public static readonly Creator<DoodleTool> setTool = new($"{slice}/setTool");
        public static readonly Creator<float> setBrushSize = new($"{slice}/setBrushSize");
        public static readonly Creator<Vector2Int> setSize = new($"{slice}/setSize");
        public static readonly Creator<ImageReferenceType> setImageReferenceType = new($"{slice}/setImageReferenceType");
        public static readonly Creator<AssetReference> setAssetReference = new($"{slice}/setAssetReference");
        public static readonly Creator<IEnumerable<DoodleLayer>> setLayers = new($"{slice}/setLayers");
        public static readonly Creator<bool> setShowBaseImage = new($"{slice}/setShowBaseImage");
        public static readonly Creator<float> setBaseImageOpacity = new($"{slice}/setBaseImageOpacity");
    }
}
