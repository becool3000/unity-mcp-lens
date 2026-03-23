using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Generators.IO.Utilities
{
    static class EditorUtilityWrapper
    {
        static readonly MethodInfo k_InstantiateForAnimatorPreviewMethod;
        static readonly MethodInfo k_InitInstantiatedPreviewRecursiveMethod;

        static EditorUtilityWrapper()
        {
            var editorUtilityType = typeof(EditorUtility);

            k_InstantiateForAnimatorPreviewMethod = editorUtilityType.GetMethod(
                "InstantiateForAnimatorPreview",
                BindingFlags.Static | BindingFlags.NonPublic
            );

            if (k_InstantiateForAnimatorPreviewMethod == null)
            {
                Debug.LogError("Could not find internal method: InstantiateForAnimatorPreview via reflection.");
            }

            k_InitInstantiatedPreviewRecursiveMethod = editorUtilityType.GetMethod(
                "InitInstantiatedPreviewRecursive",
                BindingFlags.Static | BindingFlags.NonPublic
            );

            if (k_InitInstantiatedPreviewRecursiveMethod == null)
            {
                Debug.LogError("Could not find internal method: InitInstantiatedPreviewRecursive via reflection.");
            }
        }

        public static void InitInstantiatedPreviewRecursive(GameObject go)
        {
            if (k_InitInstantiatedPreviewRecursiveMethod == null)
            {
                Debug.LogError("InitInstantiatedPreviewRecursive method not found.");
                return;
            }

            k_InitInstantiatedPreviewRecursiveMethod.Invoke(null, new object[] { go });
        }

        public static GameObject InstantiateForAnimatorPreview(UnityEngine.Object original)
        {
            if (k_InstantiateForAnimatorPreviewMethod == null)
            {
                Debug.LogError("InstantiateForAnimatorPreview method not found.");
                return null;
            }

            var result = k_InstantiateForAnimatorPreviewMethod.Invoke(null, new object[] { original });
            return result as GameObject;
        }
    }
}
