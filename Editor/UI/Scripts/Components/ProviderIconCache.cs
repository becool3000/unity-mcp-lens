using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    /// <summary>
    /// Static cache for provider icons with theme-aware loading.
    /// </summary>
    static class ProviderIconCache
    {
        static readonly Dictionary<string, string> k_ProviderIconNames = new()
        {
            { "unity", "UnitySmall" }
        };

        static readonly Dictionary<string, Texture2D> s_IconCache = new();
        static bool s_IsProSkin;

        /// <summary>
        /// Get the icon for a provider. Returns null if no icon is available.
        /// </summary>
        /// <param name="providerId">The provider ID.</param>
        /// <returns>The provider's icon texture, or null.</returns>
        public static Texture2D GetIcon(string providerId)
        {
            // Check if theme changed and invalidate cache
            if (s_IsProSkin != EditorGUIUtility.isProSkin)
            {
                s_IconCache.Clear();
                s_IsProSkin = EditorGUIUtility.isProSkin;
            }

            if (s_IconCache.TryGetValue(providerId, out var cachedIcon))
            {
                return cachedIcon;
            }

            var icon = LoadIcon(providerId);
            s_IconCache[providerId] = icon;
            return icon;
        }

        /// <summary>
        /// Invalidate the cache. Call when theme changes.
        /// </summary>
        public static void InvalidateCache()
        {
            s_IconCache.Clear();
        }

        static Texture2D LoadIcon(string providerId)
        {
            if (!k_ProviderIconNames.TryGetValue(providerId, out var iconName))
            {
                return null;
            }

            var iconBasePath = Path.Combine(
                AssistantUIConstants.BasePath,
                AssistantUIConstants.UIEditorPath,
                AssistantUIConstants.AssetFolder,
                "icons");

            var themeSuffix = EditorGUIUtility.isProSkin ? "Dark" : "Light";

            // Try themed icon first
            var themedPath = Path.Combine(iconBasePath, $"{iconName}{themeSuffix}.png");
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(themedPath);

            if (icon == null)
            {
                // Fallback to base icon
                var fallbackPath = Path.Combine(iconBasePath, $"{iconName}.png");
                icon = AssetDatabase.LoadAssetAtPath<Texture2D>(fallbackPath);
            }

            return icon;
        }
    }
}
