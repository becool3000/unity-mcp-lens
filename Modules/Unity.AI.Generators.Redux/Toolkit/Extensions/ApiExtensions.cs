using System;

namespace Unity.AI.Generators.Redux.Toolkit
{
    static class Extensions
    {
        /// <summary>
        /// API Extensions
        /// </summary>
        public static EndpointCacheItem SelectApiCacheItem(this Api api, ApiCacheKey key) =>
            api.store.State.SelectApiCacheItem(new(api.options.slice, key));

        /// <summary>
        /// Query option extensions
        /// </summary>
        public static QueryOptions Merge(this QueryOptions a, QueryOptions b)
        {
            a ??= new();
            b ??= new();
            return new QueryOptions(
                a.refetchOnFocus ?? b.refetchOnFocus,
                a.pollingInterval ?? b.pollingInterval,
                a.startOnAwait ?? b.startOnAwait,
                a.logThunkException ?? b.logThunkException,
                a.keepUnusedDataFor ?? b.keepUnusedDataFor,
                a.cancellationToken ?? b.cancellationToken);
        }
    }
}
