using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.Helpers;

namespace Becool.UnityMcpLens.Editor.Settings
{
    [Serializable]
    class McpLensProjectSettings
    {
        public bool LegacyRelayEnabled = false;
    }

    static class McpProjectPreferences
    {
        const string k_LegacyPackageName = "com.unity.ai.assistant";

        static readonly string k_SettingsPath =
            Path.Combine("ProjectSettings", "Packages", MCPConstants.packageName, "Settings.json");

        static readonly string k_LegacySettingsPath =
            Path.Combine("ProjectSettings", "Packages", k_LegacyPackageName, "Settings.json");

        static McpLensProjectSettings s_Settings;
        static JObject s_RawSettings;

        public static event Action<bool> LegacyRelayEnabledChanged;

        static McpLensProjectSettings Settings
        {
            get
            {
                if (s_Settings != null)
                    return s_Settings;

                TryMigrateLegacySettings();

                if (!File.Exists(k_SettingsPath))
                {
                    s_Settings = new McpLensProjectSettings();
                    Save();
                    return s_Settings;
                }

                try
                {
                    var json = File.ReadAllText(k_SettingsPath);
                    s_RawSettings = string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json);
                    s_Settings = new McpLensProjectSettings();
                    if (s_RawSettings.TryGetValue(nameof(McpLensProjectSettings.LegacyRelayEnabled), StringComparison.Ordinal, out var token) &&
                        token.Type == JTokenType.Boolean)
                    {
                        s_Settings.LegacyRelayEnabled = token.Value<bool>();
                    }
                    else
                    {
                        s_Settings.LegacyRelayEnabled = false;
                    }
                }
                catch (Exception ex)
                {
                    McpLog.Warning($"Failed to read MCP project preferences: {ex.Message}");
                    s_Settings = new McpLensProjectSettings();
                    s_RawSettings = new JObject();
                }

                return s_Settings;
            }
        }

        public static bool LegacyRelayEnabled
        {
            get => Settings.LegacyRelayEnabled;
            set
            {
                if (Settings.LegacyRelayEnabled == value)
                    return;

                Settings.LegacyRelayEnabled = value;
                Save();
                LegacyRelayEnabledChanged?.Invoke(value);
            }
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(k_SettingsPath) ?? "ProjectSettings");
                s_RawSettings ??= File.Exists(k_SettingsPath)
                    ? JObject.Parse(File.ReadAllText(k_SettingsPath))
                    : new JObject();
                s_RawSettings[nameof(McpLensProjectSettings.LegacyRelayEnabled)] = s_Settings.LegacyRelayEnabled;
                File.WriteAllText(k_SettingsPath, s_RawSettings.ToString(Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex)
            {
                McpLog.Warning($"Failed to save MCP project preferences: {ex.Message}");
            }
        }

        static void TryMigrateLegacySettings()
        {
            try
            {
                if (File.Exists(k_SettingsPath) || !File.Exists(k_LegacySettingsPath))
                    return;

                Directory.CreateDirectory(Path.GetDirectoryName(k_SettingsPath) ?? "ProjectSettings");
                File.Copy(k_LegacySettingsPath, k_SettingsPath, false);
                McpLog.Log("Migrated Unity MCP Lens project preferences from the previous Assistant package path.");
            }
            catch (Exception ex)
            {
                McpLog.Warning($"Failed to migrate legacy MCP project preferences: {ex.Message}");
            }
        }
    }
}
