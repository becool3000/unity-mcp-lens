using System;
using System.Collections.Generic;
using Unity.AI.Generators.UIElements.Core;
using UnityEngine;
using Unity.AI.Generators.Redux;

namespace Unity.AI.Generators.UIElements
{
    static class Use
    {
        /// <summary>
        /// Shorthand method to call with arguments rather than a single option object.
        /// </summary>
        public static Unsubscribe Selector<TResult>(Selector<TResult> selector, Action<TResult> callback, Store store, UseSelectorOptions<TResult> options = null) =>
            Selector(options.Ensure() with {selector = selector, callback = callback, store = store});

        /// <summary>
        /// Use a selector.
        ///
        /// Key Responsibilities:
        /// - Tracks state changes.
        /// - Tracks selector return value changes.
        ///
        /// In Plain terms:
        /// Calls a selector on state changed.
        /// Callback on selector value change.
        /// </summary>
        /// <param name="options"></param>
        /// <typeparam name="TResult">Selector Result type</typeparam>
        public static Unsubscribe Selector<TResult>(UseSelectorOptions<TResult> options)
        {
            if (options.selector == null)
                return () => true;

            TResult lastResult = options.initialValue;
            var comparer = options.Comparer ?? EqualityComparer<TResult>.Default;

            void InvokeSelector(IState state, bool force = false)
            {
                var result = lastResult;
                try
                {
                    result = options.selector(state);

                    // Wait until a non-default value has been returned by the selector before notifying changes.
                    if (options.waitForValue &&
                        comparer.Equals(result, options.initialValue) &&
                        comparer.Equals(lastResult, options.initialValue))
                        return;

                    if (!comparer.Equals(result, lastResult) || force)
                        options.callback(result);
                }
                catch (Exception exception)
                {
                    Debug.LogException(ExceptionUtilities.AggregateStack(exception, options.sourceInfo));
                }
                finally
                {
                    // Make sure to update lastResult even if callback fails. Otherwise it will keep getting called.
                    lastResult = result;
                }
            }

            var unsubscribe = options.store.Subscribe(state => InvokeSelector(state));

            if (options.selectImmediately)
                InvokeSelector(options.store.State, true);

            return unsubscribe;
        }
    }
}
