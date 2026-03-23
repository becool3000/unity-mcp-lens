using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UIElements.Core;
using Unity.AI.Toolkit.Utility;
using UnityEngine;

namespace Unity.AI.Generators.Redux
{
    /// <summary>
    /// A store holds the whole state tree of your application. The only way to change the state inside it is to dispatch an action on it.
    /// Your application should only have a single store in a Redux app. As your app grows, instead of adding stores,
    /// you split the root reducer into smaller reducers independently operating on the different parts of the state tree.
    /// <para/>
    /// The store has the following responsibilities:<br/>
    ///  - Holds application state <br/>
    ///  - Allows access to state via <see cref="GetState{TState}"/><br/>
    ///  - Allows state to be updated via <see cref="DispatchToSlices"/><br/>
    ///  - Registers listeners via <see cref="Subscribe{TState}"/><br/>
    ///  - Handles unregistering of listeners via the function returned by <see cref="Subscribe{TState}"/><br/>
    /// <para/>
    /// Here are some important principles you should understand about Reducers:<br/>
    ///  - Reducers are the only way to update the state.<br/>
    ///  - Reducers are pure functions that take the previous state and an action, and return the next state.<br/>
    ///  - Reducers must be pure functions. They should not mutate the state, perform side effects like API calls or routing transitions, or call non-pure functions.<br/>
    ///  - Reducers must not do asynchronous logic.<br/>
    ///  - Reducers must not call other <see cref="Reducer"/>.<br/>
    ///  - Reducers must not call <see cref="Subscribe{TState}"/>.<br/>
    ///  - Reducers must not call <see cref="GetState{TState}"/><br/>
    ///  - Reducers must not call <see cref="DispatchToSlices"/><br/>
    /// </summary>
    class Store : IStore, IStoreApi, IDisposable
    {
        static readonly Middleware[] k_DefaultMiddlewares = {ThunkMiddleware.Middleware};

        readonly IState m_State;
        readonly Dictionary<string, Reducer> m_Reducers = new();
        readonly Dictionary<string, List<Action<object>>> m_ListenerWrappers = new();
        readonly List<Action<IState>> m_Listeners = new();
        readonly DispatchQueue m_DispatchQueue = new();
        readonly StoreApi m_StoreApi;               // External-facing Store Api.
        readonly StoreInternalApi m_InternalApi;
        bool m_IsDispatching;

        internal string[] Slices => m_Reducers.Keys.ToArray();

        /// <summary>
        /// Allow side effects on dispatched action by providing an event on any action.
        ///
        /// This should be of limited use and mostly for quick prototyping or last-minute fixes
        /// where an elegant solution is too time-consuming to figure out.
        /// </summary>
        public event Action<StandardAction> OnAction;

        /// <summary>
        /// Called prior to store being disposed for unregistering subscriptions.
        /// </summary>
        public event Action OnDispose;

        /// <summary>
        /// Creates a Redux store that holds the complete state tree of your app.
        /// </summary>
        public Store(IState state = null, bool useDefaultMiddlewares = true)
        {
            m_State = state ?? new ReduxState();
            m_InternalApi = new(this);
            m_StoreApi = new(m_InternalApi);

            if (useDefaultMiddlewares)
                ApplyMiddleware(k_DefaultMiddlewares);
        }

        /// <summary>
        /// Returns the current state tree of your application for a specific slice.
        /// It is equal to the last value returned by the store's reducer.
        /// </summary>
        /// <param name="name"> The name of the state slice. </param>
        /// <typeparam name="TState"> The type of the state. </typeparam>
        /// <returns> The current state tree of your application. </returns>
        /// <exception cref="ArgumentException"> Thrown if the state slice does not exist. </exception>
        public TState GetState<TState>(string name)
        {
            if (!m_State.TryGetValue(name, out var value))
                throw new ArgumentException($"State slice '{name}' does not exist.");

            return (TState)value;
        }

        /// <summary>
        /// Current list of middlewares. Does _not_ include the store's final dispatch/getState middleware.
        /// </summary>
        public Stack<Middleware> Middlewares => m_StoreApi.middlewares;

        /// <summary>
        /// Create a StoreApi with a specific middleware
        /// </summary>
        /// <param name="middlewares">Middlewares to add</param>
        public void ApplyMiddleware(params Middleware[] middlewares)
        {
            foreach (var middleware in middlewares)
                m_StoreApi.middlewares.Push(middleware);
        }

