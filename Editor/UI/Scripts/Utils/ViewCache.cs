using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Utils
{
    class ViewCache
    {
        static readonly ViewCache k_DefaultCache = new();
        static readonly IDictionary<string, ViewCache> k_SpecializedCaches;

        readonly IDictionary<string, VisualTreeAsset> m_Cache;
        readonly string m_ViewPath;

        /// <summary>
        /// Static constructor for specialized caches
        /// </summary>
        static ViewCache()
        {
            k_SpecializedCaches = new Dictionary<string, ViewCache>();
        }

        /// <summary>
        /// Constructs a new view-cache to use a specific base path and sub-path
        /// </summary>
        /// <param name="basePath">the base path to use</param>
        /// <param name="subPath">the optional sub-path to use</param>
        public ViewCache(string basePath = null, string subPath = null)
        {
            m_Cache = new Dictionary<string, VisualTreeAsset>();
            m_ViewPath = CacheUtils.GetCachePath(basePath, subPath, AssistantUIConstants.ViewFolder);
        }

        /// <summary>
        /// Returns a view cache for a given base path and sub-path.
        /// If the base path is null or empty it will return the default cache.
        /// If there is no cache initialized yet for the custom base path it will create one and return it.
        /// </summary>
        /// <param name="basePath">the base path to return the cache for</param>
        /// <param name="subPath">the optional sub-path</param>
        /// <returns>The initialized cache</returns>
        public static ViewCache Get(string basePath = null, string subPath = null)
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

            cache = new ViewCache(basePath, subPath);
            k_SpecializedCaches.Add(key, cache);
            return cache;
        }

        /// <summary>
        /// Loads and caches a Visual tree asset and returns it, if the asset is already loaded it's just returned from the cache
        /// </summary>
        /// <param name="file">the path to load</param>
        /// <returns>The asset or null if it could not be loaded</returns>
        public VisualTreeAsset Load(string file)
        {
            if (m_Cache.TryGetValue(file, out var cachedStyle))
            {
                return cachedStyle;
            }

            if (!UXLoader.LoadAsset(m_ViewPath + file, ref cachedStyle))
            {
                return null;
            }

            m_Cache.Add(file, cachedStyle);
            return cachedStyle;
        }
    }
}
