using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.SessionBanner
{
    [InitializeOnLoad]
    static class AssistantPackageAutoUpdater
    {
        const string k_PackageName = "com.unity.ai.assistant";

        static bool s_IsCheckingForUpdate;

        static AssistantPackageAutoUpdater()
        {
            if (Application.isBatchMode || !AssistantEditorPreferences.EnablePackageAutoUpdate)
                return;

            EditorApplication.update += DeferredCheckForUpdates;
            UnityEditor.PackageManager.Events.registeredPackages -= OnPackagesChanged;
            UnityEditor.PackageManager.Events.registeredPackages += OnPackagesChanged;
        }

        static void DeferredCheckForUpdates()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess() || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            if (s_IsCheckingForUpdate)
                return;

            EditorApplication.update -= DeferredCheckForUpdates;
            s_IsCheckingForUpdate = true;
            CheckForUpdate();
        }

        static void OnPackagesChanged(UnityEditor.PackageManager.PackageRegistrationEventArgs args)
        {
            // Check if the assistant package was added or updated via Package Manager
            var assistantPackageChanged = args.added.Any(p => p.name == k_PackageName)
                || args.changedTo.Any(p => p.name == k_PackageName);

            if (assistantPackageChanged)
            {
                PackageUpdateState.instance.Clear();

                if (!s_IsCheckingForUpdate)
                {
                    // Trigger the check on the next update frame to find any further available updates
                    EditorApplication.update -= DeferredCheckForUpdates;
                    EditorApplication.update += DeferredCheckForUpdates;
                }
            }
        }

        static async void CheckForUpdate()
        {
            try
            {
                var currentPackageInfo = UnityEditor.PackageManager.PackageInfo.FindForPackageName(k_PackageName);
                if (currentPackageInfo is not { source: UnityEditor.PackageManager.PackageSource.Registry })
                {
                    s_IsCheckingForUpdate = false;
                    return;
                }

                var searchRequest = UnityEditor.PackageManager.Client.Search(k_PackageName);
                while (!searchRequest.IsCompleted)
                    await Task.Yield();

                if (searchRequest.Status != UnityEditor.PackageManager.StatusCode.Success)
                {
                    s_IsCheckingForUpdate = false;
                    return;
                }

                var remoteInfo = searchRequest.Result.FirstOrDefault(p => p.name == k_PackageName);
                if (remoteInfo == null)
                {
                    s_IsCheckingForUpdate = false;
                    return;
                }

                var latestCompatible = remoteInfo.versions.latestCompatible;
                if (!string.IsNullOrEmpty(latestCompatible) && CompareSemVer(latestCompatible, currentPackageInfo.version) > 0)
                    PackageUpdateState.instance.SetUpdateAvailable(currentPackageInfo.version, latestCompatible);

                s_IsCheckingForUpdate = false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Assistant Auto-Updater] Check failed: {ex.Message}");
                s_IsCheckingForUpdate = false;
            }
        }

        public static async Task UpdatePackage(string version)
        {
            var addRequest = UnityEditor.PackageManager.Client.Add($"{k_PackageName}@{version}");

            while (!addRequest.IsCompleted)
            {
                await Task.Yield();
            }

            if (addRequest.Status == UnityEditor.PackageManager.StatusCode.Success)
            {
                Debug.Log($"[Assistant] Updated to {version}");
                PackageUpdateState.instance.Clear();
            }
            else
            {
                Debug.LogError($"[Assistant] Update failed: {addRequest.Error.message}");
            }
        }

        public static int CompareSemVer(string versionA, string versionB)
        {
            if (versionA == versionB) return 0;

            var regex = new Regex(@"^(\d+\.\d+\.\d+)(-?)(.*)$");

            var matchA = regex.Match(versionA);
            var matchB = regex.Match(versionB);

            if (!matchA.Success || !matchB.Success)
                return string.CompareOrdinal(versionA, versionB);

            var verA = new Version(matchA.Groups[1].Value);
            var verB = new Version(matchB.Groups[1].Value);

            var coreComparison = verA.CompareTo(verB);
            if (coreComparison != 0)
                return coreComparison;

            var suffixA = matchA.Groups[3].Value;
            var suffixB = matchB.Groups[3].Value;

            if (string.IsNullOrEmpty(suffixA) && !string.IsNullOrEmpty(suffixB)) return 1;
            if (!string.IsNullOrEmpty(suffixA) && string.IsNullOrEmpty(suffixB)) return -1;
            if (string.IsNullOrEmpty(suffixA) && string.IsNullOrEmpty(suffixB)) return 0;

            var segmentsA = suffixA.Split('.');
            var segmentsB = suffixB.Split('.');

            var length = Math.Min(segmentsA.Length, segmentsB.Length);
            for (var i = 0; i < length; i++)
            {
                var segA = segmentsA[i];
                var segB = segmentsB[i];

                var isNumA = int.TryParse(segA, out var numA);
                var isNumB = int.TryParse(segB, out var numB);

                if (isNumA && isNumB)
                {
                    if (numA != numB) return numA.CompareTo(numB);
                }
                else
                {
                    var strComp = string.CompareOrdinal(segA, segB);
                    if (strComp != 0) return strComp;
                }
            }

            return segmentsA.Length.CompareTo(segmentsB.Length);
        }
    }
}
