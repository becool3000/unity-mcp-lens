using System;
using System.Collections.Generic;

namespace Unity.AI.Generators.Redux.Toolkit
{
    static partial class Selectors
    {
        public static ApiState SelectApiState(this IState state, string sliceName = Api.DefaultSlice) => state.Get<ApiState>(sliceName);
        public static EndpointResult<TResult> SelectEndpointResult<TResult>(this IState state, CacheSelectorInfo selectorInfo) =>
            (state.SelectApiCacheItem(selectorInfo) ?? new()).Result<TResult>();
        public static EndpointCacheItem SelectApiCacheItem(this IState state, CacheSelectorInfo selectorInfo) =>
            state.SelectApiState(selectorInfo.slice).cachedResponses.GetValueOrDefault(selectorInfo.key);
    }
}
