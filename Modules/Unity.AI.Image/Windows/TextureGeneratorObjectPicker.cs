using Unity.AI.Image.Services.Utilities;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Image.Windows
{
    static class TextureGeneratorObjectPicker
    {
        [InitializeOnLoadMethod]
        static void ObjectPickerBlankGenerationHook()
        {
            Toolkit.GenerationObjectPicker.RegisterTemplate<Texture2D>(
                "Texture2D",
                AssetUtils.CreateBlankTexture,
                $"Assets/{AssetUtils.defaultNewAssetName}.png",
                TextureGeneratorInspectorButton.OpenGenerationWindow
            );

            Toolkit.GenerationObjectPicker.RegisterTemplate<Sprite>(
                "Sprite",
                AssetUtils.CreateBlankSprite,
                $"Assets/{AssetUtils.defaultNewAssetNameSprite}.png",
                TextureGeneratorInspectorButton.OpenGenerationWindow
            );

            Toolkit.GenerationObjectPicker.RegisterTemplate<Cubemap>(
                "Cubemap",
                AssetUtils.CreateBlankCubemap,
                $"Assets/{AssetUtils.defaultNewAssetNameCube}.png",
                TextureGeneratorInspectorButton.OpenGenerationWindow
            );

            Toolkit.GenerationObjectPicker.RegisterTemplate<Material>(
                "SkyboxMaterial",
                AssetUtils.CreateBlankSkyboxMaterial,
                $"Assets/New Skybox Material.mat",
                OpenSkyboxGenerationWindow,
                Toolkit.GenerationObjectPicker.TemplateFlags.SkyboxOnly
            );
        }

        static void OpenSkyboxGenerationWindow(string materialPath)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            var cubemap = AssetUtils.GetSkyboxMaterialCubemap(material);
            if (cubemap != null)
            {
                var cubemapPath = AssetDatabase.GetAssetPath(cubemap);
                TextureGeneratorInspectorButton.OpenGenerationWindow(cubemapPath);
            }
        }
    }
}
