using System;
using System.ComponentModel;
using Unity.AI.Generators.Contexts;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Toolkit.Asset;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.Asset
{
    static class VisualElementExtensions
    {
        const string k_SettingsSlice = "generationSettings";
        internal static Redux.Creator<AssetReference> initializeAsset => new($"{k_SettingsSlice}/{nameof(initializeAsset)}");

        public static void SetAssetContext(this VisualElement ve, AssetReference value)
        {
            ve?.ProvideContext(AssetContextExtensions.assetKey, value);
            ve?.SetStoreApi(EditorWindowExtensions.AssetContextMiddleware(value));
            ve?.Dispatch(initializeAsset, value);
        }

        internal static AssetReference GetAssetContext(this VisualElement ve) => ve?.GetContext<AssetReference>(AssetContextExtensions.assetKey);
    }
}
