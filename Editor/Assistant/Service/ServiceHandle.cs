using System;
using System.Threading.Tasks;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.Editor.Service
{
    /// <summary>
    /// Represents the state of a service in the container.
    /// </summary>
    enum ServiceState
    {
        NotRegistered,
        Initializing,
        RegisteredAndInitialized,
        FailedToInitialize
    }

    /// <summary>
    /// Provides a way for consumers to track the progress of a service registration.
    /// Use <see cref="WaitForRegistrationOrFailure"/> to wait for the service to finish initializing,
    /// then check <see cref="State"/> to determine the outcome.
    /// </summary>
    /// <typeparam name="T">The service type</typeparam>
    class ServiceHandle<T> where T : class, IService
    {
        readonly object m_Lock = new();
        TaskCompletionSource<bool> m_RegistrationTcs;
        T m_Service;
        ServiceState m_State = ServiceState.NotRegistered;

        /// <summary>
        /// If <see cref="State"/> is <see cref="ServiceState.FailedToInitialize"/>, contains the reason for failure.
        /// </summary>
        public string FailureReason { get; private set; }

        /// <summary>
        /// If <see cref="State"/> is <see cref="ServiceState.FailedToInitialize"/>, contains the exception that caused the failure.
        /// </summary>
        public Exception FailureException { get; private set; }

        /// <summary>
        /// Gets the current state of the service.
        /// </summary>
        public ServiceState State
        {
            get
            {
                lock (m_Lock)
                {
                    return m_State;
                }
            }
        }

        /// <summary>
        /// Gets the service instance. Only valid when State is Registered. Returns null in other situations.
        /// </summary>
        public T Service
        {
            get
            {
                lock (m_Lock)
                {
                    if (m_State != ServiceState.RegisteredAndInitialized)
                        return null;
                    return m_Service;
                }
            }
            internal set
            {
                lock (m_Lock)
                {
                    m_Service = value;
                }
            }
        }
        
        /// <summary>
        /// Waits for the service to finish initializing, either successfully or with failure.
        /// After this task completes, check <see cref="State"/> to determine the outcome:
        /// <list type="bullet">
        ///   <item><see cref="ServiceState.RegisteredAndInitialized"/>: Service is ready, access via <see cref="Service"/></item>
        ///   <item><see cref="ServiceState.FailedToInitialize"/>: Initialization failed, check <see cref="FailureReason"/> or <see cref="FailureException"/></item>
        /// </list>
        /// </summary>
        public Task WaitForRegistrationOrFailure()
        {
            lock (m_Lock)
            {
                if (m_State == ServiceState.FailedToInitialize ||
                    m_State == ServiceState.RegisteredAndInitialized)
                    return Task.CompletedTask;

                m_RegistrationTcs ??= new TaskCompletionSource<bool>();
                return m_RegistrationTcs.Task;
            }
        }

        /// <summary>
        /// Initialize the service. This can be called again if necessary in failure situations if a reattempt at
        /// initialization is necessary. Errors will be reset, and a new initialization will be attempted against the
        /// service.
        /// </summary>
        public async Task InitializeService()
        {
            ClearErrors();
            SetInitializing();

            try
            {
                await m_Service.Initialize();
            }
            catch (Exception e)
            {
                InternalLog.LogException(e);
                SetFailedToInitialize(e);
                return;
            }
            
            SetRegistered(m_Service);
        }

        void ClearErrors()
        {
            FailureReason = null;
            FailureException = null;
        }

        internal void SetInitializing()
        {
            lock (m_Lock)
            {
                m_State = ServiceState.Initializing;
                m_RegistrationTcs ??= new TaskCompletionSource<bool>();
            }
        }

        internal void SetRegistered(T service)
        {
            TaskCompletionSource<bool> tcs;
            lock (m_Lock)
            {
                m_Service = service;
                m_State = ServiceState.RegisteredAndInitialized;
                tcs = m_RegistrationTcs;
            }

            tcs?.TrySetResult(true);
        }

        internal void SetNotRegistered()
        {
            TaskCompletionSource<bool> tcs;
            lock (m_Lock)
            {
                m_Service = null;
                m_State = ServiceState.NotRegistered;
                tcs = m_RegistrationTcs;
                m_RegistrationTcs = null;
            }

            tcs?.TrySetCanceled();
        }
        
        internal void SetFailedToInitialize(Exception exception)
        {
            TaskCompletionSource<bool> tcs;
            lock (m_Lock)
            {
                m_State = ServiceState.FailedToInitialize;
                tcs = m_RegistrationTcs;
                FailureReason = exception?.Message;
                FailureException = exception;
            }

            tcs?.TrySetResult(true);
        }
    }
}
