using System;

namespace Unity.AI.Generators.Redux.Toolkit
{
    static class EndpointCacheItemExtensions
    {
        public static EndpointResult<TPayload> Result<TPayload>(this EndpointCacheItem item) => item.result.Result<TPayload>();
        public static EndpointResult<TPayload> Result<TPayload>(this EndpointResult result) => new(result);
        public static bool IsExpired(this EndpointCacheItem item) => item.Subscribers <= 0;
    }
}
