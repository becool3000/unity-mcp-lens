using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.AI.Generators.Redux.Thunks;
using UnityEngine;

namespace Unity.AI.Generators.Redux.Toolkit
{
    [Serializable]
    record ApiProgress(float progress)
    {
        [field:SerializeField] public float progress { get; set; } = progress;
    }

    interface IApiAction
    {
        ApiCacheKey key { get; }
    }

    record ApiAddCacheEntry(string type, ApiCacheKey key, EndpointOperation operation, QueryType queryType) : StandardAction(type), IApiAction;
    record ApiPendingAction(ApiCacheKey key, object arg) : PendingAction<object>(null, arg), IApiAction;
    record ApiFulfilledAction(ApiCacheKey key, ProvidesTagsToCache tags, object payload) : FulfilledAction<object>(payload), IApiAction;
    record ApiRejectedAction(ApiCacheKey key, ProvidesTagsToCache tags, Exception error) : RejectedAction(error), IApiAction;
    record ApiProgressAction(ApiCacheKey key, ApiProgress payload) : ProgressAction<ApiProgress>(payload), IApiAction;
    record ApiInvalidateCacheAction(string type, IEnumerable<ApiCacheKey> keys) : StandardAction(type);
    record ApiQueryRefetchAction(string type, IEnumerable<Task> task) : StandardAction(type);

    record ApiSubscribeCache(ApiCacheKey key) : StandardAction, IApiAction;
    record ApiUnsubscribeCache(ApiCacheKey key) : StandardAction, IApiAction;
}
