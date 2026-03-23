using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Toolkit.Connect
{
    /// <summary>
    /// A serializable record to store cached Unity Connect information.
    /// Using a record provides value-based equality comparison for efficient change detection.
    /// This cache is resilient to intermittent connection failures and provides the last known good state.
    /// </summary>
    [Serializable]
    record ConnectInfoCache
    {
        // Values from reflection
        public bool isProjectInfoValid; // Now cached to be resilient

        // Values from CloudProjectSettings
        public string organizationKey;
        public string userId;
        public string userName;
        public string projectId;
        public bool projectBound;
        public string accessToken;
    }

    /// <summary>
    /// Tracks the state of a cache update operation for better logging and decision making.
    /// </summary>
    class CacheUpdateState
    {
        public bool isRawValid;        // Whether the raw connection info is valid
        public bool cacheLoaded;       // Whether the cache has been loaded from file
        public bool fetchSuccess;      // Whether data fetch was successful
        public bool dataChanged;       // Whether data has changed since last update
        public bool writeSuccess;      // Whether cache was successfully written
        public bool triggeredLoad;     // Whether a cache load was triggered
        public bool hasException;      // Whether an exception occurred
        public string exceptionType;   // Type of exception if any
        public bool loadingFromCache;  // Whether loading from cache instead of live data
        public bool mergedPartialData; // Whether partial data was merged in an invalid state
    }

    /// <summary>
    /// Provides cached Unity Connect information with automatic fallback and persistence.
    /// This class maintains a cache of Unity Connect data that is:
    /// - Updated only from the main thread for thread safety
    /// - Persisted to disk for resilience across sessions (only when connection is valid)
    /// - Only written when data actually changes to minimize I/O
    /// - Provides cached values when the raw connection state is unreliable
    /// - Merges in latest non-empty values (like accessToken) even when connection is invalid
    /// - Validates loaded data and rolls back on corruption
    /// </summary>
    static class UnityConnectProvider
    {
        static readonly int k_MainThreadId;
        const string k_DefaultCacheFilePath = "Temp/UnityConnectCache.json";
        static string s_ValidCacheFilePath = k_DefaultCacheFilePath;
        // The 'cachedInfo' is readonly, meaning we can't reassign the instance, but we can change its properties.
        internal static readonly ConnectInfoCache cachedInfo = new();
        static bool s_CacheLoadedFromFile;
        static bool isMainThread => Thread.CurrentThread.ManagedThreadId == k_MainThreadId;

        // Internal backup for rollback in case of cache corruption
        static ConnectInfoCache s_BackupCachedInfo;

        // Log deduplication state
        static string s_LastLogMessage = string.Empty;
        static DateTime s_LastLogTime = DateTime.MinValue;
        static readonly TimeSpan k_LogSuppressionWindow = TimeSpan.FromSeconds(60);

        // Public properties read from the cache, automatically triggering updates when needed
        // These properties are thread-safe and will return cached values on background threads
        public static string organizationKey
        {
            get
            {
                UpdateCache();
                return cachedInfo.organizationKey;
            }
        }

        public static string projectId
        {
            get
            {
                UpdateCache();
                return cachedInfo.projectId;
            }
        }

        public static bool projectBound
        {
            get
            {
                UpdateCache();
                return cachedInfo.projectBound;
            }
        }

        public static string userId
        {
            get
            {
                UpdateCache();
                return cachedInfo.userId;
            }
        }

        public static string userName
        {
            get
            {
                UpdateCache();
                return cachedInfo.userName;
            }
        }

        public static string accessToken
        {
            get
            {
                UpdateCache();
                return cachedInfo.accessToken;
            }
        }

        static UnityConnectProvider()
        {
            k_MainThreadId = Thread.CurrentThread.ManagedThreadId;
            if (unityConnectLogLevel > 0)
                Debug.Log($"[DevLog-UnityConnectUtils] Static constructor fired. Main thread ID captured: {k_MainThreadId}");
        }

        /// <summary>
        /// Core cache management logic.
        /// If the connection is valid, it fetches live data and persists it to a "last valid" cache file.
        /// If the connection is invalid, it loads the "last valid" cache and then merges in any
        /// non-empty values from the current (invalid) CloudProjectSettings, like a new access token.
        /// This merged state is NOT persisted, preserving the integrity of the "last valid" cache file.
        ///
        /// This ensures that fresh data like an access token is available immediately on startup,
        /// even if projectInfo.valid is initially false or if a cache file doesn't exist yet.
        /// </summary>
        internal static void UpdateCache()
        {
            if (!isMainThread)
                return;

            var state = new CacheUpdateState
            {
                isRawValid = UnityConnectUtils.GetIsProjectInfoValidRaw(),
                cacheLoaded = s_CacheLoadedFromFile
            };

            try
            {
                if (state.isRawValid)
                {
                    // --- VALID CONNECTION PATH ---
                    // Fetch live data and save it as the new "last known good" state.
                    try
                    {
                        var liveInfo = new ConnectInfoCache
                        {
                            isProjectInfoValid = true,
                            organizationKey = CloudProjectSettings.organizationKey,
                            userId = CloudProjectSettings.userId,
                            userName = CloudProjectSettings.userName,
                            projectId = CloudProjectSettings.projectId,
                            projectBound = CloudProjectSettings.projectBound,
                            accessToken = CloudProjectSettings.accessToken
                        };

                        state.fetchSuccess = true;

                        if (!liveInfo.Equals(cachedInfo))
                        {
                            state.dataChanged = true;
                            s_BackupCachedInfo = cachedInfo with { };

                            if (ValidateCacheData(liveInfo))
                            {
                                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(liveInfo), cachedInfo);

                                var json = JsonUtility.ToJson(cachedInfo, true);
                                var cachePath = GetValidCacheFilePath();
                                Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
                                File.WriteAllText(cachePath, json);
                                state.writeSuccess = true;
                            }
                            else
                            {
                                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(s_BackupCachedInfo), cachedInfo);
                                Debug.LogWarning("[UnityConnectUtils] Live data validation failed, keeping previous cache state");
                            }
                        }
                        s_CacheLoadedFromFile = true; // In-memory cache is now fresh.
                    }
                    catch (Exception ex)
                    {
                        state.hasException = true;
                        state.exceptionType = ex.GetType().Name;
                        Debug.LogError($"[UnityConnectUtils] Failed to get live data or save cache: {ex.Message}");
                    }
                }
                else
                {
                    // --- INVALID CONNECTION PATH ---
                    // Load the "last known good" state, then merge any available new data on top.
                    state.loadingFromCache = true;
                    if (!s_CacheLoadedFromFile)
                    {
                        state.triggeredLoad = true;
                        LoadCacheFromFile(); // Loads the last valid state.
                    }

                    // Now, merge latest partial data (like a new access token) into the in-memory cache.
                    // This is critical for startup scenarios where an access token might be available
                    // from CloudProjectSettings before `projectInfo.valid` becomes true.
                    // This merged state does NOT get written to disk, preserving the "last valid" cache file.
                    state.mergedPartialData = MergeWithLatestPartialData();
                    cachedInfo.isProjectInfoValid = false; // Reflect the current invalid state in memory.
                }
            }
            finally
            {
                if (unityConnectLogLevel > 0)
                {
                    LogWithDeduplication(state, state.hasException, !state.isRawValid && !state.mergedPartialData);
                }
            }
        }

        /// <summary>
        /// Merges non-empty string values and boolean flags from the current CloudProjectSettings
        /// into the in-memory cache. This is used when the connection is invalid to update fields.
        /// For booleans like `projectBound`, it uses a logical OR to preserve the 'bound' state.
        /// </summary>
        /// <returns>True if any data was merged, otherwise false.</returns>
        static bool MergeWithLatestPartialData()
        {
            var changed = false;

            // Create a temporary holder for the latest data to compare against the cache
            var latestPartial = new {
                CloudProjectSettings.accessToken,
                CloudProjectSettings.organizationKey,
                CloudProjectSettings.userId,
                CloudProjectSettings.userName,
                CloudProjectSettings.projectId,
                CloudProjectSettings.projectBound
            };

            if (!string.IsNullOrEmpty(latestPartial.accessToken) && cachedInfo.accessToken != latestPartial.accessToken)
            {
                cachedInfo.accessToken = latestPartial.accessToken;
                changed = true;
            }
            if (!string.IsNullOrEmpty(latestPartial.organizationKey) && cachedInfo.organizationKey != latestPartial.organizationKey)
            {
                cachedInfo.organizationKey = latestPartial.organizationKey;
                changed = true;
            }
            if (!string.IsNullOrEmpty(latestPartial.userId) && cachedInfo.userId != latestPartial.userId)
            {
                cachedInfo.userId = latestPartial.userId;
                changed = true;
            }
            if (!string.IsNullOrEmpty(latestPartial.userName) && cachedInfo.userName != latestPartial.userName)
            {
                cachedInfo.userName = latestPartial.userName;
                changed = true;
            }
            if (!string.IsNullOrEmpty(latestPartial.projectId) && cachedInfo.projectId != latestPartial.projectId)
            {
                cachedInfo.projectId = latestPartial.projectId;
                changed = true;
            }

            // Merge projectBound using a logical OR.
            // If it was ever true, it stays true. It only becomes true if the latest value is true.
            var newProjectBound = cachedInfo.projectBound || latestPartial.projectBound;
            if (cachedInfo.projectBound != newProjectBound)
            {
                cachedInfo.projectBound = newProjectBound;
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// Logs a message with deduplication to prevent the same message from being printed twice within a 2-second window.
        /// Creates natural language descriptions based on boolean state values.
        /// </summary>
        static void LogWithDeduplication(CacheUpdateState state, bool isError, bool isWarning)
        {
            var now = DateTime.UtcNow;

            // Generate natural language message from state
            var naturalMessage = CreateNaturalMessage(state);

            // Check if this is a duplicate message within the suppression window
            if (naturalMessage == s_LastLogMessage && now - s_LastLogTime < k_LogSuppressionWindow)
                return; // Suppress duplicate log

            // Update last log state
            s_LastLogMessage = naturalMessage;
            s_LastLogTime = now;

            // Log with appropriate level
            if (isError)
                Debug.LogError(naturalMessage);
            else if (isWarning)
                Debug.LogWarning(naturalMessage);
            else
                Debug.Log(naturalMessage);
        }

        /// <summary>
        /// Creates a natural language message from boolean state values.
        /// </summary>
        static string CreateNaturalMessage(CacheUpdateState state)
        {
            var parts = new List<string>
            {
                "[DevLog-UnityConnectUtils] Unity Connect update:",
                state.isRawValid ? "connection is valid" : "connection is invalid",
                state.cacheLoaded ? "cache already loaded" : "cache not yet loaded"
            };

            if (state.loadingFromCache)
            {
                parts.Add("using cached data");
                if (state.triggeredLoad)
                {
                    parts.Add("loading cache from file");
                }
                if (state.mergedPartialData)
                {
                    parts.Add("merged latest partial data (e.g., new access token)");
                }
            }
            else
            {
                if (state.fetchSuccess)
                {
                    parts.Add("data fetched successfully");
                    parts.Add(state.dataChanged ? "data has changed" : "data unchanged");

                    if (state.dataChanged)
                    {
                        parts.Add(state.writeSuccess ? "last valid cache saved to disk" : "cache save failed");
                    }
                }

                if (state.hasException)
                {
                    parts.Add($"exception={state.exceptionType}");
                }
            }

            return string.Join(" | ", parts);
        }

        /// <summary>
        /// Loads the last known valid Unity Connect data from the persisted file.
        /// Falls back to default values if the cache file is missing or corrupted.
        /// </summary>
        static void LoadCacheFromFile()
        {
            var path = GetValidCacheFilePath();
            if (File.Exists(path))
            {
                var backupInfo = cachedInfo with { };
                try
                {
                    var json = File.ReadAllText(path);
                    var loadedInfo = JsonUtility.FromJson<ConnectInfoCache>(json);

                    if (ValidateCacheData(loadedInfo))
                    {
                        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(loadedInfo), cachedInfo);
                        s_CacheLoadedFromFile = true;
                    }
                    else
                    {
                        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(backupInfo), cachedInfo);
                        Debug.LogWarning($"[UnityConnectUtils] Cache validation failed after loading from '{path}'. Reverted to previous state.");
                        s_CacheLoadedFromFile = false;
                    }
                }
                catch (Exception e)
                {
                    JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(backupInfo), cachedInfo);
                    Debug.LogWarning($"[UnityConnectUtils] Failed to load Unity Connect cache from '{path}'. Reverted to previous state. Error: {e.Message}");
                    s_CacheLoadedFromFile = false;
                }
            }
        }

        /// <summary>
        /// Validates cache data to ensure it's not corrupted.
        /// Basic validation to catch obvious corruption cases.
        /// </summary>
        static bool ValidateCacheData(ConnectInfoCache data) => data is
            { organizationKey: not null, userId: not null, userName: not null, projectId: not null, accessToken: not null };

        /// <summary>
        /// Gets the file path for the last valid Unity Connect cache.
        /// Cache is stored in the project's Temp folder to avoid source control issues.
        /// </summary>
        static string GetValidCacheFilePath()
        {
            if (string.IsNullOrEmpty(s_ValidCacheFilePath) || s_ValidCacheFilePath == k_DefaultCacheFilePath)
            {
                var projectRoot = Path.GetDirectoryName(Application.dataPath);
                if (projectRoot != null)
                {
                    var tempFolderPath = Path.Combine(projectRoot, "Temp");
                    // Renamed the file to be more explicit about its purpose.
                    s_ValidCacheFilePath = Path.Combine(tempFolderPath, "UnityConnectCache.json");
                }
                else
                {
                    Debug.LogError("[UnityConnectUtils] Could not determine project root from Application.dataPath.");
                    s_ValidCacheFilePath = k_DefaultCacheFilePath;
                }
            }
            return s_ValidCacheFilePath;
        }

        const string k_InternalMenu = "internal:";
        const string k_UnityConnectLogLevelMenu = "AI Toolkit/Internals/Log All UnityConnect Provider Messages";
        const string k_UnityConnectLogLevelKey = "AI_Toolkit_UnityConnect_Log_Level";

        internal static int unityConnectLogLevel
        {
            get => EditorPrefs.GetInt(k_UnityConnectLogLevelKey, 0);
            set => EditorPrefs.SetInt(k_UnityConnectLogLevelKey, value);
        }

        [MenuItem(k_InternalMenu + k_UnityConnectLogLevelMenu, false, 1020)]
        static void ToggleUnityConnectLogLevel()
        {
            unityConnectLogLevel = unityConnectLogLevel == 1 ? 0 : 1;
        }
        [MenuItem(k_InternalMenu + k_UnityConnectLogLevelMenu, true, 1020)]
        static bool ValidateUnityConnectLogLevel()
        {
            Menu.SetChecked(k_UnityConnectLogLevelMenu, unityConnectLogLevel == 1);
            return true;
        }
    }
}
