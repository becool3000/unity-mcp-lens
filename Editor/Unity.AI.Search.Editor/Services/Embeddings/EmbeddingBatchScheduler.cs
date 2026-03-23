using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.AI.Search.Editor.Knowledge;
using Unity.AI.Toolkit.Utility;
using UnityEditor;

namespace Unity.AI.Search.Editor.Embeddings
{
    /// <summary>
    /// Collects embedding requests and executes them in batches.
    /// </summary>
    class EmbeddingBatchScheduler
    {
        record ScheduledAction(IDisposable Registration, Action Update);

        // NOTE: The batch size can be larger than k_MaxBatchSize since we only check if we went over the batch size on every .update.
        //       We could either flush immediately when going over the batch size, or process in chunks of k_MaxBatchSize.
        readonly int k_MaxBatchSize = EmbeddingProviders.EmbeddingBatchSize;
        const int m_MaxDelayMs = 50;

        readonly List<(EmbeddingProviders.EmbeddingJob input, TaskCompletionSource<AssetEmbedding> tcs)> m_Pending =
            new List<(EmbeddingProviders.EmbeddingJob input, TaskCompletionSource<AssetEmbedding> tcs)>();

        ScheduledAction m_PendingSchedule;

        public EmbeddingBatchScheduler()
        {
            if (!AssetKnowledgeSettings.RunAsync)
                k_MaxBatchSize = 1;
        }

        public Task<AssetEmbedding> EnqueueAsync(EmbeddingProviders.EmbeddingJob input)
        {
            var tcs = new TaskCompletionSource<AssetEmbedding>();
            m_Pending.Add((input, tcs));
            // Ensures there is a scheduled flush, otherwise create one.
            m_PendingSchedule ??= ScheduleUntil(
                shouldFire: () => m_Pending.Count >= k_MaxBatchSize,
                maxDelayMs: m_MaxDelayMs,
                flushAction: () => _ = Try.Safely(FlushAsync()));

            m_PendingSchedule.Update();
            return tcs.Task;
        }

        async Task FlushAsync()
        {
            if (m_Pending.Count == 0) return;

            var toProcess =
                new List<(EmbeddingProviders.EmbeddingJob input, TaskCompletionSource<AssetEmbedding> tcs)>(m_Pending);
            m_Pending.Clear();
            m_PendingSchedule?.Registration?.Dispose();
            m_PendingSchedule = null;

            List<AssetEmbedding> outputs;
            try
            {
                var inputs = new List<EmbeddingProviders.EmbeddingJob>(toProcess.Count);
                foreach (var (input, _) in toProcess)
                    inputs.Add(input);
                outputs = await EmbeddingProviders.ExecuteBatchAsync(inputs);
            }
            catch (Exception ex)
            {
                foreach (var (_, tcs) in toProcess)
                    tcs.TrySetException(ex);
                return;
            }

            for (var i = 0; i < toProcess.Count; i++)
            {
                var ok = outputs != null && i < outputs.Count;
                if (ok) toProcess[i].tcs.TrySetResult(outputs[i]);
                else
                    toProcess[i].tcs
                        .TrySetException(new InvalidOperationException("Batch size mismatch."));
            }
        }

        static ScheduledAction ScheduleUntil(
            Func<bool> shouldFire,
            int maxDelayMs,
            EditorApplication.CallbackFunction flushAction)
        {
            if (shouldFire == null) throw new ArgumentNullException(nameof(shouldFire));
            if (flushAction == null) throw new ArgumentNullException(nameof(flushAction));

            var scheduledAt = EditorApplication.timeSinceStartup;
            EventHandlerRegistration registration = null;

            void Update()
            {
                var elapsed = EditorApplication.timeSinceStartup - scheduledAt;
                if (shouldFire() || elapsed >= maxDelayMs / 1000.0)
                {
                    registration?.Dispose();
                    flushAction();
                }
            }

            registration = new EventHandlerRegistration(Update);
            Update();

            return new ScheduledAction(registration.disposed ? null : registration, Update);
        }

        class EventHandlerRegistration : IDisposable
        {
            EditorApplication.CallbackFunction m_Handler;
            public bool disposed;

            public EventHandlerRegistration(EditorApplication.CallbackFunction handler)
            {
                m_Handler = handler ?? throw new ArgumentNullException(nameof(handler));
                EditorApplication.update += m_Handler;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (m_Handler != null)
                {
                    EditorApplication.update -= m_Handler;
                    m_Handler = null;
                }
            }
        }
    }
}
