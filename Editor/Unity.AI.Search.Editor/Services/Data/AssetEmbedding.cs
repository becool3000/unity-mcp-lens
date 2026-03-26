using System;
using UnityEngine;

namespace Unity.AI.Search.Editor
{
    /// <summary>
    /// Single piece of embedding with related asset data.
    /// </summary>
    [Serializable]
    record AssetEmbedding
    {
        public string assetGuid; // The GUID of the asset (more stable than path)
        public float[] embedding; // The embedding vector of the asset
        public Hash128 assetContentHash;
        public string version;
        public string embeddingModelId; // Used to identify what model to use to retrieve tags.
    }
}