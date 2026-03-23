using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.Contexts
{
    static class VisualElementContextExtensions
    {
        /// <summary>
        /// Wrapper over `RegisterContextChangedCallback` to avoid being called when a component is detached from the panel
        /// and if a context has not been changed (usually because of an attach/detach cycle).
        /// </summary>
        /// <param name="element"></param>
        /// <param name="key"></param>
        /// <param name="callback"></param>
        /// <param name="canBeNull"></param>
        /// <typeparam name="T"></typeparam>
        public static void UseContext<T>(this VisualElement element, object key, Action<T> callback, bool canBeNull = true)
        {
            var initialized = false;
            T lastContext = default;
            element.RegisterContextChangedCallback<T>(key, context =>
            {
                if (EqualityComparer<T>.Default.Equals(context, lastContext) && initialized)
                    return;
                if (!initialized && context is null)
                    return;
                if (!canBeNull && context is null)
                    return;
                initialized = true;
                lastContext = context;
                callback(context);
            });
        }

        /// <summary>
        /// Wrapper over `RegisterContextChangedCallback` to avoid being called when a component is detached from the panel
        /// and if a context has not been changed (usually because of an attach/detach cycle).
        /// </summary>
        /// <param name="element"></param>
        /// <param name="callback"></param>
        /// <param name="canBeNull"></param>
        /// <typeparam name="T"></typeparam>
        public static void UseContext<T>(this VisualElement element, Action<T> callback, bool canBeNull = true)
        {
            UseContext(element, typeof(T).FullName, callback, canBeNull);
        }
    }
}
