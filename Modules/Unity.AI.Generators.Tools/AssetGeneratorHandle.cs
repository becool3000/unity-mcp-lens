using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Object = UnityEngine.Object;

namespace Unity.AI.Generators.Tools
{
    /// <summary>
    /// A handle to an ongoing asset generation. It provides synchronous access to the placeholder asset
    /// and allows awaiting the asynchronous generation task to get the final asset.
    /// </summary>
    /// <typeparam name="T">The type of asset being generated.</typeparam>
    class GenerationHandle<T> where T : Object
    {
        /// <summary>
        /// The placeholder asset created synchronously at the start of the generation.
        /// </summary>
        public T Placeholder { get; internal set; }

        /// <summary>
        /// The task representing the asynchronous validation process. Awaiting this task ensures validation is complete and the placeholder is created.
        /// </summary>
        public Task ValidationTask { get; }

        /// <summary>
        /// The task representing the asynchronous generation process. Awaiting this task yields the final generated asset.
        /// </summary>
        public Task<T> GenerationTask { get; }

        /// <summary>
        /// The task representing the asynchronous download process. Awaiting this task yields the final generated asset.
        /// This is often the same as GenerationTask, but for some asset types (like Sprites) it can be separate.
        /// </summary>
        public Task<T> DownloadTask { get; }

        /// <summary>
        /// A list of messages (errors, warnings) from the generation process. This will be populated after the GenerationTask completes.
        /// </summary>
        public IReadOnlyList<string> Messages { get; internal set; }

        /// <summary>
        /// The estimated point cost for this generation, if available.
        /// </summary>
        public long PointCost { get; internal set; }

        internal GenerationHandle(T placeholder, Task<T> generationTask)
        {
            Placeholder = placeholder;
            ValidationTask = Task.CompletedTask;
            GenerationTask = generationTask;
            DownloadTask = generationTask;
            Messages = Array.Empty<string>();
            PointCost = 0;
        }

        internal GenerationHandle(Func<GenerationHandle<T>, Task> validationTaskFactory, Func<GenerationHandle<T>, Task<T>> generationTaskFactory)
        {
            Messages = Array.Empty<string>();
            ValidationTask = validationTaskFactory(this);
            GenerationTask = generationTaskFactory(this);
            DownloadTask = GenerationTask;
            PointCost = 0;
        }

        internal GenerationHandle(Func<GenerationHandle<T>, Task> validationTaskFactory, Func<GenerationHandle<T>, Task<T>> generationTaskFactory,
            Func<GenerationHandle<T>, Task<T>> downloadTaskFactory)
        {
            Messages = Array.Empty<string>();
            ValidationTask = validationTaskFactory(this);
            GenerationTask = generationTaskFactory(this);
            DownloadTask = downloadTaskFactory(this);
            PointCost = 0;
        }

        internal void SetMessages(IReadOnlyList<string> messages)
        {
            Messages = messages ?? Array.Empty<string>();
        }

        /// <summary>
        /// Gets an awaiter for the generation task. This allows using `await` directly on a `GenerationHandle` instance.
        /// </summary>
        /// <returns>A task awaiter.</returns>
        public System.Runtime.CompilerServices.TaskAwaiter<T> GetAwaiter()
        {
            return DownloadTask.GetAwaiter();
        }
    }
}
