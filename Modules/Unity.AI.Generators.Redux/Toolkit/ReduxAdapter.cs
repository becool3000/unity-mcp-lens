using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Toolkit.Utility;
using UnityEngine;

namespace Unity.AI.Generators.Redux.Toolkit
{
    class ReduxAdapter : IReduxAdapter
    {
        ApiOptions m_Options;

        public void Init(ApiOptions options)
        {
            m_Options = options;
            m_Options.store.CreateSlice(
                m_Options.slice,
                new ApiState(),
                reducers => reducers
                .Slice<EndpointCacheItem, IApiAction>(
                    (state, action, slice) =>
                        state.cachedResponses[action.key] = slice(state.cachedResponses.GetValueOrDefault(action.key, new())),
                    Reducers,
                    cacheItem => cacheItem with
                    {
                        result = cacheItem.result with
                        {
                            error = cacheItem.result.error != null ? cacheItem.result.error with { } : null
                        },
                        tags = cacheItem.tags?.ToList(),
                    })
                .AddCase<ApiInvalidateCacheAction>("invalidate", (state, action) =>
                {
                    foreach (var key in action.keys)
                        state.cachedResponses[key].result = new();
                })
                .AddCase<ApiQueryRefetchAction>("refetch", (state, action) =>
                    state.refetchOperations = state.refetchOperations.UnityContinueWith(_ => Task.WhenAll(action.task)))
                .AddCase<StandardAction<ApiCacheKey>>("removeCache", (state, action) =>
                    state.cachedResponses.Remove(action.payload)),
                null,
                state => state with
                {
                    cachedResponses = new(state.cachedResponses.ToDictionary(
                        kvp => kvp.Key with {},
                        kvp => kvp.Value with {}))
                }
            );
        }

        static void Reducers(SwitchBuilder<EndpointCacheItem> sliceReducers) => sliceReducers
            .AddCase<ApiAddCacheEntry>("addCacheEntry").With((state, action) =>
                state with {operation = action.operation, queryType = action.queryType})
            .AddCase<ApiPendingAction>(Constants.Pending).With((state, action) =>
                state with {result = state.result with {isLoading = true, isUninitialized = false}})
            .AddCase<ApiFulfilledAction>(Constants.Fulfilled).With((state, action) =>
                state with
                {
                    tags = action.tags(state.result),
                    result = state.result with {isLoading = false, isSuccess = true, payloadObj = action.payload}
                })
            .AddCase<ApiRejectedAction>(Constants.Rejected).With((state, action) =>
                state with
                {
                    tags = action.tags(state.result),
                    result = state.result with {isLoading = false, isError = true, error = new(action.error)}
                })
            .AddCase<ApiProgressAction>(Constants.Progress).With((state, action) =>
                state.result = state.result with {isLoading = true, progress = action.payload})
            .AddCase<ApiSubscribeCache>(ApiActions.subscribe).With((state, action) =>
                state.Subscribers++)
            .AddCase<ApiUnsubscribeCache>(ApiActions.unsubscribe).With((state, action) =>
                state.Subscribers--);

        public void Dispose() => m_Options.store.RemoveSlice(m_Options.slice);
    }
}
