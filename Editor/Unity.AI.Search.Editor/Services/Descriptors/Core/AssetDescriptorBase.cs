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
                result.CleanUpTextures();
            }

            return result;
        }

        protected abstract Task<AssetObservation> DoProcessAsync(T assetObject, CancellationToken cancellationToken);

        protected class EmbeddingResultWithObservation<TEmbedding>
        {
            public TEmbedding EmbeddingResult;
            public AssetObservation Observation;

            public EmbeddingResultWithObservation(TEmbedding embeddingResult, AssetObservation observation)
            {
                EmbeddingResult = embeddingResult;
                Observation = observation;
            }
        }

        protected async Task<EmbeddingResultWithObservation<AssetEmbedding>> GetEmbedding(
            Func<T, Task<AssetObservation>> assetInspector,
            EmbeddingProviderDelegate<AssetEmbedding> embeddingAction,
            T asset,
            CancellationToken cancellationToken)
        {
            return await Process(assetInspector,
                async obs =>
                {
                    var embeddingResult = await embeddingAction(obs);

                    if (cancellationToken.IsCancellationRequested)
                        return null;

                    embeddingResult.version = Version;

                    return new EmbeddingResultWithObservation<AssetEmbedding>(embeddingResult, obs);
                }, asset, cancellationToken);
        }

        protected async Task<EmbeddingResultWithObservation<AssetEmbedding[]>> GetEmbedding(
            Func<T, Task<AssetObservation>> assetInspector,
            EmbeddingProviderDelegate<AssetEmbedding[]> embeddingAction,
            T asset,
            CancellationToken cancellationToken)
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

                    return new EmbeddingResultWithObservation<AssetEmbedding[]>(embeddingResults, obs);
                }, asset, cancellationToken);
        }

        async Task<EmbeddingResultWithObservation<TU>> Process<TU>(
            Func<T, Task<AssetObservation>> assetInspector,
            EmbeddingProviderDelegate<EmbeddingResultWithObservation<TU>> embeddingAction,
            T asset,
            CancellationToken cancellationToken)
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

            if (obs.previews == null || obs.previews.Length == 0)
            {
                InternalLog.LogError("No previews available for embedding generation.", LogFilter.Search);
            }

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
                foreach (var texture in obs.previews)
                {
                    if (texture == null)
                        Debug.LogError($"Texture is null: {obs.assetGuid}");
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