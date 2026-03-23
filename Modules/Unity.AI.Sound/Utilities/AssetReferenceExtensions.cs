using System;
using System.Threading.Tasks;
using Unity.AI.Sound.Services.Stores.States;
using Unity.AI.Generators.Asset;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Sound.Services.Utilities
{
    static class AssetReferenceExtensions
    {
        public static async Task<bool> Replace(this AssetReference asset, AudioClipResult generatedAudioClip)
        {
            if (await generatedAudioClip.CopyToAsync(asset.GetPath()))
            {
                asset.EnableGenerationLabel();
                return true;
            }

            return false;
        }

        public static Task<bool> IsBlank(this AssetReference asset) => Task.FromResult(asset.GetObject() is AudioClip { length: < 0.05f });

        public static AudioClipResult ToResult(this AssetReference asset) => AudioClipResult.FromPath(asset.GetPath());

        public static async Task<bool> SaveToGeneratedAssets(this AssetReference asset)
        {
            try
            {
                await asset.ToResult().CopyToProject(new GenerationSetting().MakeMetadata(asset), asset.GetGeneratedAssetsPath());
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
