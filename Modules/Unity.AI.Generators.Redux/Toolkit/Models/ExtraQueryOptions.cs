using System;

namespace Unity.AI.Generators.Redux.Toolkit
{
    record ExtraQueryOptions<TArgs>(QueryType queryType, Action<EndpointCacheItem, TArgs> onDone = null);
}
