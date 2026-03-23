using System;
using System.Linq;
using Unity.AI.MCP.Editor.Constants;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.MCP.Editor.Settings
{
    static class MCPSettingsManager
    {
        static MCPSettings s_CachedSettings;
        static bool s_IsDirty;

        public static MCPSettings Settings
        {
            get
            {
                if (s_CachedSettings == null)
                {
                    LoadSettings();
                }

                return s_CachedSettings;
            }
        }

        public static event Action OnSettingsChanged;

        public static void SaveSettings()
        {
            if (s_CachedSettings == null) return;

            string json = JsonUtility.ToJson(s_CachedSettings, true);
            EditorPrefs.SetString(MCPConstants.prefProjectSettings, json);

            s_IsDirty = false;

            OnSettingsChanged?.Invoke();
        }

        public static void MarkDirty()
        {
            s_IsDirty = true;
        }

        public static bool HasUnsavedChanges => s_IsDirty;

        static void LoadSettings()
        {
            string json = EditorPrefs.GetString(MCPConstants.prefProjectSettings, "");

            if (string.IsNullOrEmpty(json))
            {
                s_CachedSettings = CreateDefaultSettings();
                SaveSettings();
            }
            else
            {
                try
                {
                    s_CachedSettings = JsonUtility.FromJson<MCPSettings>(json);
                    if (s_CachedSettings == null)
                    {
                        s_CachedSettings = CreateDefaultSettings();
                    }
                }
                catch
                {
                    s_CachedSettings = CreateDefaultSettings();
                }
            }
        }

        static MCPSettings CreateDefaultSettings()
        {
            return new MCPSettings();
        }
    }
}