using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.SessionBanner
{
    [InitializeOnLoad]
    static class AssistantPackageAutoUpdater
    {
        const string k_PackageName = "com.unity.ai.assistant";

        static bool s_HasCheckedThisSession;

        static AssistantPackageAutoUpdater()
        {
            if (Application.isBatchMode || !AssistantEditorPreferences.EnablePackageAutoUpdate)
                return;

            EditorApplication.update += DeferredCheckForUpdates;
        }

        static void DeferredCheckForUpdates()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess() || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            if (s_HasCheckedThisSession)
            {
                EditorApplication.update -= DeferredCheckForUpdates;
                return;
            }

            EditorApplication.update -= DeferredCheckForUpdates;
            s_HasCheckedThisSession = true;
            CheckForUpdate();
        }

        static async void CheckForUpdate()
        {
            try
            {
                var currentPackageInfo = UnityEditor.PackageManager.PackageInfo.FindForPackageName(k_PackageName);
                if (currentPackageInfo is not { source: PackageSource.Registry })
                    return;

                var searchRequest = Client.Search(k_PackageName);
                while (!searchRequest.IsCompleted)
                    await Task.Yield();

                if (searchRequest.Status != StatusCode.Success)
                    return;

                var remoteInfo = searchRequest.Result.FirstOrDefault(p => p.name == k_PackageName);
                if (remoteInfo == null)
                    return;

                var latestCompatible = remoteInfo.versions.latestCompatible;
                if (!string.IsNullOrEmpty(latestCompatible) && CompareSemVer(latestCompatible, currentPackageInfo.version) > 0)
                    PackageUpdateState.instance.SetUpdateAvailable(currentPackageInfo.version, latestCompatible);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Assistant Auto-Updater] Check failed: {ex.Message}");
            }
        }

        public static async Task UpdatePackage(string version)
        {
            var addRequest = Client.Add($"{k_PackageName}@{version}");

            while (!addRequest.IsCompleted)
            {
                await Task.Yield();
            }

            if (addRequest.Status == StatusCode.Success)
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
