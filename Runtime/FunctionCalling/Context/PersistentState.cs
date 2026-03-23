using System;
using System.IO;
using Newtonsoft.Json;
using Unity.AI.Assistant.Utils;
using UnityEngine;

namespace Unity.AI.Assistant.FunctionCalling
{
    sealed class PersistentStorage
    {
        const string k_StoragePath = "Library/AI.Conversations";

        readonly string m_ConversationId;
        SerializableDictionary<string, string> m_Cache;

        public PersistentStorage(string conversationId)
        {
            m_ConversationId = conversationId;
        }

        public bool TryGetState<T>(string key, out T state)
        {
            EnsureLoaded();

            try
            {
                var stateKey = GetStateKey<T>(key);
                if (m_Cache.TryGetValue(stateKey, out var json))
                {
                    state = JsonConvert.DeserializeObject<T>(json);
                    return true;
                }
            }
            catch (Exception e)
            {
                InternalLog.LogException(e);
            }

            state = default;
            return false;
        }

        public void SetState<T>(string key, T state)
        {
            EnsureLoaded();

            try
            {
                var stateKey = GetStateKey<T>(key);
                m_Cache[stateKey] = JsonConvert.SerializeObject(state);
            }
            catch (Exception e)
            {
                InternalLog.LogException(e);
            }

            Save();
        }

        public void ClearState<T>(string key)
        {
            EnsureLoaded();

            var stateKey = GetStateKey<T>(key);
            if (m_Cache.Remove(stateKey))
                Save();
        }

        public void Clear()
        {
            m_Cache?.Clear();
            Delete(m_ConversationId);
        }

        public static void Delete(string conversationId)
        {
            try
            {
                var path = GetFilePath(conversationId);
                if (path == null)
                    return;

                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception e)
            {
                InternalLog.LogException(e);
            }
        }

        void EnsureLoaded()
        {
            if (m_Cache != null)
                return;

            try
            {
                var path = GetFilePath(m_ConversationId);
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    m_Cache = JsonConvert.DeserializeObject<SerializableDictionary<string, string>>(json);
                }
                else
                {
                    m_Cache = new SerializableDictionary<string, string>();
                }
            }
            catch (Exception e)
            {
                InternalLog.LogException(e);
                m_Cache = new SerializableDictionary<string, string>();
            }
        }

        void Save()
        {
            try
            {
                var path = GetFilePath(m_ConversationId);
                if (path == null)
                    return;

                var directory = Path.GetDirectoryName(path);
                if (directory != null && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var json = JsonConvert.SerializeObject(m_Cache);
                File.WriteAllText(path, json);
            }
            catch (Exception e)
            {
                InternalLog.LogException(e);
            }
        }

        static string GetFilePath(string conversationId)
        {
            return Path.Combine(Application.dataPath, $"../{k_StoragePath}", $"{conversationId}.json");
        }

        static string GetStateKey<T>(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            return $"{typeof(T).FullName}_{key}";
        }
    }

}
