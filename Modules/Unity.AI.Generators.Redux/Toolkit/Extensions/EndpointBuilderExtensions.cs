using System;
using System.Runtime.CompilerServices;
using Unity.AI.Generators.Redux.Thunks;

namespace Unity.AI.Generators.Redux.Toolkit
{
    static class EndpointBuilderExtensions
    {
        public static EndpointDefinition<TArg, TPayload> Query<TArg, TPayload>(
            this EndpointBuilder builder,
            AsyncThunkRunner<TArg, TPayload> query,
            [CallerMemberName] string callerName = "")
            => builder.Query<TArg, TPayload>(new(query, callerName:callerName));
        public static EndpointDefinition<TArg, MutationPayload> Mutation<TArg>(
            this EndpointBuilder builder,
            AsyncThunkRunner<TArg, MutationPayload> query,
            [CallerMemberName] string callerName = "")
            => builder.Mutation<TArg>(new(query: query, callerName:callerName));
    }
}
