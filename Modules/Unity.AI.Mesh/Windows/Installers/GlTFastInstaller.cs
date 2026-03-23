using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Unity.AI.Mesh.Windows
{
    static class GlTFastInstaller
    {
        const string k_GlTFastPackage = "com.unity.cloud.gltfast";
        static ListRequest s_ListRequest;

        public static void InstallGlTFastIfNeeded()
        {
            if (Application.isBatchMode)
                return;

            if (s_ListRequest != null && !s_ListRequest.IsCompleted)
                return;

            s_ListRequest = Client.List();
            EditorApplication.update += ListProgress;
        }

        static void ListProgress()
        {
            if (!s_ListRequest.IsCompleted)
                return;

            EditorApplication.update -= ListProgress;

            if (s_ListRequest.Status != StatusCode.Success)
                return;

            foreach (var package in s_ListRequest.Result)
            {
                if (package.name == k_GlTFastPackage)
                {
                    return;
                }
            }

            Client.Add(k_GlTFastPackage);
        }
    }
}
