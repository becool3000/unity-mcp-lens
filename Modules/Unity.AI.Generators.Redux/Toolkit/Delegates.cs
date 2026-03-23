using System;
using System.Collections.Generic;

namespace Unity.AI.Generators.Redux.Toolkit
{
    delegate EndpointOperation<TPayload> Endpoint<TArg, TPayload>(TArg arg, QueryOptions options = null);
    delegate List<string> ProvidesTags<TArg, TPayload>(TArg arg, EndpointResult<TPayload> result);
    delegate ApiCacheKey SerializeQueryArgs<TArg, TPayload>(
        TArg arg,
        BaseQueryDefinitionOptions<TArg, TPayload> definitionOptions,
        ApiDefaultKey apiDefaultKey);

    delegate List<string> ProvidesTagsToCache(EndpointResult result);
}
