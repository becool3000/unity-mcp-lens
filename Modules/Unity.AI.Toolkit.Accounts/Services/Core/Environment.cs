using System;
using System.Collections.Generic;
using Unity.AI.Toolkit.Connect;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Toolkit.Accounts.Services.Core
{
    static class Environment
    {
        // Environment URL constants
        public const string prodEnvironment = "https://generators.ai.unity.com";
        public const string stagingEnvironment = "https://generators-stg.ai.unity.com";
        public const string testEnvironment = "https://generators-test.ai.unity.com";
        public const string devEnvironment = "https://generators-dev.ai.unity.com";
        public const string localEnvironment = "https://localhost:5050";

        // Define delegate for environment change callback
        public delegate void EnvironmentChangedCallback(string newEnvironment);

        static readonly Dictionary<string, string> k_RegisteredEnvironmentKeys = new();
        static readonly Dictionary<string, EnvironmentChangedCallback> k_EnvironmentCallbacks = new();

        /// <summary>
        /// Register a new environment key with display label
        /// </summary>
        /// <param name="key"></param>
        /// <param name="label"></param>
        /// <param name="callback">Optional callback that will be invoked when environment changes</param>
        public static void RegisterEnvironmentKey(string key, string label, EnvironmentChangedCallback callback = null)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(label))
                return;

            k_RegisteredEnvironmentKeys[key] = label;
            if (callback != null)
                k_EnvironmentCallbacks[key] = callback;
        }

        /// <summary>
        /// Get the currently selected environment for a specific key
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public static string GetSelectedEnvironment(string key)
        {
            return Unsupported.IsDeveloperMode()
                ? EditorPrefs.GetString(key, prodEnvironment)
                : prodEnvironment;
        }

        /// <summary>
        /// Set the currently selected environment for a specific key
        /// </summary>
        /// <param name="key"></param>
        /// <param name="newEnvironment"></param>
        public static void SetSelectedEnvironment(string key, string newEnvironment)
        {
            if (string.IsNullOrEmpty(key))
                return;

            if (!string.IsNullOrWhiteSpace(newEnvironment))
            {
                EditorPrefs.SetString(key, newEnvironment);

                // Invoke callback if exists
                if (k_EnvironmentCallbacks.TryGetValue(key, out var callback))
                {
                    try { callback(newEnvironment); }
                    catch (Exception e) { Debug.LogError($"Failed to set environment for {key}: {e.Message}"); }
                }
            }
            else
            {
                EditorPrefs.DeleteKey(key);

                // Invoke callback with default environment
                if (k_EnvironmentCallbacks.TryGetValue(key, out var callback))
                {
                    try { callback(prodEnvironment); }
                    catch (Exception e) { Debug.LogError($"Failed to reset environment for {key}: {e.Message}"); }
                }
            }
        }

        class EnvironmentInputWindow : EditorWindow
        {
            const string k_InternalMenu = "internal:";

            [MenuItem(k_InternalMenu + "AI Toolkit/Internals/Log Cloud Project Info")]
            static void ShowProjectInfo()
            {
                var traceID = "None";
                try { traceID = Selection.activeObject ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(Selection.activeObject)) : traceID; }
                catch { /* Ignored */ }

                var logMessage = $"User ID: {UnityConnectProvider.userId}\n" +
                    $"User Name: {UnityConnectProvider.userName}\n" +
                    $"Organization Key: {UnityConnectProvider.organizationKey}\n" +
                    $"Organization ID: {CloudProjectSettings.organizationId}\n" +
                    $"Organization Name: {CloudProjectSettings.organizationName}\n" +
                    $"Cloud Project ID: {UnityConnectProvider.projectId}\n" +
                    $"Cloud Project Name: {CloudProjectSettings.projectName}\n";

                // Add all registered environment keys
                foreach (var keyValuePair in k_RegisteredEnvironmentKeys)
                {
                    var key = keyValuePair.Key;
                    logMessage += $"{key}: {EditorPrefs.GetString(key, prodEnvironment)}\n";
                }

                logMessage += $"(selected) Asset ID (trace ID): {traceID}&{EditorAnalyticsSessionInfo.id}";
                Debug.Log(logMessage);
            }

            [MenuItem(k_InternalMenu + "AI Toolkit/Internals/Set Environments", false, 99)]
            public static void ShowEnvironmentWindow() => ShowWindow("AI Toolkit Environment");

            readonly Dictionary<string, bool> m_EnvironmentStates = new();
            string m_InputText = prodEnvironment;

            static void ShowWindow(string title)
            {
                var window = GetWindow<EnvironmentInputWindow>(true, title, true);
                window.minSize = new Vector2(499, 200);
                window.maxSize = new Vector2(500, 300);
                window.InitializeEnvironmentStates();
                window.Show();
            }

            void InitializeEnvironmentStates()
            {
                m_EnvironmentStates.Clear();
                foreach (var key in k_RegisteredEnvironmentKeys.Keys)
                    m_EnvironmentStates[key] = true;
            }

            void OnGUI()
            {
                EditorGUILayout.Space();

                m_InputText = EditorGUILayout.TextField("Environment URL:", m_InputText);
                EditorGUILayout.Space();

                EditorGUILayout.LabelField("Set Environment Per Tool:", EditorStyles.boldLabel);

                foreach (var (key, label) in k_RegisteredEnvironmentKeys)
                {
                    var currentValue = EditorPrefs.GetString(key, prodEnvironment);

                    EditorGUILayout.BeginHorizontal();
                    m_EnvironmentStates[key] = EditorGUILayout.Toggle(label, m_EnvironmentStates[key]);
                    EditorGUILayout.LabelField($"({currentValue})", GUILayout.Width(300));
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.Space();
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("OK"))
                {
                    foreach (var key in k_RegisteredEnvironmentKeys.Keys)
                    {
                        if (!m_EnvironmentStates[key])
                            continue;

                        SetSelectedEnvironment(key, m_InputText);
                    }
                    Close();
                }
                if (GUILayout.Button("Reset All"))
                {
                    foreach (var key in k_RegisteredEnvironmentKeys.Keys)
                    {
                        SetSelectedEnvironment(key, null);
                    }
                    Close();
                }
                EditorGUILayout.EndHorizontal();
            }
        }
    }
}
