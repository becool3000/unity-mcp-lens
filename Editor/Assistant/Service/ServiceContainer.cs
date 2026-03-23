using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.Editor.Service
{
    /// <summary>
    /// Service container that provides dependency injection functionality.
    /// Allows registration and retrieval of services with support for overriding existing services.
    /// </summary>
    class ServiceContainer
    {
        readonly Dictionary<Type, object> m_Handles = new();
        readonly object m_Lock = new();

        /// <summary>
        /// Registers a service instance for the specified type.
        /// If a service of the same type already exists, it will be overridden.
        /// </summary>
        /// <typeparam name="T">The service type to register</typeparam>
        /// <param name="service">The service instance to register</param>
        /// <returns>A handle to track the service registration progress</returns>
        /// <exception cref="ArgumentNullException">Thrown when service is null</exception>
        public async Task RegisterService<T>(T service) where T : class, IService
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));

            var handle = GetOrCreateHandle<T>();
            IService oldService = null;

            // Cache
            lock (m_Lock)
                oldService = handle.Service;

            handle.Service = service;
            await handle.InitializeService();
            
            // Dispose of the old service, do not block on this
            if (oldService != null)
                _ = oldService.DisposeAsync();
        }

        /// <summary>
        /// Retrieves a service of the specified type.
        /// </summary>
        /// <typeparam name="T">The service type to retrieve</typeparam>
        /// <returns>The service instance, or null if not registered</returns>
        public ServiceHandle<T> GetService<T>() where T : class, IService
        {
            var handle = GetOrCreateHandle<T>();
            return handle;
        }

        /// <summary>
        /// Unregisters a service of the specified type.
        /// </summary>
        /// <typeparam name="T">The service type to unregister</typeparam>
        /// <returns>True if the service was removed, false if it wasn't registered</returns>
        public async Task<bool> UnregisterService<T>() where T : class, IService
        {
            ServiceHandle<T> handle;
            IService service;

            lock (m_Lock)
            {
                if (!m_Handles.TryGetValue(typeof(T), out var handleObj))
                    return false;

                handle = (ServiceHandle<T>)handleObj;
                service = handle.Service;

                if (service == null)
                    return false;

                handle.SetNotRegistered();
            }

            await service.DisposeAsync();

            return true;
        }

        ServiceHandle<T> GetOrCreateHandle<T>() where T : class, IService
        {
            lock (m_Lock)
            {
                if (m_Handles.TryGetValue(typeof(T), out var handleObj))
                    return (ServiceHandle<T>)handleObj;

                var handle = new ServiceHandle<T>();
                m_Handles[typeof(T)] = handle;
                return handle;
            }
        }
    }
}
