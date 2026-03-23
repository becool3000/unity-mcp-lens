using System;
using System.Collections.Generic;

namespace Unity.AI.Assistant.Editor.Settings
{
    record PreferencesData
    {
        public string Error { get; init; }
        public List<ProviderInfo> ProviderInfoList { get; init; }
    }

    record EnvVar
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public bool InKeychain { get; set; }
        public bool IsSet { get; set; }         // Has been manually set (if in keychain)
        public bool IsUpdated { get; set; }     // If it has been updated in this session and needs to be saved -- for keychain variables -- will be cleared by relay on save)

        public EnvVar(string name = "", string value = "", bool inKeychain = false)
        {
            Name = name;
            Value = value;
            InKeychain = inKeychain;
        }
    }

    /// <summary>
    /// Environment variables for an agent type.
    /// </summary>
    record ProviderInfo
    {
        public string ProviderType { get; init; }
        public string ProviderDisplayName { get; init; }
        public string Version { get; init; }
        public string HelpText { get; init; }
        public bool IsCustom { get; init; }
        public List<EnvVar> Variables { get; init; }
        public List<string> RequiredEnvVarNames { get; init; }
    }

}
