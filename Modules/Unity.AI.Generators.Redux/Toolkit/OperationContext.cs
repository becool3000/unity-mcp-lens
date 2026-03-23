using System;
using Unity.AI.Generators.Redux.Thunks;

namespace Unity.AI.Generators.Redux.Toolkit
{
    record OperationContext<TArg, TResult>(
        Api api,
        AsyncThunkCreator<EndpointThunkArgs<TArg>, TResult> thunkCreator,
        BaseQueryDefinitionOptions<TArg, TResult> definitionOptions,
        ExtraQueryOptions<TArg> extra,
        ApiCacheKey key,
        QueryOptions queryOptions,
        TArg args,
        CacheSelectorInfo info);
}
