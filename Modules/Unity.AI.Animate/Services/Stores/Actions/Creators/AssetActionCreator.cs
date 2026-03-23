using System;
using Unity.AI.Generators.Redux;

namespace Unity.AI.Animate.Services.Stores.Actions.Creators
{
    record AssetActionCreator<TPayload>(string type, PrepareAction<TPayload, TPayload> prepare = null) : IStandardCreator<ContextAction<TPayload>, TPayload>
    {
        public ContextAction<TPayload> Invoke(TPayload args) => new(type, prepare == null ? args : prepare(args));
    }
}
