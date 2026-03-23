using System;
using Unity.AI.Toolkit.Connect;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Toolkit.Accounts.Services.Core
{
    static class AccountLinks
    {
        public static void ManageAccount()
        {
            var organizationId = UnityConnectProvider.organizationKey;
            if (string.IsNullOrEmpty(organizationId))
                Application.OpenURL("https://cloud.unity.com/home/organizations");
            else
                Application.OpenURL($"https://cloud.unity.com/home/organizations/{organizationId}/ai/settings");
        }

        public static void ViewBundles() => Application.OpenURL("https://cloud.unity.com/home/ai");

        public static void GetPoints() => Application.OpenURL("https://create.unity.com/unity-ai-beta-points-request");

        public static void ViewDocumentation()
        {
            var versionParts = Application.unityVersion.Split('.');
            Application.OpenURL(versionParts.Length >= 2
                ? $"https://docs.unity3d.com/{versionParts[0]}.{versionParts[1]}/Documentation/Manual/ai-menu.html"
                : "https://docs.unity3d.com/6000.3/Documentation/Manual/ai-menu.html");
        }

        public static void OpenInPackageManager() =>
            UnityEditor.PackageManager.UI.Window.Open("com.unity.ai.assistant");

        public static void OpenAssistantInPackageManager() =>
            UnityEditor.PackageManager.UI.Window.Open("com.unity.ai.assistant");

        public static void OpenGeneratorsInPackageManager() =>
            UnityEditor.PackageManager.UI.Window.Open("com.unity.ai.assistant");
    }
}
