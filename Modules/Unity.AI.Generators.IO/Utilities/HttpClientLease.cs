using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Unity.AI.Generators.IO.Utilities
{
    /// <summary>
    /// A lease for the HttpClient.
    /// Implements IDisposable to release the client immediately without waiting for the delayed cleanup.
    /// </summary>
    sealed class HttpClientLease : IDisposable
    {
        /// <summary>
        /// The acquired shared HttpClient.
        /// </summary>
        public HttpClient client { get; }

        readonly HttpClientManager m_Manager;
        bool m_Disposed;

        internal HttpClientLease(HttpClient client, HttpClientManager manager)
        {
            this.client = client;
            m_Manager = manager;
            m_Disposed = false;
        }

        /// <summary>
        /// Releases the lease. This internally triggers a fire‐and‑forget task
        /// to release the HttpClient after a delay.
        /// </summary>
        public void Dispose()
        {
            if (m_Disposed)
                return;
            // Fire-and-forget for performance and observe exceptions
            m_Manager.ReleaseClientAsync().ContinueWith(t =>
            {
                if (t.IsFaulted)
                    UnityEngine.Debug.LogException(t.Exception);
            }, TaskContinuationOptions.OnlyOnFaulted);
            m_Disposed = true;
        }
    }
}
