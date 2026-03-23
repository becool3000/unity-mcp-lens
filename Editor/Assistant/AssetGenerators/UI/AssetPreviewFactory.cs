using Unity.AI.Assistant.Editor.Backend.Socket.Tools;
using Unity.AI.Generators.Tools;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor
{
    /// <summary>
    /// Factory for creating asset preview elements with the appropriate GenerationSelector.
    /// Shared by both local function call renderers and ACP widget renderers.
    /// </summary>
    static class AssetPreviewFactory
    {
        const string k_UxmlPath = "Packages/com.unity.ai.assistant/Editor/Assistant/AssetGenerators/UI/GenerationPreviewElement.uxml";

        /// <summary>
        /// Creates an asset preview element with the appropriate GenerationSelector based on asset type.
        /// </summary>
        /// <param name="assetObject">The Unity asset to create a preview for.</param>
        /// <returns>A VisualElement containing the preview, or null if creation failed.</returns>
        public static VisualElement CreatePreview(Object assetObject)
        {
            if (assetObject == null)
                return null;

            var assetType = Constants.GetAssetType(assetObject.GetType());
            var preview = PreviewElementFactory.Create(null, assetObject, k_UxmlPath);

            switch (assetType)
            {
                case AssetTypes.HumanoidAnimation:
                    ConfigureSelector(preview, "animation-selector", ve => ve.SetAnimateContext(assetObject));
                    break;
                case AssetTypes.Cubemap:
                case AssetTypes.Image:
                case AssetTypes.Spritesheet:
                case AssetTypes.Sprite:
                    ConfigureSelector(preview, "image-selector", ve => ve.SetImageContext(assetObject));
                    break;
                case AssetTypes.Material:
                case AssetTypes.TerrainLayer:
                    ConfigureSelector(preview, "material-selector", ve => ve.SetMaterialContext(assetObject));
                    break;
                case AssetTypes.Mesh:
                    ConfigureSelector(preview, "mesh-selector", ve => ve.SetMeshContext(assetObject));
                    break;
                case AssetTypes.Sound:
                    ConfigureSelector(preview, "sound-selector", ve => ve.SetSoundContext(assetObject));
                    break;
                case AssetTypes.SpriteAnimation:
                case AssetTypes.AnimatorController:
                    // Not yet implemented
                    break;
            }

            return preview;
        }

        static void ConfigureSelector(VisualElement preview, string selectorName, System.Action<VisualElement> configureContext)
        {
            var selector = preview.Q<VisualElement>(selectorName);
            if (selector != null)
            {
                selector.style.display = DisplayStyle.Flex;
                configureContext(selector);
            }
        }
    }
}
