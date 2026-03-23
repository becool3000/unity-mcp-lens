using System;
using System.Collections.Generic;
using Unity.AI.Generators.IO.Utilities;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Generators.UI.Utilities
{
    static class SkeletonExtensions
    {
        internal const string skeletonUriPath = "file:///skeletons";
    }

    static class SkeletonRenderingUtils
    {
        static Material s_ProgressDiskMaterial;

        static readonly Dictionary<Tuple<int, int, float>, RenderTexture> k_Cache = new();

        static SkeletonRenderingUtils()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        static void OnBeforeAssemblyReload()
        {
            foreach (var rt in k_Cache.Values)
            {
                if (rt)
                {
                    rt.Release();
                    rt.SafeDestroy();
                }
            }
            k_Cache.Clear();

            if (s_ProgressDiskMaterial)
                s_ProgressDiskMaterial.SafeDestroy();
        }

        public static RenderTexture GetCached(float progress, int width, int height, float screenScaleFactor)
        {
            var texWidth = Mathf.RoundToInt(width * screenScaleFactor);
            var texHeight = Mathf.RoundToInt(height * screenScaleFactor);

            texWidth = Mathf.NextPowerOfTwo(Mathf.Clamp(texWidth, 128, 8191));
            texHeight = Mathf.NextPowerOfTwo(Mathf.Clamp(texHeight, 128, 8191));

            var bucketedProgress = progress <= 0 ? 0 : Mathf.Clamp(Mathf.Round(progress / 0.05f) * 0.05f, 0.05f, 1f);

            var key = Tuple.Create(texWidth, texHeight, bucketedProgress);

            if (k_Cache.TryGetValue(key, out var rt) && rt && rt.IsCreated())
                return rt;

            if (rt) // Not created or invalid
            {
                k_Cache.Remove(key);
                rt.Release();
            }

            rt = new RenderTexture(texWidth, texHeight, 0) { hideFlags = HideFlags.HideAndDontSave };
            rt.Create();
            k_Cache[key] = rt;

            if (!s_ProgressDiskMaterial)
                s_ProgressDiskMaterial = new Material(Shader.Find("Hidden/AIToolkit/ProgressDisk")) { hideFlags = HideFlags.HideAndDontSave };

            var previousRT = RenderTexture.active;
            try
            {
                s_ProgressDiskMaterial.SetFloat("_Value", bucketedProgress);
                Graphics.Blit(null, rt, s_ProgressDiskMaterial);
            }
            finally
            {
                RenderTexture.active = previousRT;
            }

            return rt;
        }
    }

    record FulfilledSkeleton(int progressTaskID, string resultUri);
}
