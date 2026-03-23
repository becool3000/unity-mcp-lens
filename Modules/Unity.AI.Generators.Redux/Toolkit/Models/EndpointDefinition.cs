using System;

namespace Unity.AI.Generators.Redux.Toolkit
{
    record EndpointDefinition<TArg, TPayload>(Endpoint<TArg, TPayload> callback)
    {
        public EndpointOperation<TPayload> Invoke(TArg arg, QueryOptions options = null) => callback(arg, options);
        public Endpoint<TArg, TPayload> Endpoint => Invoke;
    }

    record MutationEndpointDefinition<TArg>(Endpoint<TArg, MutationPayload> callback) :
        EndpointDefinition<TArg, MutationPayload>(callback);
}
