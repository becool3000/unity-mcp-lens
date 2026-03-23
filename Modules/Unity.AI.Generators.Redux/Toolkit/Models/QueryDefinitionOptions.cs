using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Unity.AI.Generators.Redux.Thunks;

namespace Unity.AI.Generators.Redux.Toolkit
{
    record BaseQueryDefinitionOptions<TArgs, TPayload>(
        AsyncThunkRunner<TArgs, TPayload> query = null,
        Func<TPayload, Task<TPayload>> transformResponse = null,
        SerializeQueryArgs<TArgs, TPayload> serializeQueryArgs = null,
        QueryOptions defaultQueryOptions = null,
        int? keepUnusedDataFor = null,
        bool useEndpointIdInKey = false,    // Not 100% sure this ia actually the best default
        [CallerMemberName] string callerName = ""
    );

    /// <summary>
    ///
    /// </summary>
    /// <param name="query"></param>
    /// <param name="transformResponse"></param>
    /// <param name="providesTags"></param>
    /// <param name="serializeQueryArgs">
    /// Can be provided to return a custom cache key value based on the query arguments.
    /// Any `record` object will compare well and is the preferred approach over flattening to a string.
    ///
    /// The default method will be based on query arguments, so if those are simple values or record types,
    /// then all the caching mechanism should work automatically without much intervention.
    /// </param>
    /// <param name="tags"></param>
    /// Shorthand for adding tags when only a list is needed.
    /// <param name="keepUnusedDataFor">Same as `defaultQueryOption.keepUnusedDataFor`. Just a syntax shorthand</param>
    /// <param name="useEndpointIdInKey">
    /// Use globally unique endpoint identifier in key generation.
    ///
    /// This is false by default so that a lambda generating an endpoint would always re-use the same cache.
    ///
    /// This happens mostly when a query uses another query. If `useEndpointIdInKey` is set to true, then the lambda would
    /// generate a different cache item for each call.
    ///
    /// Eg:
    /// <code>
    /// public Endpoint UserById = build.Query(async (id, api) => {});
    /// public Endpoint UserById_B => build.Query(async (id, api) => await UserById(id));
    /// </code>
    /// </param>
    /// <param name="callerName">
    /// The calling method which can be used to automatically label the query to the calling method's name.
    /// </param>
    /// <typeparam name="TArgs"></typeparam>
    /// <typeparam name="TPayload"></typeparam>
    record QueryDefinitionOptions<TArgs, TPayload>(
        AsyncThunkRunner<TArgs, TPayload> query = null,
        ProvidesTags<TArgs, TPayload> providesTags = null,
        Func<TPayload, Task<TPayload>> transformResponse = null,
        SerializeQueryArgs<TArgs, TPayload> serializeQueryArgs = null,
        List<string> tags = null,
        QueryOptions defaultQueryOptions = null,
        int? keepUnusedDataFor = null,
        bool useEndpointIdInKey = false,
        [CallerMemberName] string callerName = ""
    ) : BaseQueryDefinitionOptions<TArgs, TPayload>(query, transformResponse, serializeQueryArgs, defaultQueryOptions, keepUnusedDataFor, useEndpointIdInKey, callerName);

    record MutationDefinitionOptions<TArgs, TPayload>(
        AsyncThunkRunner<TArgs, TPayload> query = null,
        ProvidesTags<TArgs, TPayload> invalidatesTags = null,
        Func<TPayload, Task<TPayload>> transformResponse = null,
        SerializeQueryArgs<TArgs, TPayload> serializeQueryArgs = null,
        List<string> tags = null,
        QueryOptions defaultQueryOptions = null,
        bool useEndpointIdInKey = false,
        [CallerMemberName] string callerName = ""
    ) : BaseQueryDefinitionOptions<TArgs, TPayload>(query, transformResponse, serializeQueryArgs, defaultQueryOptions, null, useEndpointIdInKey, callerName);
}
