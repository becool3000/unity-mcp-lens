using System;
using System.ComponentModel;

namespace Unity.AI.Toolkit.Asset
{
    [Serializable]
    record AssetReference
    {
        public string guid = string.Empty;
    }
}
