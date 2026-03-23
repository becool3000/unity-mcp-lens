using System;
using System.Collections.Generic;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Utils
{
    /// <summary>
    /// Helper class to save and load data for a conversation locally that persists across domain reloads.
    /// </summary>
    static class LocalConversationStorageHelper
    {
        internal static string GetStorageKey<T>(string conversationId) =>
            $"ASSISTANT_LOCAL_STORAGE_{typeof(T).FullName}_{conversationId}";

        public static void Save<T>(string conversationId, List<T> list)
        {
            SetList(GetStorageKey<T>(conversationId), list);
        }

        public static List<T> Load<T>(string conversationId)
        {
            return GetList<T>(GetStorageKey<T>(conversationId));
        }

        static void SetList<T>(string key, List<T> list)
        {
            EditorPrefs.SetString(key, AssistantJsonHelper.Serialize(list));
        }

        static List<T> GetList<T>(string key)
        {
            var resultsAsJson = EditorPrefs.GetString(key);
            if (string.IsNullOrEmpty(resultsAsJson))
                return new List<T>();

            try
            {
                return AssistantJsonHelper.Deserialize<List<T>>(resultsAsJson);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return new List<T>();
            }
        }
    }
}
