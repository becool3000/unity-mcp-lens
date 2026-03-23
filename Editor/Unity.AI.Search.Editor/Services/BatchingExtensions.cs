using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Toolkit;

namespace Unity.AI.Search.Editor.Services
{
    /// <summary>
    /// Extension class to create subsets of an array for batching smaller chunks
    /// of data instead of the entire collection at once.
    /// </summary>
    static class BatchingExtensions
    {
        /// <summary>
        /// Divides the source input into smaller chunks and yield returns each chunk.
        /// </summary>
        /// <param name="source">The entire collection of source input</param>
        /// <param name="batchSize">The size of the subset of items</param>
        /// <returns>A subset of the source input</returns>
        /// <exception cref="ArgumentOutOfRangeException">batchSize must be greater than 0</exception>
        static IEnumerable<T[]> Batch<T>(this T[] source, int batchSize)
        {
            if (batchSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(batchSize));

            if (source == null)
                yield break;

            for (var index = 0; index < source.Length; index += batchSize)
                yield return source.Skip(index).Take(batchSize).ToArray();
        }

        /// <summary>
        /// Calls an action with subsets of the source input in batches until the
        /// source is exhausted and waits a frame between each batch.
        /// </summary>
        /// <param name="source">The entire collection of source input</param>
        /// <param name="batchSize">The size of the subset of items</param>
        /// <param name="processBatchAsync">The callback to call with each batch</param>
        public static async Task ProcessInBatches<T>(this T[] source, int batchSize,
            Action<T[]> processBatchAsync)
        {
            foreach (var batch in Batch(source, batchSize))
            {
                processBatchAsync(batch);
                await EditorTask.Yield();
            }
        }
    }
}
