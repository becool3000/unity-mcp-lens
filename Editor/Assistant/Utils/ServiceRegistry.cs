using System;
using System.Collections.Generic;

namespace Unity.AI.Assistant.Editor.Utils
{
    /// <summary>
    /// Simple service registry to manage services and avoid circular dependencies.
    /// Services from the UI assembly can register implementations of interfaces defined in the Editor assembly.
    /// </summary>
    internal static class ServiceRegistry
    {
        private static readonly Dictionary<Type, object> s_Services = new Dictionary<Type, object>();

        /// <summary>
        /// Register a service implementation for a given interface type.
        /// </summary>
        public static void RegisterService<T>(T implementation) where T : class
        {
            s_Services[typeof(T)] = implementation;
        }

        /// <summary>
        /// Get a registered service by interface type.
        /// </summary>
        public static T GetService<T>() where T : class
        {
            if (s_Services.TryGetValue(typeof(T), out var service))
            {
                return service as T;
            }
            return null;
        }

        /// <summary>
        /// Clear all registered services.
        /// </summary>
        public static void Clear()
        {
            s_Services.Clear();
        }
    }
}
