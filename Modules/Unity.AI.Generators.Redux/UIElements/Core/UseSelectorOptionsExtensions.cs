using System;
using System.Collections.Generic;
using Unity.AI.Generators.Redux;

namespace Unity.AI.Generators.UIElements.Core
{
    static class UseSelectorOptionsExtensions
    {
        public static UseSelectorOptions<T> Ensure<T>(this UseSelectorOptions<T> obj) => obj ?? new();

        public static UseSelectorOptions<TResult> Create<TResult>(
            this UseSelectorOptions<TResult> options, Selector<TResult> selector, Action<TResult> callback) =>
            options.Ensure() with {selector = selector, callback = callback};

        // Create options with an appropriate sequence comparer for selectors returning an IEnumerable<T>
        public static UseSelectorOptions<IEnumerable<TResult>> CreateForSequence<TResult>(
            this UseSelectorOptions<IEnumerable<TResult>> options,
            Selector<IEnumerable<TResult>> selector,
            Action<IEnumerable<TResult>> callback) =>
            options.Create(selector, callback) with {Comparer = new SequenceComparer<TResult>()};
    }
}
