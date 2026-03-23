using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Unity.AI.Toolkit.Accounts.Services;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Toolkit.Connect
{
    class ChangeInfo
    {
        public bool registered;
        public Func<Task> onChange;
        public Delegate eventDelegate;
    }

    enum UnityConnectEvents
    {
        StateChanged,
        ProjectRefreshed,
        ProjectStateChanged,
        UserStateChanged
    }

    /// <summary>
    /// Provides access to Unity Connect services through reflection and cached data.
    ///
    /// This class serves as the main interface for Unity Connect information and handles:
    /// - Reflection-based access to internal Unity Connect APIs
    /// - Thread-safe cached data access via UnityConnectProvider
    /// - Event registration for Unity Connect state changes
    /// - Fallback mechanisms for unreliable connection states
    ///
    /// All public methods that return connection data are backed by a persistent cache
    /// that is automatically managed by UnityConnectProvider. The cache provides resilience
    /// against intermittent connection failures and ensures consistent data access across threads.
    /// </summary>
    static class UnityConnectUtils
    {
        /// <summary>
        /// Method used to get organization's foreign key
        /// </summary>
        static MethodInfo GetOrganizationForeignKey => s_GetOrganizationForeignKey ??= k_UnityConnectType.GetMethod("GetOrganizationForeignKey");
        static MethodInfo s_GetOrganizationForeignKey;

        static MethodInfo GetShowLogin => s_ShowLogin ??= k_UnityConnectType.GetMethod("ShowLogin");
        static MethodInfo s_ShowLogin;

        static PropertyInfo GetProjectInfo => s_GetProjectInfo ??= k_UnityConnectType.GetProperty("projectInfo");
        static PropertyInfo s_GetProjectInfo;

        /// <summary>
        /// Property used to get whether or not user is logged in
        /// </summary>
        static PropertyInfo LoggedInProperty => s_LoggedInProperty ??= k_UnityConnectType.GetProperty("loggedIn");
        static PropertyInfo s_LoggedInProperty;

        /// <summary>
        /// Property used to know if user info is ready to be considered
        /// </summary>
        static PropertyInfo isUserInfoReady => s_IsUserInfoReady ??= k_UnityConnectType.GetProperty("isUserInfoReady");
        static PropertyInfo s_IsUserInfoReady;

        /// <summary>
        /// Property used to get CloudProjectSettings configuration
        /// </summary>
        static PropertyInfo ConfigurationProperty => s_ConfigurationProperty ??= k_UnityConnectType.GetProperty("configuration");
        static PropertyInfo s_ConfigurationProperty;

        static readonly Assembly k_ConnectAssembly = typeof(CloudProjectSettings).Assembly;
        static readonly Type k_UnityConnectType = k_ConnectAssembly.GetType("UnityEditor.Connect.UnityConnect");

        static object Instance =>
            s_Instance ??= k_UnityConnectType
                .GetProperty("instance", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null, null);
        static object s_Instance;
        static readonly Dictionary<UnityConnectEvents, EventInfo> k_Events = new();

        /// <summary>
        /// Get event info for event with passed id
        /// </summary>
        /// <param name="eventId"> ID of event to get info for </param>
        /// <returns> Requested event info </returns>
        public static EventInfo GetEventInfo(UnityConnectEvents eventId)
        {
            if (!k_Events.ContainsKey(eventId))
                k_Events[eventId] = k_UnityConnectType.GetEvent(eventId.ToString());
            return k_Events[eventId];
        }

        /// <summary>
        /// Get the current user's organization ID
        /// </summary>
        /// <returns> Current user's organization ID </returns>
        /// <exception cref="Exception"> Throws exception when organization ID cannot be fetched </exception>
        public static string GetUserOrganizationId()
        {
            try
            {
                return (string) GetOrganizationForeignKey.Invoke(Instance, null);
            }
            catch (Exception exception)
            {
                throw new Exception($"Could not fetch CloudProjectSettings organization ID: {exception.Message}");
            }
        }

        /// <summary>
        /// Clears the access token using Unity Connect API.
        /// </summary>
        public static void ClearAccessToken()
        {
            try
            {
                k_UnityConnectType.GetMethod("ClearAccessToken")?.Invoke(Instance, null);
            }
            catch (Exception exception)
            {
                throw new Exception($"Could not clear access token: {exception.Message}");
            }
        }

        /// <summary>
        /// Is the user currently logged in?
        /// </summary>
        /// <returns> True if the user is currently logged in </returns>
        /// <exception cref="Exception"> Throws exception when user's login state cannot be fetched </exception>
        public static bool GetIsLoggedIn()
        {
            try
            {
                return (bool) LoggedInProperty.GetValue(Instance);
            }
            catch (Exception exception)
            {
                throw new Exception($"Could not fetch CloudProjectSettings log-in state: {exception.Message}");
            }
        }

        /// <summary>
        /// Is the user info ready?
        /// </summary>
        /// <returns> True if the user is currently logged in </returns>
        /// <exception cref="Exception"> Throws exception when user's login state cannot be fetched </exception>
        public static bool GetIsUserInfoReady()
        {
            try
            {
                return (bool) isUserInfoReady.GetValue(Instance);
            }
            catch (Exception exception)
            {
                throw new Exception($"Could not fetch CloudProjectSettings is user ready state: {exception.Message}");
            }
        }

        /// <summary>
        /// Reads the current raw value of UnityConnect.projectInfo.valid without any caching.
        /// This is used internally by the caching system to determine when to refresh cached data.
        ///
        /// Warning: This method bypasses the cache and may return unreliable values due to
        /// intermittent connection issues. Use GetIsProjectInfoValid() for stable results.
        /// </summary>
        public static bool GetIsProjectInfoValidRaw()
        {
            if (GetSimulatedBrokenState())
                return false;

            try
            {
                var projectInfo = GetProjectInfo?.GetValue(Instance);
                if (projectInfo == null) return false;
                var validProperty = projectInfo.GetType().GetProperty("valid");
                if (validProperty == null) return false;
                return (bool)validProperty.GetValue(projectInfo);
            }
            catch (Exception ex)
            {
                if (Unsupported.IsDeveloperMode())
                    Debug.LogWarning($"[DevLog-UnityConnectUtils] Exception in GetIsProjectInfoValidRaw(): {ex}");
                return false;
            }
        }

        static bool s_SimulateBrokenState;

        /// <summary>
        /// This is only for testing purposes.
        /// </summary>
        /// <param name="value"></param>
        internal static void SimulateBrokenState(bool value)
        {
            s_SimulateBrokenState = value;
            Account.cloudConnected.Refresh();
        }

        /// <summary>
        /// This is only for testing purposes.
        /// </summary>
        internal static bool GetSimulatedBrokenState() => s_SimulateBrokenState;

        /// <summary>
        /// Gets the project validity status from cached data.
        /// This method provides resilience against intermittent connection failures
        /// by returning the last known valid state from the cache.
        ///
        /// The underlying cache is updated automatically when called from the main thread
        /// and uses the raw projectInfo.valid as a signal for data freshness.
        /// </summary>
        public static bool GetIsProjectInfoValid()
        {
            UnityConnectProvider.UpdateCache();
            return UnityConnectProvider.cachedInfo.isProjectInfoValid;
        }

        /// <summary>
        /// Get whether or not the editor is currently in staging
        /// </summary>
        /// <returns> True if the editor is currently in staging </returns>
        /// <exception cref="Exception"> Throws exception when editor's current environment cannot be fetched </exception>
        public static bool GetIsStaging()
        {
            try
            {
                string environment = (string)ConfigurationProperty.GetValue(Instance);
                return string.Equals(environment, "staging");
            }
            catch (Exception exception)
            {
                throw new Exception($"Could not fetch CloudProjectSettings environment: {exception.Message}");
            }
        }

        /// <summary>
        /// Opens the Unity Connect login window.
        /// </summary>
        public static void ShowLogin()
        {
            GetShowLogin.Invoke(Instance, null);
        }

        /// <summary>
        /// Registers a callback for user state changes.
        /// </summary>
        public static Delegate RegisterUserStateChangedEvent(Action<object> changed) =>
            RegisterUnityConnectChangedEvent(changed, UnityConnectEvents.UserStateChanged);
        /// <summary>
        /// Registers a callback for project state changes.
        /// </summary>
        public static Delegate RegisterProjectStateChangedEvent(Action<object> changed) =>
            RegisterUnityConnectChangedEvent(changed, UnityConnectEvents.ProjectStateChanged);
        /// <summary>
        /// Registers a callback for general connect state changes.
        /// </summary>
        public static Delegate RegisterConnectStateChangedEvent(Action<object> changed) =>
            RegisterUnityConnectChangedEvent(changed, UnityConnectEvents.StateChanged);
        /// <summary>
        /// Unregisters a callback for user state changes.
        /// </summary>
        public static void UnregisterUserStateChangedEvent(Delegate attachedDelegate) =>
            UnregisterUnityConnectStateChangedEvent(attachedDelegate, UnityConnectEvents.UserStateChanged);
        /// <summary>
        /// Unregisters a callback for project state changes.
        /// </summary>
        public static void UnregisterProjectStateChangedEvent(Delegate attachedDelegate) =>
            UnregisterUnityConnectStateChangedEvent(attachedDelegate, UnityConnectEvents.ProjectStateChanged);
        /// <summary>
        /// Unregisters a callback for general connect state changes.
        /// </summary>
        public static void UnregisterConnectStateChangedEvent(Delegate attachedDelegate) =>
            UnregisterUnityConnectStateChangedEvent(attachedDelegate, UnityConnectEvents.StateChanged);

        /// <summary>
        /// Register callback to UnityConnectEvent
        /// </summary>
        /// <param name="callback"> Callback to register </param>
        /// <param name="eventId"> Event to register to </param>
        /// <returns></returns>
        /// <exception cref="Exception"> Event cannot be registered to </exception>
        static Delegate RegisterUnityConnectChangedEvent(Action<object> callback, UnityConnectEvents eventId)
        {
            try
            {
                UnityConnectProvider.UpdateCache();
                var userChangedEvent = GetEventInfo(eventId);
                var convertedHandler = Delegate.CreateDelegate(
                    userChangedEvent.EventHandlerType,
                    callback.Target,
                    callback.Method);
                userChangedEvent.AddEventHandler(Instance, convertedHandler);
                return convertedHandler;
            }
            catch(Exception exception)
            {
                throw new Exception($"Could not register to change event: {exception.Message}");
            }
        }

        /// <summary>
        /// Unregister callback from UnityConnectEvent
        /// </summary>
        /// <param name="attachedDelegate"> Callback to unregister </param>
        /// <param name="eventId"> Event to unregister from </param>
        /// <returns></returns>
        /// <exception cref="Exception"> Event cannot be unregistered </exception>
        static void UnregisterUnityConnectStateChangedEvent(Delegate attachedDelegate, UnityConnectEvents eventId)
        {
            try
            {
                UnityConnectProvider.UpdateCache();
                var userChangedEvent = GetEventInfo(eventId);
                userChangedEvent.RemoveEventHandler(Instance, attachedDelegate);
            }
            catch(Exception exception)
            {
                throw new Exception($"Could not de-register to change event: {exception.Message}");
            }
        }
    }
}
