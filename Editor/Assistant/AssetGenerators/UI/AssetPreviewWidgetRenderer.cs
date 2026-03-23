using Unity.AI.Assistant.Editor.Acp;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor
{
    /// <summary>
    /// Renders asset preview widgets for ACP tool calls.
    /// Handles the "unity://widget/asset_preview" resource URI.
    /// </summary>
    [AcpWidgetRenderer("unity://widget/asset_preview")]
    class AssetPreviewWidgetRenderer : IAcpWidgetRenderer
    {
        public VisualElement TryRender(UiMetadata ui)
        {
            if (ui?.Context == null)
                return null;

            // Extract asset GUID from context
            var assetGuid = ui.Context["assetGuid"]?.ToString();
            if (string.IsNullOrEmpty(assetGuid))
                return null;

            // Load the asset
            var assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogWarning($"[AssetPreviewWidgetRenderer] Could not find asset path for GUID: {assetGuid}");
                return null;
            }

            var assetObject = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (assetObject == null)
            {
                Debug.LogWarning($"[AssetPreviewWidgetRenderer] Could not load asset at path: {assetPath}");
                return null;
            }

            return AssetPreviewFactory.CreatePreview(assetObject);
        }
    }
}
