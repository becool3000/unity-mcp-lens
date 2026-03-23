using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using AiEditorToolsSdk;
using AiEditorToolsSdk.Components.Common.Enums;
using AiEditorToolsSdk.Components.Common.Responses.Wrappers;
using AiEditorToolsSdk.Components.Organization;
using AiEditorToolsSdk.Components.Organization.Responses;
using AiEditorToolsSdk.Domain.Abstractions.Services;
using Unity.AI.Toolkit.Accounts.Services.States;
using Unity.AI.Toolkit.Connect;
using UnityEditor;
using UnityEngine;
using Logger = Unity.AI.Toolkit.Accounts.Services.Sdk.Logger;

namespace Unity.AI.Toolkit.Accounts.Services.Core
{
    static class AccountApi
    {
        [InitializeOnLoadMethod]
        static void InitializeEnvironmentKeys()
        {
            Environment.RegisterEnvironmentKey(accountEnvironmentKey, "Account Environment", _ =>
            {
                Account.settings.Refresh();
                Account.pointsBalance.Refresh();
            });
            InitializeCacheTimer();
        }

        public const string accountEnvironmentKey = "AI_Toolkit_Account_Environment";

        public static string selectedEnvironment => Environment.GetSelectedEnvironment(accountEnvironmentKey);

        static string s_LastLoggedError = string.Empty;
        static string s_LastLoggedException = string.Empty;

        static readonly string k_SessionTraceId = Guid.NewGuid().ToString();

        static readonly int[] k_TimeoutDurationsDisconnect = { 2, 4, 8 };
        static readonly int[] k_TimeoutDurationsReconnect = { 4, 8, 16, 32 };

        // Cache for in-progress tasks
        static readonly ConcurrentDictionary<Type, Task> k_TaskCache = new();
        static Timer s_CacheClearTimer;

        class TraceIdProvider : ITraceIdProvider
        {
            readonly string m_SessionId;

            public TraceIdProvider(string sessionId) => m_SessionId = sessionId;

            public Task<string> GetTraceId() => Task.FromResult(m_SessionId);
        }

        class PackageInfoProvider : IPackageInfoProvider
        {
            static UnityEditor.PackageManager.PackageInfo s_PackageInfo;
            public string PackageName { get; }
            public string PackageVersion { get; }

            public PackageInfoProvider()
            {
                if (s_PackageInfo == null)
                {
                    s_PackageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PackageInfoProvider).Assembly);
                }

