using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UIElements.Core;

namespace Unity.AI.Generators.UIElements.Extensions
{
    static class UseSelectorExtensions
    {
        /// <summary>
        /// Automatically use an appropriate comparer for sequences.
        /// </summary>
        public static Unsubscribe UseArray<TResult>(this VisualElement element,
            Selector<IEnumerable<TResult>> selector,
            Action<IEnumerable<TResult>> callback,
            UseSelectorOptions<IEnumerable<TResult>> options = null) =>
            element.Use(options.CreateForSequence(selector, callback));
        public static void UseArray<TResult>(this VisualElement element, Selector<IEnumerable<TResult>> selector, Action<List<TResult>> callback, UseSelectorOptions<IEnumerable<TResult>> options = null) =>
            element.Use(options.CreateForSequence(selector, result => callback(result?.ToList())));

        /// <summary>
        /// Shorthand for Use with method arguments instead of relying on constructing an option object.
        /// </summary>
        public static Unsubscribe Use<TResult>(this VisualElement element, Selector<TResult> selector, Action<TResult> callback, UseSelectorOptions<TResult> options = null) =>
            element.Use(options.Create(selector, callback));

        /// <summary>
        /// Simplifies Use selector for VisualElement
        ///
        /// Key responsibilities:
        /// - Handles calling back when a selector value has changed
        /// - Handles subscribe/unsubscribe to store based on component lifecycle
        /// - Handles getting store from context
        /// </summary>
        /// <param name="element"></param>
        /// <param name="options"></param>
        /// <typeparam name="TResult"></typeparam>
        public static Unsubscribe Use<TResult>(this VisualElement element, UseSelectorOptions<TResult> options)
        {
            if (ExceptionUtilities.detailedExceptionStack)
                options = options with { sourceInfo = new(true) };
            else
                options = options with { };
            if (options.selector == null)
                return () => true;

            Unsubscribe unsubscribe = null;
            Unsubscribe unsubscribeLifecycle = null;
            element.UseStore(store =>
            {
                unsubscribe?.Invoke();  // Ensures that the previous subscription is removed if the store has changed.
                unsubscribe = null;

                options = options with {store = options.store ?? store};
                unsubscribeLifecycle = element.OnLive(
                    () => unsubscribe = UIElements.Use.Selector(options),
                    () => unsubscribe?.Invoke()
                );
            });

            return () =>
                (unsubscribe?.Invoke() ?? true) &&
                (unsubscribeLifecycle?.Invoke() ?? true);
        }
    }
}
