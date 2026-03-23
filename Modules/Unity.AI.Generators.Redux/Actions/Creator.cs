using System;

namespace Unity.AI.Generators.Redux
{
    /// <summary>
    /// Action Creator with ease-to-use interface for common simple cases.
    /// </summary>
    record Creator(string type) : IStandardCreator<StandardAction>
    {
        public object Invoke() => new StandardAction(type);

        public static implicit operator Creator(string type) => new(type);
    }

    /// <summary>
    /// Action Creator with ease-to-use interface for common simple cases.
    /// </summary>
    record Creator<TPayload>(string type, PrepareAction<TPayload, TPayload> prepare = null) : IStandardCreator<StandardAction<TPayload>, TPayload>
    {
        public StandardAction<TPayload> Invoke(TPayload args) => new(type, prepare == null ? args : prepare(args));

        public static implicit operator Creator<TPayload>(string type) => new(type);
    }

    /// <summary>
    /// Action Creator with ease-to-use interface for common simple cases.
    /// </summary>
    record Creator<TArgs, TPayload>(string type, PrepareAction<TArgs, TPayload> prepare = null) : IStandardCreator<StandardAction<TPayload>, TPayload, TArgs>
    {
        public StandardAction<TPayload> Invoke(TArgs args) => new(type, prepare(args));

        public static implicit operator Creator<TArgs, TPayload>(string type) => new(type);
    }
}