                if (s_PackageInfo != null)
                {
                    PackageName = s_PackageInfo.name;
                    PackageVersion = s_PackageInfo.version;
                }
            }
        }

        static void InitializeCacheTimer()
        {
            s_CacheClearTimer = new Timer(ClearStaleCache, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
            EditorApplication.quitting += DisposeCacheTimer;
        }

        static void ClearStaleCache(object state)
        {
            if (k_TaskCache.IsEmpty)
                return;

            // Get the key-value pairs of completed tasks. This is a snapshot.
            var staleEntries = k_TaskCache.Where(kvp => kvp.Value.IsCompleted).ToList();
            foreach (var entry in staleEntries)
            {
                // This will only remove the entry if the key still points to the exact same
                // completed task instance. If another thread has replaced it with a new
                // task, this Remove operation will correctly do nothing.
                ((ICollection<KeyValuePair<Type, Task>>)k_TaskCache).Remove(entry);
            }
        }

        static void DisposeCacheTimer()
        {
            s_CacheClearTimer?.Dispose();
            s_CacheClearTimer = null;
            EditorApplication.quitting -= DisposeCacheTimer;
        }

        /// <summary>
        /// Performs an API request with a multi-layered retry strategy.
        /// </summary>
        static async Task<TResponse> Request<TResponse>(Func<IOrganizationComponent, CancellationToken, Task<OperationResult<TResponse>>> callback) where TResponse : class
        {
            try
            {
                // hysteresis
                var timeoutDurations = Account.settings?.Value == null ? k_TimeoutDurationsReconnect : k_TimeoutDurationsDisconnect;

                await ApiAccessibleState.WaitForCloudProjectSettings();

                using var editorFocus = new EditorAsyncKeepAliveScope("Verifying account settings.");

                OperationResult<TResponse> result = null;
                // This loop attempts the operation with increasingly longer deadlines.
                var retryAttempt = 0;
                for (; retryAttempt < timeoutDurations.Length; retryAttempt++)
                {
                    try
                    {
                        var timeSpan = TimeSpan.FromSeconds(timeoutDurations[retryAttempt]);
                        var builder = Builder.Build(orgId: UnityConnectProvider.organizationKey, userId: UnityConnectProvider.userId,
                            projectId: UnityConnectProvider.projectId, httpClient: HttpClientManager.instance, baseUrl: selectedEnvironment,
                            logger: new Logger(), unityAuthenticationTokenProvider: new Auth(), traceIdProvider: new TraceIdProvider(k_SessionTraceId),
                            defaultOperationTimeout: timeSpan, packageInfoProvider: new PackageInfoProvider());
                        var component = builder.OrganizationComponent();

                        using var tokenSource = new CancellationTokenSource(timeSpan);
                        var timeoutToken = tokenSource.Token;

                        result = await callback(component, timeoutToken);

                        if (result.Result.IsSuccessful)
                        {
                            // Success: The operation completed within our deadline.
                            return result.Result.Value;
                        }

                        if (result.Result.Error.AiResponseError is AiResultErrorEnum.ApiNoLongerSupported or AiResultErrorEnum.UnavailableForLegalReasons)
                        {
                            // Definitive failure: No point in retrying further.
                            break;
                        }

                        // retry after waiting for the remainder of the time span (timeoutToken) or thirty seconds
                        await EditorTask.Delay(TimeSpan.FromSeconds(30), timeoutToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // retry
                    }
                }

                // Handle the final result after all attempts have been exhausted or a definitive failure was encountered.
                if (result != null)
                {
                    if (result.Result.Error.AiResponseError == AiResultErrorEnum.UnavailableForLegalReasons)
                        Account.settings.RegionAvailable = false;

                    if (result.Result.Error.AiResponseError == AiResultErrorEnum.ApiNoLongerSupported)
                        Account.settings.PackagesSupported = false;

                    var errorMessage = result.Result.Error.AiResponseError == AiResultErrorEnum.RateLimitExceeded // typically means wrong url (staging vs prod)
                        ? $"Account information returned {result.Result.Error.AiResponseError} on Url '{selectedEnvironment}'. Is the Url correct?\nTrace Id {result.SdkTraceId} => {result.W3CTraceId}"
                        : $"Error after {retryAttempt + 1} attempt(s): {result.Result.Error.AiResponseError} - {result.Result.Error.Errors.FirstOrDefault()} -- Result type: {typeof(TResponse).Name} -- Url: {selectedEnvironment} -- Trace Id {result.SdkTraceId} => {result.W3CTraceId}";
                    if (!string.IsNullOrEmpty(UnityConnectProvider.organizationKey) && errorMessage != s_LastLoggedError)
                    {
                        Debug.Log(errorMessage);
                        s_LastLoggedError = errorMessage;
                    }
                }
            }
            catch (Exception exception)
            {
                // This outer catch handles any unexpected exceptions not caught by the inner retry loop.
                var exceptionMessage = exception.ToString();
                if (!string.IsNullOrEmpty(UnityConnectProvider.organizationKey) && exceptionMessage != s_LastLoggedException)
                {
                    Debug.Log($"Exception after retry attempts: {exceptionMessage}");
                    s_LastLoggedException = exceptionMessage;
                }
            }

            return null;
        }

        static Task<T> GetOrCreateCachedTask<T>(Func<Task<T>> taskFactory) where T : class
        {
            var type = typeof(T);
            var task = k_TaskCache.GetOrAdd(type, _ => taskFactory());

            // If the task from the cache was not completed, so we can safely return it for the caller to await.
            if (!task.IsCompleted)
                return (Task<T>)task;

            // The task is stale. We will try to replace it with a new one.
            // This is an atomic operation that only succeeds if the current value
            // in the dictionary is still the stale 'task' we just retrieved.
            var newTask = taskFactory();
            if (k_TaskCache.TryUpdate(type, newTask, task))
                return newTask;

            // We lost the race. Another thread already replaced the stale task.
            // The dictionary now contains a fresh task, so we get it again.
            // GetOrAdd is the safest way to do this, as the factory won't be called.
            return (Task<T>)k_TaskCache.GetOrAdd(type, _ => taskFactory());
        }

        internal static Func<Task<SettingsResult>> GetSettingsDelegate = () => Request((component, ct) => component.GetSettings(cancellationToken: ct));
        internal static Func<Task<PointsBalanceResult>> GetPointsDelegate = () => Request((component, ct) => component.GetPointsBalance(cancellationToken: ct));

        internal static Task<SettingsResult> GetSettings() =>
            GetOrCreateCachedTask(GetSettingsDelegate);

        internal static Task<PointsBalanceResult> GetPointsBalance() =>
            GetOrCreateCachedTask(GetPointsDelegate);

        internal static Task<SettingsResult> SetTermsOfServiceAcceptance(bool value) =>
            Request((component, ct) => component.SetTermsOfServiceAcceptance(value, cancellationToken: ct));
    }

    static class HttpClientManager
    {
        static HttpClient s_Instance;

        public static HttpClient instance
        {
            get { return s_Instance ??= new HttpClient(); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Initialize()
        {
            s_Instance = null;
            Application.quitting += Dispose;
        }

        static void Dispose()
        {
            Application.quitting -= Dispose;

            if (s_Instance != null)
            {
                s_Instance.Dispose();
                s_Instance = null;
            }
        }
    }
}
