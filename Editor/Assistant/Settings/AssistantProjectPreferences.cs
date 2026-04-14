using System;
using System.IO;
using Unity.AI.Assistant.Editor.Checkpoint.Git;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor
{
    [Serializable]
    class AssistantProjectSettings
    {
        public string CustomInstructionsGUID;
        public bool CheckpointEnabled;
        public int GitInstanceTypeValue;
        public string CustomGitPath;
        public int CheckpointRetentionWeeks = 2;
        public bool LegacyRelayEnabled = true;
    }

    static class AssistantProjectPreferences
    {
        static readonly string k_SettingsPath =
            Path.Combine("ProjectSettings", "Packages", AssistantConstants.PackageName, "Settings.json");

        internal static Action CustomInstructionsFilePathChanged;
        internal static Action CheckpointEnabledChanged;
        internal static Action GitInstanceTypeChanged;
        public static event Action<bool> LegacyRelayEnabledChanged;

        static AssistantProjectSettings s_Settings;

        internal static AssistantProjectSettings Settings
        {
            get
            {
                if (s_Settings == null)
                {
                    if (!File.Exists(k_SettingsPath))
                    {
                        s_Settings = new AssistantProjectSettings();
                        Save();
                    }
                    else
                    {
                        try
                        {
                            var json = File.ReadAllText(k_SettingsPath);
                            s_Settings = JsonUtility.FromJson<AssistantProjectSettings>(json);
                            if (!json.Contains(nameof(AssistantProjectSettings.LegacyRelayEnabled), StringComparison.Ordinal))
                            {
                                s_Settings.LegacyRelayEnabled = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            InternalLog.LogException(ex);
                            s_Settings = new AssistantProjectSettings();
                        }
                    }
                }

                return s_Settings;
            }
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(k_SettingsPath));

                var json = JsonUtility.ToJson(s_Settings, true);
                File.WriteAllText(k_SettingsPath, json);
            }
            catch (Exception ex)
            {
                InternalLog.LogException(ex);
            }
        }

        public static string CustomInstructionsFilePath
        {
            get
            {
                var guidString = Settings.CustomInstructionsGUID;
                if (GUID.TryParse(guidString, out var guid))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    return path;
                }

                return null;
            }
            set
            {
                var guid = AssetDatabase.AssetPathToGUID(value);

                Settings.CustomInstructionsGUID = guid;
                Save();

                CustomInstructionsFilePathChanged?.Invoke();
            }
        }

        public static bool CheckpointEnabled
        {
            get => Settings.CheckpointEnabled;
            set
            {
                if (Settings.CheckpointEnabled == value)
                    return;

                Settings.CheckpointEnabled = value;
                Save();

                CheckpointEnabledChanged?.Invoke();
            }
        }

        public static GitInstanceType GitInstanceType
        {
            get => (GitInstanceType)Settings.GitInstanceTypeValue;
            set
            {
                var intValue = (int)value;
                if (Settings.GitInstanceTypeValue == intValue)
                    return;

                Settings.GitInstanceTypeValue = intValue;
                Save();

                GitInstanceTypeChanged?.Invoke();
            }
        }

        public static string CustomGitPath
        {
            get => Settings.CustomGitPath ?? string.Empty;
            set
            {
                if (Settings.CustomGitPath == value)
                    return;

                Settings.CustomGitPath = value;
                Save();

                GitInstanceTypeChanged?.Invoke();
            }
        }

        public static int CheckpointRetentionWeeks
        {
            get => Settings.CheckpointRetentionWeeks > 0 ? Settings.CheckpointRetentionWeeks : 2;
            set
            {
                var clampedValue = Math.Clamp(value, 1, 2);
                if (Settings.CheckpointRetentionWeeks == clampedValue)
                    return;

                Settings.CheckpointRetentionWeeks = clampedValue;
                Save();
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
    }

    public static class AssistantRelayProjectPreferences
    {
        public static event Action<bool> LegacyRelayEnabledChanged
        {
            add => AssistantProjectPreferences.LegacyRelayEnabledChanged += value;
            remove => AssistantProjectPreferences.LegacyRelayEnabledChanged -= value;
        }

        public static bool LegacyRelayEnabled
        {
            get => AssistantProjectPreferences.LegacyRelayEnabled;
            set => AssistantProjectPreferences.LegacyRelayEnabled = value;
        }
    }
}
