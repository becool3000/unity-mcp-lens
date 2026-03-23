using System;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Utils
{
    static class UXLoader
    {
        /// <summary>
        /// Load a custom asset from the editor if the asset is not already loaded as provided by the target.
        /// </summary>
        /// <param name="file">The asset to load</param>
        /// <param name="target">The target reference to load the asset into, if it is already set this method will do nothing</param>
        /// <param name="silentFailure">if true there will be no logs or throws in case of failure to load the asset</param>
        /// <typeparam name="T">The type of asset to load</typeparam>
        /// <returns>True if the target is already set or if the asset was loaded successfully</returns>
        public static bool LoadAsset<T>(string file, ref T target, bool silentFailure = false)
            where T : UnityEngine.Object
        {
            if (target != null)
            {
                // Asset is already loaded
                return true;
            }

            target = LoadAssetInternal<T>(file);

            if (target == null)
            {
                if (!silentFailure)
                {
                    Debug.LogErrorFormat("Failed to Load Asset: {0}", file);
                }

                return false;
            }

            return true;
        }

        static T LoadAssetInternal<T>(string file)
            where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            // First we try to load the asset directly via path
            T result = AssetDatabase.LoadAssetAtPath<T>(file);
            if (result != null)
            {
                return result;
            }

            // If that fails we fall through to try to load as resource
#endif

            var resourceIndex = file.ToLowerInvariant().IndexOf(AssistantUIConstants.ResourceFolderName.ToLowerInvariant() + "/", StringComparison.Ordinal);
            if (resourceIndex >= 0)
            {
                int copyIndex = resourceIndex + AssistantUIConstants.ResourceFolderName.Length + 1;
                file = file.Substring(copyIndex, file.Length - copyIndex);
            }

            var extensionIndex = file.LastIndexOf(".", StringComparison.OrdinalIgnoreCase);
            if (extensionIndex > 0)
            {
                file = file.Substring(0, extensionIndex);
            }

            return Resources.Load<T>(file);
        }
    }
}
