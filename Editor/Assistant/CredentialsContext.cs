using System;
using System.Collections.Generic;
using Unity.AI.Assistant.Data;
using UnityEditor;
using Unity.Ai.Assistant.Protocol.Client;
using Unity.AI.Assistant.Utils;
using UnityEngine;

namespace Unity.AI.Assistant.Editor
{
    /// <summary>
    /// Used to pass around credentials so they can be accessed from background threads.
    /// </summary>
    class CredentialsContext : ICredentialsContext
    {
        public string AccessToken { get; private set; }
        public string OrganizationId { get; private set; }
        public string ProjectId { get; private set; }
        public string AnalyticsSessionId { get; private set; }
        public int AnalyticsSessionCount { get; private set; }
        public string AnalyticsUserId { get; private set; }
        public string EditorVersion { get; private set; }
        public string PackageVersion { get; private set; }
        public string ApiVersion { get; private set; }

        public static CredentialsContext Default()
        {
            return new CredentialsContext(CloudProjectSettings.accessToken, CloudProjectSettings.organizationKey);
        }

        public CredentialsContext(string accessToken, string organizationId)
        {
            AccessToken = accessToken;
            OrganizationId = organizationId;

            ProjectId = PlayerSettings.productGUID.ToString();
            AnalyticsSessionId = EnsureAnalyticsString(EditorAnalyticsSessionInfo.id.ToString());
            AnalyticsSessionCount = (int)EditorAnalyticsSessionInfo.sessionCount;
            AnalyticsUserId = EnsureAnalyticsString(EditorAnalyticsSessionInfo.userId);
            EditorVersion = Application.unityVersion;
            PackageVersion = GetPackageVersion();
            ApiVersion = Configuration.Version;
        }

        public Dictionary<string, string> Headers => new()
        {
            { "Authorization", $"Bearer {AccessToken}" },
            { "org-id", OrganizationId },
            { "project-id", ProjectId },
            { "analytics-session-id", AnalyticsSessionId },
            { "analytics-session-count", AnalyticsSessionCount.ToString() },
            { "analytics-user-id", AnalyticsUserId },
            { "version-editor", EditorVersion },
            { "version-package", PackageVersion },
            { "version-api-specification", ApiVersion }
        };

        static string EnsureAnalyticsString(string candidate)
        {
            if (string.IsNullOrEmpty(candidate))
                candidate = "could-not-load-analytics";

            return candidate;
        }

        [Serializable]
        class PackageJson
        {
            public string version;
        }

        static string GetPackageVersion()
        {
            try
            {
                string path = $"Packages/{AssistantConstants.PackageName}/package.json";
                TextAsset packageJson = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (packageJson == null)
                {
                    Debug.LogError($"Failed to load package.json at path: {path}");
                    return "could-not-load-package-info";
                }

                PackageJson packageData = JsonUtility.FromJson<PackageJson>(packageJson.text);
                return packageData.version;
            }
            catch (Exception e)
            {
                InternalLog.LogException(e);
                return "could-not-load-package-info";
            }
        }
    }
}
