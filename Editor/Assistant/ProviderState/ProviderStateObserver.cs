using System;
using Unity.AI.Assistant.Editor.Acp;
using Unity.AI.Toolkit;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor
{
    /// <summary>
    /// Broadcasts the currently selected provider state for status tracking purposes.
    /// Both AssistantStatusTracker and SessionBanner subscribe to this to determine
    /// whether Unity-specific status checks should apply.
    /// </summary>
    static class ProviderStateObserver
    {
        static string s_CurrentProviderId = AssistantProviderFactory.DefaultProvider.ProfileId;
        static ProviderReadyState s_ReadyState = ProviderReadyState.Ready;
        static string s_InitializationError;
        static string s_InitializationErrorCode;
        static InitializationPhase s_CurrentPhase = InitializationPhase.None;

        /// <summary>
        /// Represents the readiness state of the current provider.
        /// </summary>
        public enum ProviderReadyState
        {
            /// <summary>Provider is ready to accept prompts.</summary>
            Ready,
            /// <summary>Provider is starting or connecting.</summary>
            Initializing,
            /// <summary>Provider failed to initialize.</summary>
            Error
        }

        /// <summary>
        /// Represents the current initialization phase for ACP providers.
        /// Used for debugging and diagnostics in Developer Tools.
        /// </summary>
        public enum InitializationPhase
        {
            /// <summary>No initialization in progress.</summary>
            None,
            /// <summary>Connecting to the relay server.</summary>
            ConnectingToRelay,
            /// <summary>Sending gateway/session/create request.</summary>
            CreatingSession,
            /// <summary>Waiting for gateway/started response.</summary>
            WaitingForStarted,
            /// <summary>Waiting for session/initialized response.</summary>
            WaitingForInitialized
        }

        /// <summary>
        /// Fired when the provider changes. The parameter is the new provider ID.
        /// </summary>
        public static event Action<string> OnProviderChanged;

        /// <summary>
        /// Fired when the provider ready state changes.
        /// Parameters: (new ready state, error message if any).
        /// </summary>
        public static event Action<ProviderReadyState, string> OnReadyStateChanged;

        /// <summary>
        /// Fired when the initialization phase changes.
        /// Used by Developer Tools to show detailed initialization progress.
        /// </summary>
        public static event Action<InitializationPhase> OnPhaseChanged;

        /// <summary>
        /// The currently selected provider ID.
        /// </summary>
        public static string CurrentProviderId => s_CurrentProviderId;

        /// <summary>
        /// The current ready state of the provider.
        /// </summary>
        public static ProviderReadyState ReadyState => s_ReadyState;

        /// <summary>
        /// The initialization error message, if any.
        /// </summary>
        public static string InitializationError => s_InitializationError;

        /// <summary>
        /// The initialization error code, if any.
        /// </summary>
        public static string InitializationErrorCode => s_InitializationErrorCode;

        /// <summary>
        /// The current initialization phase for ACP providers.
        /// </summary>
        public static InitializationPhase CurrentPhase => s_CurrentPhase;

        /// <summary>
        /// Returns true if the current provider is ready to accept prompts.
        /// </summary>
        public static bool IsReady => s_ReadyState == ProviderReadyState.Ready;

        /// <summary>
        /// Returns true if a Unity profile is currently selected
        /// </summary>
        public static bool IsUnityProvider => AssistantProviderFactory.IsUnityProvider(s_CurrentProviderId);

        /// <summary>
        /// Sets the current provider and notifies subscribers.
        /// All providers start as Ready. Initializing state is set explicitly
        /// when a session is being created (ConversationLoad or EnsureSession).
        /// </summary>
        /// <param name="providerId">The provider ID to set.</param>
        public static void SetProvider(string providerId)
        {
            if (s_CurrentProviderId == providerId)
                return;

            AcpTracing.Observer.Debug($"observer.provider.changed: oldId={s_CurrentProviderId}, newId={providerId ?? AssistantProviderFactory.DefaultProvider.ProfileId}");

            s_CurrentProviderId = providerId ?? AssistantProviderFactory.DefaultProvider.ProfileId;

            // All providers start as Ready. Initializing state is set explicitly
            // when a session is being created (ConversationLoad or EnsureSession).
            s_ReadyState = ProviderReadyState.Ready;
            s_InitializationError = null;
            s_InitializationErrorCode = null;
            if (s_CurrentPhase != InitializationPhase.None)
            {
                s_CurrentPhase = InitializationPhase.None;
                var phaseCopy = s_CurrentPhase;
                Dispatch(() => OnPhaseChanged?.Invoke(phaseCopy));
            }

            var providerCopy = s_CurrentProviderId;
            var readyCopy = s_ReadyState;
            var errorCopy = s_InitializationError;
            Dispatch(() => OnProviderChanged?.Invoke(providerCopy));
            Dispatch(() => OnReadyStateChanged?.Invoke(readyCopy, errorCopy));
        }

        /// <summary>
        /// Sets the provider ready state and notifies subscribers.
        /// </summary>
        /// <param name="state">The new ready state.</param>
        /// <param name="errorMessage">Optional error message for Error state.</param>
        public static void SetReadyState(ProviderReadyState state, string errorMessage = null, string errorCode = null)
        {
            if (state != ProviderReadyState.Error)
            {
                errorMessage = null;
                errorCode = null;
            }

            if (s_ReadyState == state && s_InitializationError == errorMessage && s_InitializationErrorCode == errorCode)
                return;

            AcpTracing.Observer.Debug($"observer.ready.changed: oldState={s_ReadyState}, newState={state}, error={errorMessage ?? "(none)"}");

            s_ReadyState = state;
            s_InitializationError = errorMessage;
            s_InitializationErrorCode = errorCode;
            var stateCopy = s_ReadyState;
            var errorCopy = s_InitializationError;
            Dispatch(() => OnReadyStateChanged?.Invoke(stateCopy, errorCopy));
        }

        /// <summary>
        /// Sets the current initialization phase and notifies subscribers.
        /// Used by ACP session startup to report progress.
        /// </summary>
        /// <param name="phase">The new initialization phase.</param>
        public static void SetPhase(InitializationPhase phase)
        {
            if (s_CurrentPhase == phase)
                return;

            AcpTracing.Observer.Debug($"observer.phase.changed: oldPhase={s_CurrentPhase}, newPhase={phase}");

            s_CurrentPhase = phase;
            var phaseCopy = s_CurrentPhase;
            Dispatch(() => OnPhaseChanged?.Invoke(phaseCopy));
        }

        /// <summary>
        /// Resets the provider state to Unity/Ready.
        /// Call this when cleaning up a non-Unity provider session (e.g., window close during initialization).
        /// </summary>
        public static void Reset()
        {
            AcpTracing.Observer.Debug($"observer.reset");

            s_CurrentProviderId = AssistantProviderFactory.DefaultProvider.ProfileId;
            s_ReadyState = ProviderReadyState.Ready;
            s_InitializationError = null;
            s_InitializationErrorCode = null;
            s_CurrentPhase = InitializationPhase.None;

            Dispatch(() => OnProviderChanged?.Invoke(AssistantProviderFactory.DefaultProvider.ProfileId));
            Dispatch(() => OnReadyStateChanged?.Invoke(ProviderReadyState.Ready, null));
            Dispatch(() => OnPhaseChanged?.Invoke(InitializationPhase.None));
        }

        static void Dispatch(Action action)
        {
            EditorTask.delayCall += () => action();
        }
    }
}
