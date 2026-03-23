using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Unity.AI.Search.Editor.Embeddings;
using Unity.AI.Search.Editor.Utilities;
using UnityEngine;

namespace Unity.AI.Search.Editor.Knowledge.Descriptors
{
    [UsedImplicitly]
    class GameObjectDescriptor : AssetDescriptorBase<GameObject>
    {
        public override string Version => "0.1.0";

        protected override async Task<AssetObservation> DoProcessAsync(GameObject gameObject, CancellationToken cancellationToken)
        {
            var result = await GetEmbedding(
                obj => AssetInspectors.ForGameObjectViews(obj,
                    new GameObjectPreviewOptions(images: 3, baseYaw: 135, pitch: 30f, stepDegrees: 90f)),
                EmbeddingProviders.Embeddings,
                gameObject, cancellationToken);

            var embeddingResults = result?.EmbeddingResult;

            if (embeddingResults is { Length: > 0 })
                EmbeddingIndex.instance.Add(embeddingResults.Average());

            return result?.Observation;
        }
    }
}
