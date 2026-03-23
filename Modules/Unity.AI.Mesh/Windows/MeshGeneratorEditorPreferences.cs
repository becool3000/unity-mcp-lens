using System;
using System.Text;
using Unity.AI.Generators.UI.AIDropdownIntegrations;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Mesh.Windows
{
    /// <summary>
    /// Manages the editor preferences for the Mesh AI Generator, specifically for Scenario.com credentials.
    /// This class uses EditorPrefs to store user credentials in a way that is cross-platform and avoids
    /// committing them to version control systems like Git.
    /// On Windows, EditorPrefs are stored in the user's registry.
    /// On macOS, they are stored in a user-specific file (~/Library/Preferences/com.unity3d.UnityEditor5.x.plist).
    /// While automated security reviews may flag EditorPrefs as insecure due to its plaintext storage,
    /// it is the standard Unity mechanism for storing user-specific editor settings outside of the project folder,
    /// preventing accidental credential exposure in shared repositories. There is no better built-in,
    /// cross-platform, and user-friendly alternative for this purpose within Unity's ecosystem.
    /// </summary>
    [InitializeOnLoad]
    class MeshEditorPreferences : SettingsProvider
    {
        public const string PreferencesPath = "Preferences/AI/Asset Generators";
        public const string ScenarioBasicAuthKey = "Unity.AI.Mesh.Scenario.BasicAuth";

        const string k_ScenarioApiKey = "Unity.AI.Mesh.Scenario.ApiKey";
        const string k_ScenarioApiSecret = "Unity.AI.Mesh.Scenario.ApiSecret";
        const string k_ScenarioBasicAuth = ScenarioBasicAuthKey;

        static string s_ApiKey;
        static string s_ApiSecret;
        static string s_BasicAuth;
        static bool s_ShowSecrets;

        static MeshEditorPreferences()
        {
            var basicAuth = EditorPrefs.GetString(k_ScenarioBasicAuth, "");
            if (!string.IsNullOrEmpty(basicAuth))
            {
                GlTFastInstaller.InstallGlTFastIfNeeded();
            }
        }

        MeshEditorPreferences(string path, SettingsScope scope) : base(path, scope) { }

        public override void OnActivate(string searchContext, UnityEngine.UIElements.VisualElement rootElement)
        {
            s_ApiKey = EditorPrefs.GetString(k_ScenarioApiKey, "");
            s_ApiSecret = EditorPrefs.GetString(k_ScenarioApiSecret, "");
            s_BasicAuth = EditorPrefs.GetString(k_ScenarioBasicAuth, "");
        }

        public override void OnGUI(string searchContext)
        {
            s_ShowSecrets = GUILayout.RepeatButton(Styles.ShowSecrets, GUILayout.Width(100));
            EditorGUILayout.Space();

            EditorGUILayout.LabelField(Styles.Scenario, EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUI.indentLevel++;

            EditorGUI.BeginChangeCheck();

            s_ApiKey = EditorGUILayout.TextField(Styles.ApiKey, s_ApiKey);
            s_ApiSecret = s_ShowSecrets ? EditorGUILayout.TextField(Styles.ApiSecret, s_ApiSecret) : EditorGUILayout.PasswordField(Styles.ApiSecret, s_ApiSecret);

            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(k_ScenarioApiKey, s_ApiKey);
                EditorPrefs.SetString(k_ScenarioApiSecret, s_ApiSecret);
                UpdateBasicAuth();
            }

            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            s_BasicAuth = s_ShowSecrets ? EditorGUILayout.TextField(Styles.BasicAuthToken, s_BasicAuth) : EditorGUILayout.PasswordField(Styles.BasicAuthToken, s_BasicAuth);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(k_ScenarioBasicAuth, s_BasicAuth);
                UpdateApiKeyAndSecretFromBasicAuth();
            }

            EditorGUILayout.HelpBox("Provide either the API Key and Secret, or the Basic Auth Token. The other fields will be populated automatically.", MessageType.Info);

            EditorGUI.indentLevel--;
        }

        static void UpdateApiKeyAndSecretFromBasicAuth()
        {
            if (string.IsNullOrEmpty(s_BasicAuth) || !s_BasicAuth.StartsWith("Basic "))
            {
                s_ApiKey = "";
                s_ApiSecret = "";
            }
            else
            {
                try
                {
                    var base64EncodedData = s_BasicAuth["Basic ".Length..];
                    var base64EncodedBytes = Convert.FromBase64String(base64EncodedData);
                    var decodedString = Encoding.UTF8.GetString(base64EncodedBytes);
                    var parts = decodedString.Split(new[] { ':' }, 2);
                    if (parts.Length == 2)
                    {
                        s_ApiKey = parts[0];
                        s_ApiSecret = parts[1];
                    }
                    else
                    {
                        s_ApiKey = decodedString;
                        s_ApiSecret = "";
                    }
                }
                catch (FormatException)
                {
                    s_ApiKey = "";
                    s_ApiSecret = "";
                }
            }
            EditorPrefs.SetString(k_ScenarioApiKey, s_ApiKey);
            EditorPrefs.SetString(k_ScenarioApiSecret, s_ApiSecret);

            if (!string.IsNullOrEmpty(s_BasicAuth))
            {
                GlTFastInstaller.InstallGlTFastIfNeeded();
            }
        }

        static void UpdateBasicAuth()
        {
            if (string.IsNullOrEmpty(s_ApiKey) && string.IsNullOrEmpty(s_ApiSecret))
            {
                s_BasicAuth = "";
            }
            else
            {
                var plainTextBytes = Encoding.UTF8.GetBytes($"{s_ApiKey}:{s_ApiSecret}");
                s_BasicAuth = "Basic " + Convert.ToBase64String(plainTextBytes);
            }
            EditorPrefs.SetString(k_ScenarioBasicAuth, s_BasicAuth);

            if (!string.IsNullOrEmpty(s_BasicAuth))
            {
                GlTFastInstaller.InstallGlTFastIfNeeded();
            }
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            if (!MeshGeneratorInternals.MeshGeneratorOwnKeyEnabled)
                return null;

            var provider = new MeshEditorPreferences(PreferencesPath, SettingsScope.User)
            {
                keywords = GetSearchKeywordsFromGUIContentProperties<Styles>()
            };
            return provider;
        }

        class Styles
        {
            public static readonly GUIContent ApiKey = new GUIContent("API Key", "Your Scenario.com API Key.");
            public static readonly GUIContent ApiSecret = new GUIContent("API Secret", "Your Scenario.com API Secret.");
            public static readonly GUIContent Scenario = new GUIContent("Scenario.com");
            public static readonly GUIContent BasicAuthToken = new GUIContent("Basic Auth Token", "The Basic Authentication token. Can be generated from API Key and Secret, or pasted directly.");
            public static readonly GUIContent ShowSecrets = new GUIContent("Show Secrets", "Hold this button down to reveal the content of the secret fields.");
        }
    }
}
