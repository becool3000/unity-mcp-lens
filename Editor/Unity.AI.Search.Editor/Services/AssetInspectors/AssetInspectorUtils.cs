using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Unity.AI.Assistant.Editor.Utils;
using Object = UnityEngine.Object;

namespace Unity.AI.Search.Editor
{
    static class AssetInspectorUtils
    {
        static Texture2D Resized(this Texture2D texture) =>
            TextureUtils.ResizeTextureSinglePass(texture,
                AssetInspectors.k_DefaultPreviewWidth,
                AssetInspectors.k_DefaultPreviewHeight,
                RenderTextureReadWrite.Default);

        public static async Task<Texture2D> GetPreview(Object asset)
        {
            var preview = AssetPreview.GetAssetPreview(asset);
            if (preview == null)
            {
                while (AssetPreview.IsLoadingAssetPreview(asset.GetInstanceID()))
                    await Task.Yield();
                preview = AssetPreview.GetAssetPreview(asset) ?? AssetPreview.GetMiniThumbnail(asset);
            }
            return Resized(preview);
        }

        public static async Task<Texture2D> GetPreviewFromTexture(Texture texture)
        {
            // For texture materials, the results can vary widely in terms of appearance if using only the texture.
            // For instance, a cubemap will just look like a grey square.
            // So we use the AssetPreview instead, which is more consistent.
            if (texture is Texture2D texture2d)
                return Resized(texture2d);

            return await GetPreview(texture);
        }
    }
}
