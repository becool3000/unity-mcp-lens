using System;
using System.Threading.Tasks;
using Unity.AI.Toolkit.Utility;

namespace Unity.AI.Generators.Redux.Toolkit
{
    [Serializable]
    record ApiState
    {
        public SerializableDictionary<ApiCacheKey, EndpointCacheItem> cachedResponses = new();
        public Task refetchOperations = Task.CompletedTask;
    }
}
