using System;
using System.Reflection;
using UnityEditor;

namespace Unity.AI.Toolkit.Asset
{
    static class ProjectWindowUtilWrapper
    {
        static readonly MethodInfo k_GetActiveFolderPathMethod;

        static ProjectWindowUtilWrapper()
        {
            var projectWindowUtilType = typeof(Editor).Assembly.GetType("UnityEditor.ProjectWindowUtil");
            if (projectWindowUtilType == null)
            {
                throw new Exception("Unable to find type 'UnityEditor.ProjectWindowUtil'.");
            }

            k_GetActiveFolderPathMethod = projectWindowUtilType.GetMethod("GetActiveFolderPath", BindingFlags.Static | BindingFlags.NonPublic);
            if (k_GetActiveFolderPathMethod == null)
            {
                throw new Exception("Unable to find method 'GetActiveFolderPath'.");
            }
        }

        public static string GetActiveFolderPath() => (string)k_GetActiveFolderPathMethod.Invoke(null, null);
    }
}
