using System;

namespace Unity.AI.Generators.Redux
{
    /// <summary>
    /// A Slice is a collection of reducers and actions for a specific part of the application state.
    /// </summary>
    class Slice
    {
        /// <summary>
        /// The name of the slice.
        /// </summary>
        public string name { get; protected set; }

        /// <summary>
        /// The action creators for this slice.
        /// </summary>

        public static implicit operator string(Slice slice) => slice.name;
    }

    /// <summary>
    /// A Slice is a collection of reducers and actions for a specific domain.
    /// It is a convenient way to bundle them together for use in a Redux store.
    /// </summary>
    /// <typeparam name="TState"> The type of the state associated with this slice. </typeparam>
    sealed class Slice<TState> : Slice
    {
        /// <summary>
        /// Creates a new slice.
        /// </summary>
        /// <param name="name"> The name of the slice. </param>
        /// <param name="initialState"> The initial state for this slice. </param>
        internal Slice(string name, TState initialState)
        {
            this.name = name;
            this.initialState = initialState;
        }

        /// <summary>
        /// The initial state for this slice.
        /// </summary>
        public TState initialState { get; }
    }
}
