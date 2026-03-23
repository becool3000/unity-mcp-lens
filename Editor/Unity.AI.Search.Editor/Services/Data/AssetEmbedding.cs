using System;
using UnityEngine;

namespace Unity.AI.Search.Editor
{
    /// <summary>
    /// Single piece of embedding with related asset guid.
    /// </summary>
    [Serializable]
    record AssetEmbedding
    {
        public string assetGuid; // The GUID of the asset (more stable than path)
        public float[] embedding; // The embedding vector of the asset
        public Hash128 assetContentHash;
        public string version;
    }
}
