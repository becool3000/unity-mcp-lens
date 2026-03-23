using System.Collections.Generic;

namespace Unity.AI.Generators.Redux
{
    record SwitchReducers<TState>
    {
        public List<(ActionMatcher matcher, CaseReducer<TState> reducer)> Reducers { get; } = new();
        public void Add(ActionMatcher matcher, CaseReducer<TState> reducer) => Reducers.Add((matcher, reducer));
    }
}
