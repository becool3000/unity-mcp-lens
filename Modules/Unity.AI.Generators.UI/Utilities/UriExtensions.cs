using System;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEngine;

namespace Unity.AI.Generators.UI.Utilities
{
    static class UriExtensions
    {
        public static GeneratedAssetMetadata GetGenerationMetadata(Uri resultUri)
        {
            var data = new GeneratedAssetMetadata();
            try { data = JsonUtility.FromJson<GeneratedAssetMetadata>(FileIO.ReadAllText($"{resultUri.GetLocalPath()}.json")); }
            catch { /*Debug.LogWarning($"Could not read {animationClipResult.uri.GetLocalPath()}.json");*/ }
            return data;
        }
    }
}
