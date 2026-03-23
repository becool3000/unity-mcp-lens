using System;

namespace Unity.AI.Generators.Redux
{
    /// <summary>
    /// A store which holds a typed state.
    ///
    /// The typed state can be generated however you prefer, but it is generally done by mapping all slices to a single object.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    class Store<T> : Store where T: class, IState, new()
    {
        public Store(T state = null) : base(state ?? new()) { }

        public void CreateTypedRootReducer(
            Action<SwitchBuilder<T>> reducers,
            Action<SwitchBuilder<T>> extraReducers = null,
            StateDuplicator<T> stateDuplicator = null) =>
            CreateSlice("typedRoot", GetState(), reducers, extraReducers, stateDuplicator);

        public T GetState() => State as T;
        public static implicit operator T(Store<T> store) => store.GetState();
    }
}
