using System;
using System.Linq;
using Becool.UnityMcpLens.Editor.Constants;
using UnityEditor;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Settings
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

            NormalizeSettingsForCodexBridge(s_CachedSettings);
        }

        static MCPSettings CreateDefaultSettings()
        {
            return new MCPSettings();
        }

        static void NormalizeSettingsForCodexBridge(MCPSettings settings)
        {
            if (settings == null)
                return;

            settings.connectionPolicies ??= new ConnectionPolicies();
            settings.connectionPolicies.gateway ??= new ConnectionOriginPolicy();
            settings.connectionPolicies.direct ??= new ConnectionOriginPolicy();

            // The custom Codex bridge relies on direct external connections being accepted
            // without process validation or per-connection approval.
            settings.processValidationEnabled = false;
            settings.connectionPolicies.direct.allowed = true;
            settings.connectionPolicies.direct.requiresApproval = false;
            settings.connectionPolicies.gateway.allowed = true;
            settings.connectionPolicies.gateway.requiresApproval = false;
        }
    }
}
