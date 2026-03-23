using System;
using System.Threading.Tasks;
using Unity.AI.Animate.Services.Stores.States;
using Unity.AI.Generators.Asset;
using Unity.AI.Toolkit.Asset;
using UnityEngine;

namespace Unity.AI.Animate.Services.Utilities
{
    static class AssetReferenceExtensions
    {
        public static async Task<bool> ReplaceAsync(this AssetReference asset, AnimationClipResult generatedAnimationClip)
        {
            if (await generatedAnimationClip.CopyToAsync(asset))
            {
                asset.EnableGenerationLabel();
                return true;
            }

            return false;
        }

        public static bool Replace(this AssetReference asset, AnimationClipResult generatedAnimationClip)
        {
            if (generatedAnimationClip.CopyTo(asset))
            {
                asset.EnableGenerationLabel();
                return true;
            }

            return false;
        }

        public static Task<bool> IsBlank(this AssetReference asset) => Task.FromResult(asset.GetObject<AnimationClip>().IsBlank());

        public static AnimationClipResult ToResult(this AssetReference asset) => AnimationClipResult.FromPath(asset.GetPath());

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
