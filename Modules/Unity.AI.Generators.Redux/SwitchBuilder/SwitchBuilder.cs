using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Unity.AI.Generators.Redux
{
    partial class SwitchBuilder<TState>
    {
        static bool DefaultMatcher(StandardAction _) => true;  // Matcher for default cases

        enum CasePriority { Case, Matcher, Default }

        readonly Dictionary<CasePriority, SwitchReducers<TState>> m_Reducers = new()
        {
            { CasePriority.Case, new() },
            { CasePriority.Matcher, new() },
            { CasePriority.Default, new() }
        };

        public ActionTypeResolver ActionTypeResolver { get; init; }

        SwitchBuilder<TState> AddReducer(CasePriority priority, ActionMatcher matcher, CaseReducer<TState> reducer)
        {
            m_Reducers[priority].Add(matcher, reducer);
            return this;
        }

        public SwitchBuilder<TState> AddCase(ActionMatcher matcher, CaseReducer<TState> reducer) =>
            AddReducer(CasePriority.Case, matcher, reducer);

        /// <summary>
        /// Adds a matcher case to the reducer switch statement.
        /// A matcher case is a case that will be executed if the action type matches the predicate.
        /// </summary>
        /// <param name="matcher"> The predicate that will be used to match the action type. </param>
        /// <param name="reducer"> The reducer function for the action type you want to handle. </param>
        /// <returns> The Reducer Switch Builder. </returns>
        public SwitchBuilder<TState> AddMatcher(ActionMatcher matcher, CaseReducer<TState> reducer) =>
            AddReducer(CasePriority.Matcher, matcher, reducer);

        /// <summary>
        /// Adds a default case to the reducer switch statement.
        /// A default case is a case that will be executed if no other cases match.
        /// </summary>
        /// <param name="matcher"> The predicate that will be used to match the action type. </param>
        /// <param name="reducer"> The reducer function for the default case. </param>
        /// <returns> The Reducer Switch Builder. </returns>
        public SwitchBuilder<TState> AddDefault(ActionMatcher matcher, CaseReducer<TState> reducer) =>
            AddReducer(CasePriority.Default, matcher, reducer);

        List<CaseReducer<TState>> GetMatchingReducers(StandardAction action)
        {
            var matchingReducers = new List<CaseReducer<TState>>();

            foreach (var caseReducerGroup in m_Reducers)
            {
                if (caseReducerGroup.Key == CasePriority.Default && matchingReducers.Any())
                    break;

                foreach (var caseReducer in caseReducerGroup.Value.Reducers)
                {
                    if (caseReducer.matcher(action))
                        matchingReducers.Add(caseReducer.reducer);
                }
            }

            return matchingReducers;
        }

        // Start of Selection
        static TState DuplicateState(object state, StateDuplicator<TState> stateDuplicator) =>
            state is TState typedState && stateDuplicator != null
                ? stateDuplicator(typedState)
                : (TState)state;

        /// <summary>
        /// Builds the reducer switch statement.
        /// </summary>
        /// <param name="initialState"> The initial state of the reducer. </param>
        /// <param name="stateDuplicator">
        /// Optional method to pre-process a reducer. This lets you make a copy of the state so that reducers don't have
        /// to deal with immutability for instance.
        /// </param>
        /// <returns> The reducer switch statement. </returns>
        public Reducer BuildReducer(TState initialState, StateDuplicator<TState> stateDuplicator = null)
        {
            return (state, action) =>
            {
                state ??= initialState;

                var matchingReducers = GetMatchingReducers(action);

                if (!matchingReducers.Any())
                    return state;

                var newState = DuplicateState(state, stateDuplicator);

                foreach (var reducer in matchingReducers)
                    newState = reducer(newState, action);

                return newState;
            };
        }
    }
}
