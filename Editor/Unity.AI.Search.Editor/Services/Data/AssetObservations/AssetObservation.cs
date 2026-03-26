using System;

namespace Unity.AI.Search.Editor
{
    /// <summary>
    /// Inputs gathered from inspecting an asset.
    /// </summary>
    class AssetObservation : IDisposable
    {
        public string assetGuid;

        protected bool m_Disposed;

        public virtual void Dispose()
        {
            m_Disposed = true;
        }

        ~AssetObservation()
        {
            Dispose();
        }
    }
}