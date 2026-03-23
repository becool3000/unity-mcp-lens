using UnityEngine.UIElements;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Unity.AI.Generators.Contexts
{
    static class ContextExtensions
    {
        // Internal class to hold context data for each VisualElement
        class ContextData
        {
            public Dictionary<object, object> LocalContext { get; } = new();
            public Dictionary<object, object> ContextCache { get; } = new();
            public Dictionary<object, List<Action<object>>> ContextChangedCallbacks { get; } = new();
            public Dictionary<(object key, Delegate original), Action<object>> CallbackMappings { get; } = new();
            public HashSet<object> RegisteredKeys { get; } = new();
        }

        // ConditionalWeakTable to associate ContextData with VisualElements without preventing garbage collection
        static ConditionalWeakTable<VisualElement, ContextData> s_ContextDataTable = new();

        // Retrieves or initializes ContextData for a given VisualElement
        static ContextData GetContextData(this VisualElement element)
        {
            if (!s_ContextDataTable.TryGetValue(element, out var data))
            {
                data = new();
                s_ContextDataTable.Add(element, data);

                // Register callbacks for hierarchy changes
                element.RegisterCallback<AttachToPanelEvent>(_ => OnHierarchyChanged(element));
                element.RegisterCallback<DetachFromPanelEvent>(_ => OnHierarchyChanged(element));
            }
            return data;
        }

        // Handles hierarchy changes by invalidating all caches
        static void OnHierarchyChanged(VisualElement element) => InvalidateAllCache(element);

        /// <summary>
        /// Provides a context value for a specific key within the VisualElement.
        /// </summary>
        /// <param name="element">The VisualElement to provide context for.</param>
        /// <param name="key">The key identifying the context.</param>
        /// <param name="value">The value to associate with the key.</param>
        public static void ProvideContext(this VisualElement element, object key, object value)
        {
            var data = element.GetContextData();
            data.LocalContext[key] = value;

            // Invalidate cache for the element itself
            data.ContextCache.Remove(key);
            var newValue = element.GetContext<object>(key);
            if (newValue != null)
            {
                data.ContextCache[key] = newValue;
            }

            // Invalidate cache for descendants
            InvalidateCacheForKeyInDescendants(element, key);

            // Notify callbacks for this key
            NotifyContextChanged(element, key, value);
        }

        public static void ProvideContext(this VisualElement element, object value) =>
            element.ProvideContext(value.GetType().FullName, value);

        /// <summary>
        /// Provides a context value for the root VisualElement (the visual tree) of the current panel,
        /// or the first parent that already has an equivalent key.
        /// </summary>
        /// <param name="visualElement">The VisualElement whose root context will be set.</param>
        /// <param name="value">The value to associate with its type's FullName as the key.</param>
        public static void ProvideRootContext(this VisualElement visualElement, object value) =>
            visualElement.ProvideRootContext(value.GetType().FullName, value);

        /// <summary>
        /// Provides a context value for a specific key in the root VisualElement (the visual tree) of the current panel,
        /// or the first parent that already has an equivalent key (or itself).
        /// </summary>
        /// <param name="visualElement">The VisualElement whose root context will be set.</param>
        /// <param name="key">The key identifying the context.</param>
        /// <param name="value">The value to associate with the key.</param>
        public static void ProvideRootContext(this VisualElement visualElement, object key, object value)
        {
            if (visualElement?.panel?.visualTree == null)
                return;

            VisualElement target = null;
            var current = visualElement;
            while (current != null)
            {
                var data = current.GetContextData();
                if (data.LocalContext.ContainsKey(key))
                {
                    target = current;
                    break;
                }
                current = current.parent;
            }

            if (target == null)
                target = visualElement.panel.visualTree;

            target.ProvideContext(key, value);
        }

        public static T GetContext<T>(this VisualElement element) =>
            element.GetContext<T>(typeof(T).FullName);

        /// <summary>
        /// Retrieves the context value associated with the specified key.
        /// </summary>
        /// <typeparam name="T">The expected type of the context value.</typeparam>
        /// <param name="element">The VisualElement to retrieve context from.</param>
        /// <param name="key">The key identifying the context.</param>
        /// <returns>The context value if found and of type T; otherwise, default(T).</returns>
        /// <exception cref="InvalidCastException">Thrown if the context value is not of type T.</exception>
        public static T GetContext<T>(this VisualElement element, object key)
        {
            var data = element.GetContextData();

            if (data.ContextCache.TryGetValue(key, out var cachedValue))
            {
                if (cachedValue is T typedCachedValue)
                {
                    return typedCachedValue;
                }
                throw new InvalidCastException($"Cached context value for key '{key}' is not of type {typeof(T).Name}.");
            }

            if (data.LocalContext.TryGetValue(key, out var value))
            {
                if (value is T typedValue)
                {
                    data.ContextCache[key] = value;
                    return typedValue;
                }
                throw new InvalidCastException($"Context value for key '{key}' is not of type {typeof(T).Name}.");
            }

            // Traverse up the hierarchy
            var parentElement = element.parent;
            while (parentElement != null)
            {
                var parentData = parentElement.GetContextData();
                if (parentData.LocalContext.TryGetValue(key, out var parentValue))
                {
                    if (parentValue is T typedParentValue)
                    {
                        data.ContextCache[key] = parentValue;
                        return typedParentValue;
                    }
                    throw new InvalidCastException($"Context value for key '{key}' in parent is not of type {typeof(T).Name}.");
                }
                parentElement = parentElement.parent;
            }

            return default; // Or throw an exception if preferred
        }

        // Invalidates the cache for a specific key in all descendant VisualElements
        static void InvalidateCacheForKeyInDescendants(VisualElement element, object key)
        {
            foreach (var child in element.Children().ToList())
            {
                var data = child.GetContextData();

                // Remove the cached value for the key
                data.ContextCache.Remove(key);

                // Retrieve the new value, if any, to update the cache
                var newValue = child.GetContext<object>(key);

                // Update cache without invoking callbacks
                if (newValue != null)
                {
                    data.ContextCache[key] = newValue;
                }

                // Recursively invalidate cache for children
                InvalidateCacheForKeyInDescendants(child, key);
            }
        }

        // Invalidates all caches within the VisualElement hierarchy
        static void InvalidateAllCache(VisualElement element)
        {
            var data = element.GetContextData();

            var oldValues = new Dictionary<object, object>();

            foreach (var key in data.RegisteredKeys)
            {
                data.ContextCache.TryGetValue(key, out var oldValue);
                oldValues[key] = oldValue;
            }

            data.ContextCache.Clear();

            foreach (var key in data.RegisteredKeys)
            {
                var newValue = element.GetContext<object>(key);
                oldValues.TryGetValue(key, out var oldValue);

                // Invoke the helper method to handle callback invocation
                InvokeCallbacksSafely(data, key, oldValue, newValue);
            }

            foreach (var child in element.Children().ToList())
            {
                InvalidateAllCache(child);
            }
        }

        // Notifies all relevant callbacks about a context change
        static void NotifyContextChanged(VisualElement element, object key, object value)
        {
            var data = element.GetContextData();

            if (data.ContextChangedCallbacks.TryGetValue(key, out var callbacks))
            {
                foreach (var callback in callbacks)
                {
                    callback(value);
                }
            }

            // Notify descendants who have registered callbacks
            foreach (var child in element.Children().ToList())
            {
                NotifyContextChanged(child, key, value);
            }
        }

        /// <summary>
        /// Registers a callback to be invoked when the context associated with the specified key changes.
        /// Note: The callback will be invoked immediately upon registration with the current context value.
        /// </summary>
        /// <typeparam name="T">The type of the context value.</typeparam>
        /// <param name="element">The VisualElement to register the callback on.</param>
        /// <param name="key">The key identifying the context to monitor.</param>
        /// <param name="callback">The action to invoke when the context changes.</param>
        public static void RegisterContextChangedCallback<T>(this VisualElement element, object key, Action<T> callback)
        {
            var data = element.GetContextData();

            if (!data.ContextChangedCallbacks.TryGetValue(key, out var callbacks))
            {
                callbacks = new List<Action<object>>();
                data.ContextChangedCallbacks[key] = callbacks;
            }

            // Wrap the callback to handle type casting and nulls using the helper method
            Action<object> wrappedCallback = obj => InvokeCallbackSafely(callback, key, obj);

            callbacks.Add(wrappedCallback);
            data.CallbackMappings[(key, callback)] = wrappedCallback;

            data.RegisteredKeys.Add(key);

            // Immediately invoke the callback with the current context value
            var currentValue = element.GetContext<object>(key);
            InvokeCallbackSafely(callback, key, currentValue);
        }

        /// <summary>
        /// Unregisters a previously registered context change callback.
        /// </summary>
        /// <typeparam name="T">The type of the context value.</typeparam>
        /// <param name="element">The VisualElement to unregister the callback from.</param>
        /// <param name="key">The key identifying the context.</param>
        /// <param name="callback">The action to remove from the callback list.</param>
        public static void UnregisterContextChangedCallback<T>(this VisualElement element, object key, Action<T> callback)
        {
            var data = element.GetContextData();
            if (data.ContextChangedCallbacks.TryGetValue(key, out var callbacks) &&
                data.CallbackMappings.TryGetValue((key, callback), out var wrappedCallback))
            {
                callbacks.Remove(wrappedCallback);
                data.CallbackMappings.Remove((key, callback));

                if (callbacks.Count == 0)
                {
                    data.ContextChangedCallbacks.Remove(key);
                    data.RegisteredKeys.Remove(key);
                }
            }
        }

        /// <summary>
        /// Invokes callbacks associated with a key if the value has changed.
        /// </summary>
        /// <param name="data">The context data containing callbacks.</param>
        /// <param name="key">The key identifying the context.</param>
        /// <param name="oldValue">The previous value associated with the key.</param>
        /// <param name="newValue">The new value to compare and potentially pass to callbacks.</param>
        static void InvokeCallbacksSafely(ContextData data, object key, object oldValue, object newValue)
        {
            if (!Equals(oldValue, newValue))
            {
                if (data.ContextChangedCallbacks.TryGetValue(key, out var callbacks))
                {
                    foreach (var callback in callbacks.ToList())
                    {
                        callback(newValue);
                    }
                }
            }
        }

        /// <summary>
        /// Helper method to safely invoke a typed callback with proper null handling.
        /// </summary>
        /// <typeparam name="T">The type of the context value.</typeparam>
        /// <param name="callback">The original callback to invoke.</param>
        /// <param name="key">The key identifying the context.</param>
        /// <param name="obj">The object to pass to the callback.</param>
        static void InvokeCallbackSafely<T>(Action<T> callback, object key, object obj)
        {
            if (obj is T typedValue)
            {
                callback(typedValue);
            }
            else if (obj == null && !typeof(T).IsValueType)
            {
                // If T is a reference type and obj is null, pass default(T) which is null
                callback(default(T));
            }
            else if (obj == null && typeof(T).IsValueType)
            {
                // If T is a non-nullable value type and obj is null, pass default(T)
                callback(default(T));
            }
            else
            {
                throw new InvalidCastException($"Context value for key '{key}' is not of type {typeof(T).Name}.");
            }
        }
    }
}
