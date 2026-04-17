#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEditor;

namespace Becool.UnityMcpLens.Editor.Tracing
{
    /// <summary>
    /// Manages per-sink TraceConfig persistence using EditorUserSettings.
    /// Supports sink keys: "unity.file", "unity.console", "relay.file", "relay.console".
    /// </summary>
    static class TraceSinkConfigManager
    {
        const string k_SettingKey = "Trace.SinkConfigs";

        static Dictionary<string, TraceConfig> s_Configs;
        static readonly object s_Lock = new();

        /// <summary>
        /// Default configs for each sink (hardcoded, shared with relay).
        /// </summary>
        static readonly Dictionary<string, TraceConfig> k_Defaults = new()
        {
            { "unity.file", new TraceConfig { DefaultLevel = "debug" } },
            { "unity.console", new TraceConfig { DefaultLevel = "warn" } },
            { "relay.file", new TraceConfig { DefaultLevel = "debug" } },
            { "relay.console", new TraceConfig { DefaultLevel = "info" } },
        };

        /// <summary>
        /// Get the TraceConfig for a specific sink.
        /// Returns a clone to prevent accidental modification.
        /// </summary>
        /// <param name="sinkKey">Sink key, e.g. "unity.file", "relay.console"</param>
        public static TraceConfig GetSinkConfig(string sinkKey)
        {
            EnsureLoaded();
            lock (s_Lock)
            {
                if (s_Configs.TryGetValue(sinkKey, out var config))
                    return CloneConfig(config);

                // Return default if not stored
                if (k_Defaults.TryGetValue(sinkKey, out var defaultConfig))
                    return CloneConfig(defaultConfig);

                return new TraceConfig { DefaultLevel = "info" };
            }
        }

        /// <summary>
        /// Set the TraceConfig for a specific sink and persist to EditorUserSettings.
        /// </summary>
        /// <param name="sinkKey">Sink key, e.g. "unity.file", "relay.console"</param>
        /// <param name="config">The config to store</param>
        public static void SetSinkConfig(string sinkKey, TraceConfig config)
        {
            EnsureLoaded();
            lock (s_Lock)
            {
                s_Configs[sinkKey] = CloneConfig(config);
                SaveToSettings();
            }
        }

        /// <summary>
        /// Check if a sink config differs from the default.
        /// Used to determine if relay config file should be written.
        /// </summary>
        public static bool IsDefaultConfig(string sinkKey)
        {
            EnsureLoaded();
            lock (s_Lock)
            {
                if (!s_Configs.TryGetValue(sinkKey, out var config))
                    return true; // Not stored = using default

                if (!k_Defaults.TryGetValue(sinkKey, out var defaultConfig))
                    return false; // No default = not default

                return ConfigEquals(config, defaultConfig);
            }
        }

        /// <summary>
        /// Get all relay sink configs for writing to config file.
        /// Returns (fileConfig, consoleConfig).
        /// </summary>
        public static (TraceConfig file, TraceConfig console) GetRelayConfigs()
        {
            return (GetSinkConfig("relay.file"), GetSinkConfig("relay.console"));
        }

        /// <summary>
        /// Check if relay configs are at defaults (no need to write config file).
        /// </summary>
        public static bool AreRelayConfigsDefault()
        {
            return IsDefaultConfig("relay.file") && IsDefaultConfig("relay.console");
        }

        static void EnsureLoaded()
        {
            if (s_Configs != null) return;

            lock (s_Lock)
            {
                if (s_Configs != null) return;
                LoadFromSettings();
            }
        }

        static void LoadFromSettings()
        {
            s_Configs = new Dictionary<string, TraceConfig>();

            var json = EditorUserSettings.GetConfigValue(k_SettingKey);
            if (string.IsNullOrEmpty(json))
                return;

            try
            {
                var stored = JsonConvert.DeserializeObject<Dictionary<string, TraceConfig>>(json);
                if (stored != null)
                    s_Configs = stored;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[TraceSinkConfigManager] Failed to load config: {ex.Message}");
            }
        }

        static void SaveToSettings()
        {
            try
            {
                var json = JsonConvert.SerializeObject(s_Configs, Formatting.None,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                EditorUserSettings.SetConfigValue(k_SettingKey, json);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[TraceSinkConfigManager] Failed to save config: {ex.Message}");
            }
        }

        static TraceConfig CloneConfig(TraceConfig config)
        {
            return new TraceConfig
            {
                DefaultLevel = config.DefaultLevel,
                FilterRecurring = config.FilterRecurring,
                Categories = config.Categories != null
                    ? new Dictionary<string, string>(config.Categories)
                    : null,
                Sessions = config.Sessions != null
                    ? new Dictionary<string, string>(config.Sessions)
                    : null,
                Components = config.Components != null
                    ? new Dictionary<string, string>(config.Components)
                    : null,
            };
        }

        static bool ConfigEquals(TraceConfig a, TraceConfig b)
        {
            return a.DefaultLevel == b.DefaultLevel
                && a.FilterRecurring == b.FilterRecurring
                && DictionaryEquals(a.Categories, b.Categories)
                && DictionaryEquals(a.Sessions, b.Sessions)
                && DictionaryEquals(a.Components, b.Components);
        }

        static bool DictionaryEquals(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            // Both null or empty = equal
            var aEmpty = a == null || a.Count == 0;
            var bEmpty = b == null || b.Count == 0;
            if (aEmpty && bEmpty) return true;
            if (aEmpty != bEmpty) return false;

            if (a.Count != b.Count) return false;

            foreach (var kvp in a)
            {
                if (!b.TryGetValue(kvp.Key, out var bValue) || kvp.Value != bValue)
                    return false;
            }

            return true;
        }
    }
}
#endif