        /// <summary>
        /// Creates a new Store Api based on the current store Api.
        ///
        /// This allows to create different action processing pipelines without affecting the standard dispatch api.
        /// </summary>
        /// <param name="middleware"></param>
        /// <returns></returns>
        public IStoreApi CreateApi(Middleware middleware) =>
            m_StoreApi with {middlewares = new(m_StoreApi.middlewares.Append(middleware))};

        /// <summary>
        /// Returns the current state tree of your application. It is equal to the last value returned by the store's reducer.
        /// </summary>
        /// <returns> The current state tree of your application. </returns>
        public IState State => m_StoreApi.State;

        /// <summary>
        /// Dispatch method used to dispatch actions to the store.
        /// </summary>
        /// <param name="action">Action, action data or action creator.</param>
        public Task DispatchAction(object action) => m_StoreApi.DispatchAction(action);

        internal void InternalDispatch<TAction>(TAction action)
        {
            if (action is StandardAction reduxAction)
                DispatchToSlices(reduxAction, Slices);
            else
                Debug.LogError($"Dispatch cannot process non redux action: {action} -- {action.GetType().Name}");
        }

        internal IState InternalState => m_State;

        /// <summary>
        /// Dispatch to given slices.
        /// </summary>
        /// <param name="action"></param>
        /// <param name="slices"></param>
        internal void DispatchToSlices(StandardAction action, string[] slices)
        {
            // Delay action if a dispatch happens while dispatching to avoid infinite loops
            if (m_IsDispatching)
            {
                ExceptionUtilities.LogRedux($"Delaying action [{action.type}] -- Dispatching while dispatching.");
                m_DispatchQueue.Queue(this, action, slices);
                return;
            }

            if (!m_DispatchQueue.isProcessing)
                ExceptionUtilities.LogRedux($"Dispatching [{action.type}]");

            foreach (var slice in slices)
            {
                if (!m_Reducers.TryGetValue(slice, out var reducer))
                    throw new ArgumentException($"Reducer for action type '{action.type}' does not exist.");

                Try.Safely(() => m_State[slice] = reducer(m_State[slice], action));
            }

            m_IsDispatching = true;
            Try.Safely(() => NotifyStateChanged(slices));   // Don't stop executing when running outside actions
            Try.Safely(() => OnAction?.Invoke(action));
            m_IsDispatching = false;

            // Run queued dispatches that happened as an effect of notifying for this dispatch's changes.
            m_DispatchQueue.Drain(action);
        }

        /// <summary>
        /// Adds a change listener.
        /// It will be called any time an action is dispatched, and some part of the state tree may potentially have changed.
        /// </summary>
        /// <remarks>
        /// This method doesn't check for duplicate listeners,
        /// so calling it multiple times with the same listener will result in the listener being called multiple times.
        /// </remarks>
        /// <param name="name"> The name of the state slice. </param>
        /// <param name="listener"> A callback to be invoked on every dispatch. </param>
        /// <typeparam name="TState"> The type of the state. </typeparam>
        /// <returns> A function to remove this change listener. </returns>
        public Unsubscribe Subscribe<TState>(string name, Action<TState> listener)
        {
            if (!m_ListenerWrappers.ContainsKey(name))
                m_ListenerWrappers[name] = new List<Action<object>>();

            var wrapper = new Action<object>(state => listener.Invoke((TState)state));
            m_ListenerWrappers[name].Add(wrapper);

            return () => Unsubscribe(name, wrapper);
        }

        /// <summary>
        /// Subscribe to any state change
        /// </summary>
        /// <param name="listener">Listener for the state change</param>
        /// <returns></returns>
        public Unsubscribe Subscribe(Action<IState> listener)
        {
            m_Listeners.Add(listener);
            return () => Unsubscribe(listener);
        }

        /// <summary>
        /// Removes a change listener.
        /// </summary>
        /// <remarks>
        /// This method won't throw if the listener is not found.
        /// </remarks>
        /// <param name="name"> The name of the state slice. </param>
        /// <param name="listenerWrapper"> A callback to be invoked on every dispatch. </param>
        /// <returns> True if the listener was removed, false otherwise. </returns>
        bool Unsubscribe(string name, Action<object> listenerWrapper) =>
            m_ListenerWrappers.ContainsKey(name) && m_ListenerWrappers[name].Remove(listenerWrapper);

        bool Unsubscribe(Action<IState> listener) => m_Listeners.Remove(listener);

