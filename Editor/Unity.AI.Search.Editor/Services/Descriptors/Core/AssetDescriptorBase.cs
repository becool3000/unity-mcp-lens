using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Utils;
using Unity.AI.Search.Editor.Embeddings;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Search.Editor.Knowledge
{
    abstract class AssetDescriptorBase<T> : AssetDescriptor where T : Object
    {
        static readonly HashSet<AssetObservation> s_Observations = new HashSet<AssetObservation>();

        public override async Task<AssetObservation> ProcessAsync(Object assetObject,
            CancellationToken cancellationToken)
        {
            if (assetObject is not T assetAsT)
                throw new InvalidOperationException($"The asset {assetObject} is not a {typeof(T)}.");

            if (AssetKnowledgeSettings.RunAsync)
                await Task.Yield();

            if (cancellationToken.IsCancellationRequested)
                return null;

            var result = await DoProcessAsync(assetAsT, cancellationToken);

            if (!RetainPreviews)
            {
                result?.Dispose();
            }

            return result;
        }

        protected abstract Task<AssetObservation> DoProcessAsync(T assetObject, CancellationToken cancellationToken);

        protected class EmbeddingResultWithObservation<TEmbedding, TAssetObservationType>
            where TAssetObservationType : AssetObservation
        {
            public TEmbedding EmbeddingResult;
            public TAssetObservationType Observation;

            public EmbeddingResultWithObservation(TEmbedding embeddingResult, TAssetObservationType observation)
            {
                EmbeddingResult = embeddingResult;
                Observation = observation;
            }
        }

        protected async Task<EmbeddingResultWithObservation<TAssetEmbeddingType, TAssetObservationType>> GetEmbedding<
            TAssetEmbeddingType, TAssetObservationType>(
            Func<T, Task<TAssetObservationType>> assetInspector,
            EmbeddingProviderDelegate<TAssetEmbeddingType, TAssetObservationType> embeddingAction,
            T asset,
            CancellationToken cancellationToken)
            where TAssetEmbeddingType : AssetEmbedding
            where TAssetObservationType : AssetObservation
        {
            return await Process(assetInspector,
                async obs =>
                {
                    var embeddingResult = await embeddingAction(obs);

                    if (cancellationToken.IsCancellationRequested)
                        return null;

                    embeddingResult.version = Version;

                    return new EmbeddingResultWithObservation
                        <TAssetEmbeddingType, TAssetObservationType>(
                            embeddingResult,
                            obs);
                }, asset, cancellationToken);
        }

        protected async Task<EmbeddingResultWithObservation<TAssetEmbeddingType[], TAssetObservationType>>
            GetEmbedding<TAssetEmbeddingType, TAssetObservationType>(
                Func<T, Task<TAssetObservationType>> assetInspector,
                EmbeddingProviderDelegate<TAssetEmbeddingType[], TAssetObservationType> embeddingAction,
                T asset,
                CancellationToken cancellationToken)
            where TAssetEmbeddingType : AssetEmbedding
            where TAssetObservationType : AssetObservation
        {
            return await Process(assetInspector,
                async obs =>
                {
                    var embeddingResults = await embeddingAction(obs);

                    if (cancellationToken.IsCancellationRequested)
                        return null;

                    foreach (var embeddingResult in embeddingResults)
                    {
                        embeddingResult.version = Version;
                    }

                    return new EmbeddingResultWithObservation<TAssetEmbeddingType[], TAssetObservationType>(
                        embeddingResults, obs);
                }, asset, cancellationToken);
        }

        async Task<EmbeddingResultWithObservation<TU, TAssetObservationType>> Process<TU, TAssetObservationType>(
            Func<T, Task<TAssetObservationType>> assetInspector,
            EmbeddingProviderDelegate<EmbeddingResultWithObservation<TU, TAssetObservationType>, TAssetObservationType>
                embeddingAction,
            T asset,
            CancellationToken cancellationToken) where TAssetObservationType : AssetObservation
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            if (cancellationToken.IsCancellationRequested)
                return null;

            InternalLog.Log($"Creating observation for asset: {asset}", LogFilter.SearchVerbose);
            var obs = await assetInspector(asset);

            if (cancellationToken.IsCancellationRequested)
                return null;

            sw.Stop();
            InternalLog.Log(
                $"Created observation for asset: {asset} GUID: {obs.assetGuid} ({sw.ElapsedMilliseconds / 1000f}s)",
                LogFilter.SearchVerbose);

            // Store reference in static collection to avoid premature cleanup of textures by Unity:
            s_Observations.Add(obs);

            try
            {
                sw.Restart();
                var result = await embeddingAction(obs);

                if (cancellationToken.IsCancellationRequested)
                    return null;

                sw.Stop();
                InternalLog.Log($"Created embedding for asset: {obs.assetGuid} ({sw.ElapsedMilliseconds / 1000f}s)",
                    LogFilter.SearchVerbose);

                return result;
            }
            catch (Exception ex)
            {
#if ASSISTANT_INTERNAL
                if (obs is PreviewAssetObservation previewAssetObservation)
                {
                    foreach (var texture in previewAssetObservation.previews)
                    {
                        if (texture == null)
                            Debug.LogError($"Texture is null: {obs.assetGuid}");
                    }
                }
#endif
                InternalLog.LogError($"[{GetType().Name}] {nameof(AssetEmbedding)} failed for {asset}: {ex.Message}",
                    LogFilter.Search);

                // Yield so domain reload can complete if that's the cause of the exception
                if (AssetKnowledgeSettings.RunAsync)
                    await Task.Yield();

                // Rethrow to preserve stack trace and let caller handle the exception
                throw;
            }
            finally
            {
                s_Observations.Remove(obs);
            }
        }
    }
}