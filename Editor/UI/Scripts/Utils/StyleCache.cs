using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Utils
{
    /// <summary>
    /// Cache for style files loaded dynamically
    /// </summary>
    class StyleCache
    {
        static readonly StyleCache k_DefaultCache = new();
        static readonly IDictionary<string, StyleCache> k_SpecializedCaches;

        readonly IDictionary<string, StyleSheet> m_Cache;
        readonly string m_StylePath;

        /// <summary>
        /// Static constructor for specialized caches
        /// </summary>
        static StyleCache()
        {
            k_SpecializedCaches = new Dictionary<string, StyleCache>();
        }

        /// <summary>
        /// Constructs a new style-cache to use a specific base path and sub-path
        /// </summary>
        /// <param name="basePath">the base path to use</param>
        /// <param name="subPath">the optional sub-path to use</param>
        public StyleCache(string basePath = null, string subPath = null)
        {
            m_Cache = new Dictionary<string, StyleSheet>();

            m_StylePath = CacheUtils.GetCachePath(basePath, subPath, AssistantUIConstants.StyleFolder);
        }

        /// <summary>
        /// Returns a style cache for a given base path and sub-path.
        /// If the base path is null or empty it will return the default cache.
        /// If there is no cache initialized yet for the custom base path it will create one and return it.
        /// </summary>
        /// <param name="basePath">the base path to return the cache for</param>
        /// <param name="subPath">the optional sub-path</param>
        /// <returns>The initialized cache</returns>
        public static StyleCache Get(string basePath = null, string subPath = null)
        {
            if (string.IsNullOrEmpty(basePath))
            {
                return k_DefaultCache;
            }

            string key = CacheUtils.GetCacheKey(basePath, subPath);
            if (k_SpecializedCaches.TryGetValue(key, out var cache))
            {
                return cache;
            }

            cache = new StyleCache(basePath, subPath);
            k_SpecializedCaches.Add(key, cache);
            return cache;
        }

        /// <summary>
        /// Loads and caches a stylesheet file and returns it, if the style is already loaded it's just returned from the cache
        /// </summary>
        /// <param name="file">the path to load</param>
        /// <param name="isAbsolutePath">if true the path is assumed to be absolute and will not be prefixes with the base and sub paths</param>
        /// <returns>The stylesheet or null if the style could not be loaded</returns>
        public StyleSheet Load(string file, bool isAbsolutePath = false)
        {
            if (!isAbsolutePath)
            {
                file = m_StylePath + file;
            }

            if (m_Cache.TryGetValue(file, out var cachedStyle))
            {
                return cachedStyle;
            }

            if (!UXLoader.LoadAsset(file, ref cachedStyle))
            {
                return null;
            }

            m_Cache.Add(file, cachedStyle);
            return cachedStyle;
        }
    }
}
