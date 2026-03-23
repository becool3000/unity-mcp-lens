using Unity.AI.Search.Editor.Knowledge;
using UnityEditor;
using UnityEditor.PackageManager;

namespace Unity.AI.Search.Editor.Utilities
{
    [InitializeOnLoad]
    static class SentisInstallHelper
    {
        const string k_SentisPackageName = "com.unity.ai.inference";

        static SentisInstallHelper()
        {
            AssetKnowledgeSettings.SearchEnabledChanged += AssetKnowledgeSettingsOnSearchEnabledChanged;
            EditorApplication.delayCall += CheckIfSentisNeedsToBeInstalled;
        }

        static void AssetKnowledgeSettingsOnSearchEnabledChanged(bool enabled)
        {
            CheckIfSentisNeedsToBeInstalled();
        }

        static void CheckIfSentisNeedsToBeInstalled()
        {
            if (AssetKnowledgeSettings.SearchEnabled)
            {
                // Check if already installed:
                if (UnityEditor.PackageManager.PackageInfo.IsPackageRegistered(k_SentisPackageName))
                    return;

                // Otherwise try to add it:
                if (!EditorUtility.DisplayDialog("Installing a required package",
                        "The Sentis package is required for the Asset Knowledge Add-on. Would you like to install it now?",
                        "Yes", "No"))
                {
                    // Disable it next frame to avoid modifying settings during event callbacks:
                    EditorApplication.delayCall += () => AssetKnowledgeSettings.SearchEnabled = false;
                }
                else
                {
                    Client.Add(k_SentisPackageName);
                }
            }
        }
    }
}