using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Unity.AI.MCP.Editor.Helpers;

namespace Unity.AI.MCP.Editor.Settings
{
    [Serializable]
    class McpLensProjectSettings
    {
        public bool LegacyRelayEnabled = true;
    }

    static class McpProjectPreferences
    {
        static readonly string k_SettingsPath =
            Path.Combine("ProjectSettings", "Packages", MCPConstants.packageName, "Settings.json");

        static McpLensProjectSettings s_Settings;
        static JObject s_RawSettings;

        public static event Action<bool> LegacyRelayEnabledChanged;

        static McpLensProjectSettings Settings
        {
            get
            {
                if (s_Settings != null)
                    return s_Settings;

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
                        s_Settings.LegacyRelayEnabled = true;
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
    }
}
