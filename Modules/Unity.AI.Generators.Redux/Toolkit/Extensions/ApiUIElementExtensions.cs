using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.Redux.Toolkit
{
    static class ApiUIElementExtensions
    {
        public const string defaultApiKey = "api";

        /// <summary>
        /// Favoring a global static list of Apis over ui element contexts
        /// for ease-of-use and because Apis are usually app-global.
        /// </summary>
        static Dictionary<string, object> s_Apis = new();

        public static T Api<T>(this VisualElement element, string contextKey = defaultApiKey) where T: class =>
            s_Apis.GetValueOrDefault(contextKey) as T;
    }
}
