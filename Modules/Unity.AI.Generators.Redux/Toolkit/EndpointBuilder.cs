using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Generators.Redux.Thunks;
using UnityEngine;

namespace Unity.AI.Generators.Redux.Toolkit
{
    record EndpointBuilder(Api api)
    {
        static QueryOptions s_DefaultQueryOptions = new(startOnAwait:false, logThunkException:true, keepUnusedDataFor:60 * 1000);
        static int s_EndpointId = 1;

        static List<string> GetTags<TArgs, TPayload>(ProvidesTags<TArgs, TPayload> providesTags, List<string> tags, TArgs args, EndpointResult result) =>
            (providesTags?.Invoke(args, result.Result<TPayload>()) ?? new()).Concat(tags ?? new()).ToList();

        static List<string> GetTags<TArgs, TPayload>(BaseQueryDefinitionOptions<TArgs, TPayload> definition, TArgs args, EndpointResult result)
        {
            if (definition is QueryDefinitionOptions<TArgs, TPayload> queryOptions)
                return GetTags(queryOptions.providesTags, queryOptions.tags, args, result);
            return new();
        }

        EndpointDefinition<TArg, TPayload> BaseQuery<TArg, TPayload>(BaseQueryDefinitionOptions<TArg, TPayload> definitionOptions, ExtraQueryOptions<TArg> extra)
        {
            var query = definitionOptions.query;
            var transformResponse = definitionOptions.transformResponse ?? Task.FromResult;
            var defaultQueryOptions = definitionOptions.defaultQueryOptions
                .Merge(new(keepUnusedDataFor: definitionOptions.keepUnusedDataFor))
                .Merge(s_DefaultQueryOptions);

            var endpointId = s_EndpointId++;

            var thunkCreator = new AsyncThunkCreator<EndpointThunkArgs<TArg>, TPayload>(
                api.options.slice,
                async (arg, thunkApi) => {
                    var payload = await query(arg.args, thunkApi);
                    return await transformResponse(payload);
                }
            );

            thunkCreator.ProvideCreators = (args, token) => new(
                pending: requestId => new ApiPendingAction(requestId.key, args.args) {type = thunkCreator.PendingType},
                fulfilled: payload => new ApiFulfilledAction(args.key, result => GetTags(definitionOptions, args.args, result), payload) {type = thunkCreator.FulfilledType},
                rejected: exception => new ApiRejectedAction(args.key, result => GetTags(definitionOptions, args.args, result), exception) {type = thunkCreator.RejectedType},
                progress: progress => new ApiProgressAction(args.key, new(progress)) {type = thunkCreator.ProgressType});

            return new((args, queryOptions) =>
            {
                queryOptions = queryOptions.Merge(defaultQueryOptions);
                var apiDefaultKey = new ApiDefaultKey(args, definitionOptions.callerName, endpointId);
                var key = definitionOptions.serializeQueryArgs?.Invoke(args, definitionOptions, apiDefaultKey) ??
                    (definitionOptions.useEndpointIdInKey ? apiDefaultKey : apiDefaultKey with {endpointId = -1});

                // Only use cache for queries.
                if (extra.queryType == QueryType.Query)
                {
                    var item = api.SelectApiCacheItem(key);
                    if (item?.operation != null)
                        return item.operation as EndpointOperation<TPayload>;
                }

                return new EndpointOperation<TArg, TPayload>(
                    new(api,
                        thunkCreator,
                        definitionOptions,
                        extra,
                        key,
                        queryOptions,
                        args,
                        new(api.options.slice, key))
                );
            });
        }

        public EndpointDefinition<TArg, TPayload> Query<TArg, TPayload>(QueryDefinitionOptions<TArg, TPayload> definitionOptions) =>
            BaseQuery(definitionOptions, new(QueryType.Query));

        /// <summary>
        /// Creates a mutation which will invalidate any tags or keys passed to it.
        /// </summary>
        public MutationEndpointDefinition<TArg> Mutation<TArg>(MutationDefinitionOptions<TArg, MutationPayload> definitionOptions)
        {
            return new(BaseQuery(definitionOptions, new(
                QueryType.Mutation,
                (cacheItem, args) =>
                {
                    var state = api.store.State.SelectApiState();
                    var invalidationTags = GetTags(definitionOptions.invalidatesTags, definitionOptions.tags, args, cacheItem.result);
                    var invalidationItems = state.cachedResponses.Where(kvp =>
                        kvp.Value.queryType == QueryType.Query &&
                        kvp.Value.tags.Exists(tag => invalidationTags.Contains(tag))).ToList();

                    api.store.Dispatch(new ApiInvalidateCacheAction($"{api.options.slice}/invalidate", invalidationItems.Select(kvp => kvp.Key)));
                    api.store.Dispatch(new ApiQueryRefetchAction($"{api.options.slice}/refetch",
                        invalidationItems.Select(kvp => kvp.Value.operation.Refetch())));
                }
                )).callback);
        }
    }
}
