using System;
using System.IO;
using System.Linq;
using Becool.UnityMcpLens.Editor.Constants;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Becool.UnityMcpLens.Editor.Settings
{
    static class MCPSettingsManager
    {
        const string k_SettingsObjectName = "McpSettings";
        static MCPSettings s_CachedSettings;
        static JObject s_RawSettings;
        static bool s_IsDirty;
        static DateTime s_LastLoadedWriteUtc;

        public static MCPSettings Settings
        {
            get
            {
                if (s_CachedSettings == null || HasSettingsFileChangedOnDisk())
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

            s_RawSettings = ReadRawSettingsFile();
            s_RawSettings[k_SettingsObjectName] = JObject.Parse(JsonUtility.ToJson(s_CachedSettings, true));
            WriteRawSettingsFile(s_RawSettings);

            s_IsDirty = false;

            OnSettingsChanged?.Invoke();
        }

        public static void MarkDirty()
        {
            s_IsDirty = true;
            SaveSettings();
        }

        public static bool HasUnsavedChanges => s_IsDirty;

        static void LoadSettings()
        {
            // Ensure legacy-relay project preferences migrate into the shared settings file
            // before this manager merges the broader MCP settings object.
            _ = McpProjectPreferences.LegacyRelayEnabled;

            s_RawSettings = ReadRawSettingsFile();
            JToken settingsToken = s_RawSettings[k_SettingsObjectName];

            if (settingsToken == null || settingsToken.Type == JTokenType.Null)
            {
                settingsToken = TryReadLegacyEditorPrefsSettings();
                if (settingsToken != null)
                {
                    s_RawSettings[k_SettingsObjectName] = settingsToken;
                    WriteRawSettingsFile(s_RawSettings);
                }
            }

            if (settingsToken == null || settingsToken.Type == JTokenType.Null)
            {
                s_CachedSettings = CreateDefaultSettings();
                s_RawSettings[k_SettingsObjectName] = JObject.Parse(JsonUtility.ToJson(s_CachedSettings, true));
                WriteRawSettingsFile(s_RawSettings);
            }
            else
            {
                try
                {
                    s_CachedSettings = JsonUtility.FromJson<MCPSettings>(settingsToken.ToString(Formatting.None));
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
            s_LastLoadedWriteUtc = GetSettingsFileWriteUtc();
        }

        static JObject TryReadLegacyEditorPrefsSettings()
        {
            string json = EditorPrefs.GetString(MCPConstants.prefProjectSettings, "");
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var legacySettings = JsonUtility.FromJson<MCPSettings>(json) ?? CreateDefaultSettings();
                NormalizeSettingsForCodexBridge(legacySettings);
                return JObject.Parse(JsonUtility.ToJson(legacySettings, true));
            }
            catch
            {
                return null;
            }
        }

        static JObject ReadRawSettingsFile()
        {
            try
            {
                string settingsPath = MCPConstants.ProjectSettingsJsonPath;
                if (!File.Exists(settingsPath))
                    return new JObject();

                string json = File.ReadAllText(settingsPath);
                return string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json);
            }
            catch
            {
                return new JObject();
            }
        }

        static void WriteRawSettingsFile(JObject rawSettings)
        {
            try
            {
                string settingsPath = MCPConstants.ProjectSettingsJsonPath;
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath) ?? "ProjectSettings");
                string tempPath = settingsPath + ".tmp";
                File.WriteAllText(tempPath, rawSettings.ToString(Formatting.Indented));
                if (File.Exists(settingsPath))
                    File.Replace(tempPath, settingsPath, null);
                else
                    File.Move(tempPath, settingsPath);
                s_LastLoadedWriteUtc = GetSettingsFileWriteUtc();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to save Unity MCP Lens settings: {ex.Message}");
            }
        }

        static bool HasSettingsFileChangedOnDisk()
        {
            if (s_IsDirty)
                return false;

            DateTime writeUtc = GetSettingsFileWriteUtc();
            return writeUtc != s_LastLoadedWriteUtc;
        }

        static DateTime GetSettingsFileWriteUtc()
        {
            try
            {
                return File.Exists(MCPConstants.ProjectSettingsJsonPath)
                    ? File.GetLastWriteTimeUtc(MCPConstants.ProjectSettingsJsonPath)
                    : DateTime.MinValue;
            }
            catch
            {
                return DateTime.MinValue;
            }
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
