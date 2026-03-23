using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Unity.AI.Search.Editor.Embeddings;
using UnityEngine;

namespace Unity.AI.Search.Editor.Knowledge.Descriptors
{
    [UsedImplicitly]
    class MaterialDescriptor : AssetDescriptorBase<Material>
    {
        public override string Version => "0.1.0";

        protected override async Task<AssetObservation> DoProcessAsync(Material material, CancellationToken cancellationToken)
        {
            var result = await GetEmbedding(
                AssetInspectors.ForMaterial,
                EmbeddingProviders.ImageEmbedding(),
                material, cancellationToken);

            var embeddingResult = result?.EmbeddingResult;

            if (embeddingResult != null)
                EmbeddingIndex.instance.Add(embeddingResult);

            return result?.Observation;
        }
    }
}
