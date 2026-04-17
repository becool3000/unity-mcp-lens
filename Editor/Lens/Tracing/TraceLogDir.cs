using System;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Becool.UnityMcpLens.Editor.Tracing
{
    /// <summary>
    /// Single source of truth for the trace log directory.
    /// When no override is set, defaults to {ProjectRoot}/Logs.
    /// The override is persisted per-project in EditorUserSettings.
    /// </summary>
    static class TraceLogDir
    {
        const string k_SettingKey = "Trace.LogDir.Override";

        static string s_Override;
        static bool s_Loaded;

        /// <summary>
        /// Fires when the log directory changes (either override set or cleared).
        /// </summary>
        public static event Action OnChanged;

        /// <summary>
        /// The default log directory: {ProjectRoot}/Logs.
        /// </summary>
        public static string DefaultLogDir =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath)!, "Logs");

        /// <summary>
        /// The active log directory (override if set, otherwise default).
        /// </summary>
        public static string LogDir
        {
            get
            {
                EnsureLoaded();
                return !string.IsNullOrEmpty(s_Override) ? s_Override : DefaultLogDir;
            }
        }

        /// <summary>
        /// True when a custom override is active.
        /// </summary>
        public static bool HasOverride
        {
            get
            {
                EnsureLoaded();
                return !string.IsNullOrEmpty(s_Override);
            }
        }

        /// <summary>
        /// Set a custom log directory override. Pass null or empty to clear.
        /// Persists the setting and fires <see cref="OnChanged"/>.
        /// </summary>
        public static void SetOverride(string path)
        {
            s_Override = string.IsNullOrEmpty(path) ? null : path;

#if UNITY_EDITOR
            EditorUserSettings.SetConfigValue(k_SettingKey, s_Override ?? "");
#endif

            OnChanged?.Invoke();
        }

        static void EnsureLoaded()
        {
            if (s_Loaded) return;
            s_Loaded = true;

#if UNITY_EDITOR
            var stored = EditorUserSettings.GetConfigValue(k_SettingKey);
            if (!string.IsNullOrEmpty(stored))
                s_Override = stored;
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// Re-read persisted value at editor startup (handles domain reload).
        /// </summary>
        [InitializeOnLoadMethod]
        static void InitializeFromSettings()
        {
            s_Loaded = false; // force re-read after domain reload
            EnsureLoaded();
        }
#endif
    }
}
