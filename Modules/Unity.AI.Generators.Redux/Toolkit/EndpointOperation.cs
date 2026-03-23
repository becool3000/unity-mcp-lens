using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Toolkit;
using UnityEngine;

namespace Unity.AI.Generators.Redux.Toolkit
{
    abstract class EndpointOperation
    {
        public abstract Task Refetch();
    }

    /// <summary>
    /// Operation API.
    /// </summary>
    abstract class EndpointOperation<TResult> : EndpointOperation
    {
        public abstract QueryOptions queryOptions { get; }
        public abstract Api api { get; }
        public abstract CacheSelectorInfo cacheInfo { get; }
        public abstract EndpointCacheItem CacheItem { get; }
        public abstract EndpointResult<TResult> Result { get; }
        public abstract Task<EndpointResult<TResult>> Task { get; }
        public TaskAwaiter<EndpointResult<TResult>> GetAwaiter() => Task.GetAwaiter();
        public abstract void Subscribe();
        public abstract void Unsubscribe();
        public Task RefetchOperations => api.store.State.SelectApiState().refetchOperations;

        public void EnsureCacheItem()
        {
            if (CacheItem == null)
                Refetch();
        }
    }

    /// <summary>
    /// Operation API that also captures the operation context's arguments.
    /// </summary>
    sealed class EndpointOperation<TArg, TResult> : EndpointOperation<TResult>
    {
        Lazy<Task<EndpointResult<TResult>>> m_Task;

        public readonly OperationContext<TArg, TResult> context;
        public override QueryOptions queryOptions => context.queryOptions;
        public override Api api => context.api;
        public override CacheSelectorInfo cacheInfo => context.info;
        public override EndpointCacheItem CacheItem => api.store.State.SelectApiCacheItem(cacheInfo);
        public override EndpointResult<TResult> Result => api.store.State.SelectEndpointResult<TResult>(cacheInfo);

        public override Task<EndpointResult<TResult>> Task => m_Task.Value;

        public EndpointOperation(OperationContext<TArg, TResult> context)
        {
            this.context = context;

            Reset();

            if (!context.queryOptions.startOnAwait!.Value)
                _ = Task;   // Start operation right away
        }

        /// <summary>
        /// Re-fetch doesn't wait for any `await` and is immediate.
        /// </summary>
        public override Task Refetch()
        {
            Reset();
            return Task;
        }

        void Reset()
        {
            if (CacheItem == null)
                CreateCacheItem();

            m_Task = new(async () =>
            {
                Subscribe(); // Ensure cache item stays available while operation is in-flight.
                await Execute();
                var result = Result;
                Unsubscribe();
                return result;
            });
        }

        async Task Execute()
        {
            await api.store.DispatchAsync(context.thunkCreator.Invoke(
                new(context.key, context.args),
                context.queryOptions.cancellationToken ?? CancellationToken.None,
                new(queryOptions.logThunkException ?? true)));
            context.extra.onDone?.Invoke(CacheItem, context.args);
        }

        void CreateCacheItem() =>
            api.store.Dispatch(
                new ApiAddCacheEntry(
                    $"{context.thunkCreator.type}/addCacheEntry",
                    context.key,
                    this,
                    context.extra.queryType));

        public override void Subscribe() => api.store.Dispatch(api.internalActions.ApiCacheSubscribe, new(cacheInfo.key));
        // Cache Removal:
        //  - If no subscribers left and cache retain time policy has expired.
        //  - Do not remove if cache item's result is uninitialized
        public override void Unsubscribe()
        {
            api.store.Dispatch(api.internalActions.ApiCacheUnsubscribe, new(cacheInfo.key));
            _ = ApplyCacheLifetimePolicy();
        }

        async Task ApplyCacheLifetimePolicy()
        {
            if (CacheItem.IsExpired())
            {
                // Remove the cache item
                await EditorTask.Delay(queryOptions.keepUnusedDataFor ?? 0);
                if (CacheItem.IsExpired())
                    api.store.Dispatch(api.internalActions.ApiCacheRemove, cacheInfo.key);
            }
        }
    }
}
