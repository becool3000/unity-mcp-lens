using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Toolkit;
using UnityEditor;

namespace Unity.AI.Generators.IO.Utilities
{
    /// <summary>
    /// A ScriptableSingleton that manages a single HttpClient instance.
    /// When no one has acquired it, it starts a one‑second countdown before disposing the client.
    /// It also cleans up on OnDisable and OnEditorQuitting.
    ///
    /// This version supports using‑semantics via an HttpClientLease.
    /// </summary>
    sealed class HttpClientManager : ScriptableSingleton<HttpClientManager>
    {
        HttpClient m_Client;
        CancellationTokenSource m_CtsForDisposal;
        int m_AcquiredCount;

        /// <summary>
        /// Acquires a lease for the shared HttpClient.
        /// The client is created if needed and any pending disposal is cancelled.
        /// The returned lease implements IDisposable so it can be used with using statements.
        /// </summary>
        public HttpClientLease AcquireLease()
        {
            m_CtsForDisposal?.Cancel();
            m_CtsForDisposal = null;
            m_Client ??= new HttpClient();
            m_AcquiredCount++;
            return new HttpClientLease(m_Client, this);
        }

        /// <summary>
        /// Releases the client. When no one has acquired it the client is kept alive for 1 second
        /// before being disposed.
        /// </summary>
        public async Task ReleaseClientAsync()
        {
            m_AcquiredCount = Math.Max(m_AcquiredCount - 1, 0);
            if (m_AcquiredCount > 0)
                return;

            m_CtsForDisposal?.Dispose();
            m_CtsForDisposal = new CancellationTokenSource();
            try
            {
                await EditorTask.Delay(1000, m_CtsForDisposal.Token);
                if (m_AcquiredCount > 0)
                    return;

                m_Client?.Dispose();
                m_Client = null;
            }
            catch (TaskCanceledException)
            {
                // If canceled, the client was re-acquired—do nothing.
            }
        }

        void CleanupClient()
        {
            m_CtsForDisposal?.Dispose();
            m_CtsForDisposal = null;
            m_Client?.Dispose();
            m_Client = null;
        }

        void OnEnable() => EditorApplication.quitting += OnEditorQuitting;

        void OnDisable()
        {
            EditorApplication.quitting -= OnEditorQuitting;
            CleanupClient();
        }

        void OnEditorQuitting() => CleanupClient();
    }
}
