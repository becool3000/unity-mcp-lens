using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Animate.Services.Utilities
{
    [FilePath("UserSettings/AI.Animate/AnimationClipDatabase.asset", FilePathAttribute.Location.ProjectFolder)]
    class AnimationClipDatabase : ScriptableSingleton<AnimationClipDatabase>
    {
        [SerializeField]
        internal List<AnimationClipDatabaseItem> cachedClips = new();

        readonly Dictionary<string, AnimationClipDatabaseItem> m_ClipMap = new();

        const int k_MaxInMemoryCacheSize = 200; // Max items during editor session
        const int k_PersistentCacheSize = 50;   // Max items to save to disk

        bool m_Dirty;

        void OnEnable()
        {
            m_ClipMap.Clear();
            foreach (var entry in cachedClips)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.uri))
                    m_ClipMap[entry.uri] = entry;
            }
            EditorApplication.quitting += OnEditorQuitting;
        }

        /// <summary>
        /// Save the database when the editor quits or the singleton is unloaded.
        /// </summary>
        void OnDisable()
        {
            Save();
            EditorApplication.quitting -= OnEditorQuitting;
        }

        /// <summary>
        /// Force a final save.
        /// </summary>
        void OnEditorQuitting() => Save();

        void Save()
        {
            var needsTrimmingForPersistence = cachedClips.Count > k_PersistentCacheSize;
            if (!m_Dirty && !needsTrimmingForPersistence)
                return;

            if (needsTrimmingForPersistence)
                m_Dirty = true;

            if (!m_Dirty)
                return;

            try
            {
                if (EditorUtility.DisplayCancelableProgressBar("AI.Animate", "Preparing AI.Animate database...", 0.2f))
                {
                    EditorUtility.ClearProgressBar();
                    return;
                }

                if (needsTrimmingForPersistence)
                {
                    cachedClips.Sort((a, b) => a.lastUsedTimestamp.CompareTo(b.lastUsedTimestamp));
                    var numToRemove = cachedClips.Count - k_PersistentCacheSize;
                    for (var i = 0; i < numToRemove; i++)
                    {
                        if (cachedClips.Count == 0)
                            break;
                        var itemToRemove = cachedClips[0];
                        cachedClips.RemoveAt(0);
                        m_ClipMap.Remove(itemToRemove.uri);
                    }
                }

                if (EditorUtility.DisplayCancelableProgressBar("AI.Animate", "Saving AI.Animate database...", 0.7f))
                {
                    EditorUtility.ClearProgressBar();
                    return;
                }

                base.Save(true);

                m_Dirty = false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// Adds the AnimationClip to the cache using the provided URI.
        /// </summary>
        public bool AddClip(string uri, AnimationClip clip)
        {
            if (!clip)
                return false;

            var now = EditorApplication.timeSinceStartup;

            if (m_ClipMap.TryGetValue(uri, out var existing))
            {
                if (Math.Abs(existing.lastUsedTimestamp - now) > double.Epsilon)
                {
                    existing.lastUsedTimestamp = now;
                    m_Dirty = true;
                }
                return true;
            }

            m_Dirty = true;

            if (cachedClips.Count >= k_MaxInMemoryCacheSize)
                EvictOldClips();

            var serializedData = AnimationClipDatabaseUtils.SerializeAnimationClip(clip);
            if (serializedData.data == null || serializedData.data.Length == 0)
            {
                Debug.LogError($"Failed to serialize AnimationClip for URI '{uri}'. Serialized data is null or empty.");
                return false;
            }

            var cachedItem = new AnimationClipDatabaseItem
            {
                uri = uri,
                fileName = serializedData.fileName,
                clipData = serializedData.data,
                lastUsedTimestamp = now
            };

            cachedClips.Add(cachedItem);
            m_ClipMap.Add(uri, cachedItem);

            // Instead of saving now, wait until editor close or OnDisable.
            return true;
        }

        public bool AddClip(Uri uri, AnimationClip clip) => AddClip(uri.ToString(), clip);

        /// <summary>
        /// Returns the AnimationClip for the given URI, or null if it doesn’t exist.
        /// </summary>
        public AnimationClip GetClip(string uri)
        {
            if (!m_ClipMap.TryGetValue(uri, out var cachedItem))
                return null;

            cachedItem.lastUsedTimestamp = EditorApplication.timeSinceStartup;
            m_Dirty = true;

            // Instead of immediate save, rely on the OnDisable (or quitting) save.
            return AnimationClipDatabaseUtils.DeserializeAnimationClip(cachedItem.fileName, cachedItem.clipData);
        }

        /// <summary>
        /// Returns the AnimationClip for the given URI, or null if it doesn’t exist.
        /// </summary>
        public AnimationClip GetClip(Uri uri) => GetClip(uri.ToString());

        /// <summary>
        /// Simply verifies if a clip exists for this URI.
        /// </summary>
        public bool Peek(string uri) => m_ClipMap.ContainsKey(uri);

        public bool Peek(Uri uri) => Peek(uri.ToString());

        /// <summary>
        /// Evicts the least recently used items from the in-memory cache
        /// until it's strictly below the k_MaxInMemoryCacheSize limit,
        /// effectively making space for one new item if the cache was full.
        /// </summary>
        void EvictOldClips()
        {
            cachedClips.Sort((a, b) => a.lastUsedTimestamp.CompareTo(b.lastUsedTimestamp));

            while (cachedClips.Count >= k_MaxInMemoryCacheSize)
            {
                if (cachedClips.Count == 0)
                    break;

                var toRemove = cachedClips[0];
                cachedClips.RemoveAt(0);
                m_ClipMap.Remove(toRemove.uri);
            }
        }
    }
}
