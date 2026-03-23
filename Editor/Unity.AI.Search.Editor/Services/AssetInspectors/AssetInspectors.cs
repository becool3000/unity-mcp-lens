using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Utils;
using Unity.AI.Search.Editor.Knowledge;
using Unity.AI.Search.Editor.Services.Models;
using Unity.AI.Search.Editor.Utilities;
using UnityEditor;
using UnityEngine;
using PreviewRenderUtility = Unity.AI.Search.Editor.Utilities.PreviewRenderUtility;

namespace Unity.AI.Search.Editor
{
    static class AssetInspectors
    {
        internal static readonly int k_DefaultPreviewWidth = SigLip2.ModelInfo.size;
        internal static readonly int k_DefaultPreviewHeight = SigLip2.ModelInfo.size;

        // Limit concurrent preview rendering to avoid "Unable to allocate new scene culling mask" errors
        static readonly int k_MaxConcurrentPreviews = 3;

        static readonly SemaphoreSlim k_PreviewConcurrencyLimiter =
            new SemaphoreSlim(k_MaxConcurrentPreviews, k_MaxConcurrentPreviews);

        static readonly Queue<Func<Task>> m_Queue = new Queue<Func<Task>>();

        static bool m_IsRunning;
        public static int PendingCount => m_Queue.Count;
        public static event Action<int> PendingCountChanged;

        static AssetInspectors()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        public static Task<AssetObservation> ForTexture(Texture asset) =>
            Enqueue(() => ForTextureInternal(asset));

        static async Task<AssetObservation> ForTextureInternal(Texture asset)
        {
            var path = AssetDatabase.GetAssetPath(asset);
            return new AssetObservation
            {
                assetGuid = AssetDatabase.AssetPathToGUID(path),
                previews = new[] { await AssetInspectorUtils.GetPreviewFromTexture(asset) }
            };
        }

        public static Task<AssetObservation> ForMaterial(Material asset) =>
            Enqueue(() => ForMaterialInternal(asset));

        static async Task<AssetObservation> ForMaterialInternal(Material asset)
        {
            var path = AssetDatabase.GetAssetPath(asset);
            var preview = await AssetInspectorUtils.GetPreview(asset);
            return new AssetObservation
            {
                assetGuid = AssetDatabase.AssetPathToGUID(path),
                previews = new[] { preview }
            };
        }

        public static Task<AssetObservation> ForGameObjectViews(GameObject gameObject,
            GameObjectPreviewOptions options = null)
        {
            return Enqueue(() => ForGameObjectViewsInternal(gameObject, options));
        }

        static async Task<AssetObservation> ForGameObjectViewsInternal(
            GameObject gameObject,
            GameObjectPreviewOptions options = null)
        {
            var path = AssetDatabase.GetAssetPath(gameObject);
            var assetGuid = AssetDatabase.AssetPathToGUID(path);

            // Wait for semaphore to limit concurrent preview rendering
            await k_PreviewConcurrencyLimiter.WaitAsync();

            try
            {
                options ??= new GameObjectPreviewOptions();
                using var util = new PreviewRenderUtility();
                var views = new List<Texture2D>();

                // Profile default preview generation
                var defaultPreview = await AssetInspectorUtils.GetPreview(gameObject);
                views.Add(defaultPreview);

                // Profile multiple angle views generation
                var multiViews = await Task.FromResult(util.RenderViews(gameObject, options));
                views.AddRange(multiViews);

                return new AssetObservation
                {
                    assetGuid = assetGuid,
                    previews = views.ToArray(),
                };
            }
            finally
            {
                k_PreviewConcurrencyLimiter.Release();
            }
        }

        static Task<T> Enqueue<T>(Func<Task<T>> work)
        {
            if (!AssetKnowledgeSettings.RunAsync)
                return work();

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_Queue.Enqueue(async () =>
            {
                try
                {
                    tcs.TrySetResult(await work());
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            PendingCountChanged?.Invoke(m_Queue.Count);
            return tcs.Task;
        }

        static async void OnEditorUpdate()
        {
            try
            {
                if (m_IsRunning || m_Queue.Count == 0)
                    return;

                m_IsRunning = true;

                if (m_Queue.Count > 0)
                {
                    var next = m_Queue.Dequeue();
                    PendingCountChanged?.Invoke(m_Queue.Count);
                    await next();
                }
            }
            catch (Exception ex)
            {
                InternalLog.LogException(ex, LogFilter.Search);
            }
            finally
            {
                m_IsRunning = false;
            }
        }
    }
}