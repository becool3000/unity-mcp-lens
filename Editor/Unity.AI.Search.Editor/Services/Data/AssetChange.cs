using System;

namespace Unity.AI.Search.Editor
{
    enum AssetChangeType
    {
        Modified,
        Deleted
    }
    
    /// <summary>
    /// Represents a change to an asset for knowledge generation.
    /// </summary>
    [Serializable]
    record AssetChange
    {
        public string assetGuid;
        public AssetChangeType change;
        public bool forceProcess;

        public AssetChange(string assetGuid, AssetChangeType changeType, bool forceProcess = false)
        {
            this.assetGuid = assetGuid;
            change = changeType;
            this.forceProcess = forceProcess;
        }
    }
}