        /// <summary>
        /// Create a new state slice. A state slice is a part of the state tree.
        /// You can provide reducers that will "own" the state slice at the same time.
        /// </summary>
        /// <remarks>
        /// You can also provide extra reducers that will be called if the action type does not match any of the main reducers.
        /// </remarks>
        /// <param name="name"> The name of the state slice. </param>
        /// <param name="initialState"> The initial state of the state slice. </param>
        /// <param name="reducers"> The reducers that will "own" the state slice. </param>
        /// <param name="extraReducers"> The reducers that will be called if the action type does not match any of the main reducers. </param>
        /// <param name="stateDuplicator">
        /// Optional method to pre-process a slice. This lets you make a copy of the state so that reducers don't have
        /// to deal with immutability for instance.
        ///
        /// The default state clone method uses reflection and might not be as well optimized as a custom method.
        /// </param>
        /// <typeparam name="TState"> The type of the state. </typeparam>
        /// <returns> A slice object that can be used to access the state slice. </returns>
        /// <exception cref="ArgumentException"> Thrown if the state slice already exists. </exception>
        public Slice<TState> CreateSlice<TState>(
            string name,
            TState initialState,
            Action<SwitchBuilder<TState>> reducers,
            Action<SwitchBuilder<TState>> extraReducers = null,
            StateDuplicator<TState> stateDuplicator = null)
        {
            if (m_State.ContainsKey(name))
                throw new ArgumentException($"State slice '{name}' already exists.");

            // add the reducers
            reducers ??= _ => { };
            var reducer = CreateReducer(initialState, new SliceSwitchBuilder<TState>(name), reducers, stateDuplicator);

            // add the extra reducers
            if (extraReducers != null)
            {
                var extraReducer = CreateReducer(initialState, new(), extraReducers,stateDuplicator);
                m_Reducers[name] = CombineReducers(reducer, extraReducer);
            }
            else
            {
                m_Reducers[name] = reducer;
            }

            // add the initial state
            m_State[name] = initialState;

            // return the slice
            return new(name, initialState);
        }

        public void RemoveSlice(string name)
        {
            m_State.Remove(name);
            m_Reducers.Remove(name);
            m_ListenerWrappers.Remove(name);
        }

        /// <summary>
        /// Create reducers for a state slice.
        /// </summary>
        /// <param name="initialState"> The initial state of the state slice. </param>
        /// <param name="builder"></param>
        /// <param name="builderCallback"> The builder that will be used to create the reducers. </param>
        /// <param name="stateDuplicator">
        /// Optional method to pre-process a reducer. This lets you make a copy of the state so that reducers don't have
        /// to deal with immutability for instance.
        /// </param>
        /// <typeparam name="TState"> The type of the state. </typeparam>
        /// <typeparam name="TBuilder"></typeparam>
        /// <returns> A reducer record that can be used to create a state slice. </returns>
        public static Reducer CreateReducer<TState, TBuilder>(
            TState initialState,
            TBuilder builder,
            Action<TBuilder> builderCallback,
            StateDuplicator<TState> stateDuplicator = null) where TBuilder: SwitchBuilder<TState>
        {
            builderCallback(builder);
            return builder.BuildReducer(initialState, stateDuplicator);
        }

        /// <summary>
        /// Create a reducer that combines multiple reducers into one.
        /// </summary>
        /// <param name="reducers"> The reducers to combine. </param>
        /// <returns> A reducer that combines the given reducers. </returns>
        public static Reducer CombineReducers(params Reducer[] reducers) =>
            (state, action) =>
                reducers.AsEnumerable().Aggregate(state, (newState, reducer) => reducer(newState, action));

        /// <summary>
        /// Force notify all listeners of a state slice.
        /// </summary>
        /// <param name="slices"> The name of the state slice.</param>
        void NotifyStateChanged(params string[] slices)
        {
            foreach (var slice in slices)
            {
                if (m_State.TryGetValue(slice, out var state) && m_ListenerWrappers.TryGetValue(slice, out var listeners))
                {
                    foreach (var listener in listeners)
                    {
                        Try.Safely(() => listener.Invoke(state));
                    }
                }
            }

            foreach(var listener in m_Listeners.ToList()) // .ToList(): it can happen that a component gets created and subscribes during the loop
                Try.Safely(() => listener.Invoke(State));
        }

        public void Dispose()
        {
            OnDispose?.Invoke();
            OnDispose = null;
        }
    }
}
