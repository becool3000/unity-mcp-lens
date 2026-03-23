using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Generators.Asset
{
    static class BrushAssetWrapper
    {
        static readonly Type k_BrushType;
        static readonly MethodInfo k_SetDirtyMethod;
        static readonly FieldInfo k_MaskField;

        static BrushAssetWrapper()
        {
            try
            {
                k_BrushType = Type.GetType("UnityEditor.Brush, UnityEditor.TerrainModule");
                if (k_BrushType != null)
                {
                    k_SetDirtyMethod = k_BrushType.GetMethod("SetDirty", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(bool) }, null);
                    k_MaskField = k_BrushType.GetField("m_Mask", BindingFlags.Public | BindingFlags.Instance);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to initialize BrushAssetWrapper: {e}");
            }
        }

        public static bool TrySetBrushDirty(UnityEngine.Object brushAsset, bool isDirty)
        {
            if (brushAsset == null)
                return false;

            if (k_BrushType == null || k_SetDirtyMethod == null)
                return false;

            if (!k_BrushType.IsInstanceOfType(brushAsset))
                return false;

            try
            {
                k_SetDirtyMethod.Invoke(brushAsset, new object[] { isDirty });
                return true;
            }
            catch { /* ignored */ }
            return false;
        }

        public static bool IsBrushAsset(UnityEngine.Object asset) => asset != null && k_BrushType != null && k_BrushType.IsInstanceOfType(asset);

        public static bool IsTerrainAsset(UnityEngine.Object asset) => asset != null && asset is GameObject obj && obj.TryGetComponent<Terrain>(out _);

        public static void RefreshTerrainBrushes(UnityEngine.Object asset)
        {
            if (asset is not Texture2D texture)
                return;

            var brushes = FindLegacyBrushesUsingTexture(texture);
            foreach (var brush in brushes)
                TrySetBrushDirty(brush, true);
        }

        public static List<UnityEngine.Object> FindLegacyBrushesUsingTexture(Texture2D targetTexture)
        {
            var brushesUsingTexture = new List<UnityEngine.Object>();
            if (targetTexture == null)
                return brushesUsingTexture;

            if (k_BrushType == null || k_MaskField == null)
            {
                Debug.LogWarning("BrushAssetWrapper.FindLegacyBrushesUsingTexture: UnityEditor.Brush type or its m_Mask field not resolved. Cannot proceed.");
                return brushesUsingTexture;
            }

            var guids = AssetDatabase.FindAssets("t:Brush");
            if (guids.Length == 0)
                return brushesUsingTexture;

            foreach (var guid in guids)
            {
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null)
                    continue;

                if (asset.GetType() != k_BrushType)
                    continue;

                try
                {
                    if (k_MaskField.GetValue(asset) as Texture2D == targetTexture)
                        brushesUsingTexture.Add(asset);
                }
                catch { /* ignored */ }
            }

            return brushesUsingTexture;
        }
    }
}
