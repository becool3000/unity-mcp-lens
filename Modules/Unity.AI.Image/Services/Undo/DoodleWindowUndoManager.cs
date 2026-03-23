using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Generators.UI.Utilities;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Image.Services.Undo
{
    [Serializable]
    record DoodleWindowHistoryItem
    {
        /// <summary>
        /// Unique identifier of the state.
        /// </summary>
        public string guid;

        /// <summary>
        /// Layers of the state.
        /// </summary>
        public List<DoodleLayer> layers = new();
    }

    [Serializable]
    class DoodleWindowHistory : ScriptableObject, ISerializationCallbackReceiver
    {
        const string k_Directory = "Temp/DoodleWindowHistory";

        static DoodleWindowHistory s_Instance;

        public static DoodleWindowHistory instance
        {
            // single instance since a single window can be opened at a time
            get
            {
                if (!s_Instance)
                {
                    s_Instance = CreateInstance<DoodleWindowHistory>();
                    s_Instance.hideFlags = HideFlags.HideAndDontSave;
                    s_Instance.name = nameof(DoodleWindowHistory);
                }
                return s_Instance;
            }
        }

        [SerializeField] List<string> m_HistoryGuids = new();
        [NonSerialized] string m_LastGuid;

        string lastGuid => m_HistoryGuids.Count > 0 ? m_HistoryGuids[^1] : null;

        /// <summary>
        /// Whether the history can undo.
        /// </summary>
        public bool canUndo => m_HistoryGuids.Count > 1;

        /// <summary>
        /// Event triggered when the history changes.
        /// </summary>
        public event Action<DoodleWindowHistoryItem> StateChanged;

        DoodleWindowHistory() => ResetWithoutClearUndo();

        void ResetWithoutClearUndo()
        {
            if (Directory.Exists(k_Directory))
                Directory.Delete(k_Directory, true);
            Directory.CreateDirectory(k_Directory);
            m_HistoryGuids.Clear();
            m_LastGuid = null;
        }

        /// <summary>
        /// Clear the history and delete all saved states.
        /// </summary>
        public void Clear()
        {
            ResetWithoutClearUndo();
            UnityEditor.Undo.ClearUndo(this);
        }

        /// <summary>
        /// Push a new state to the history.
        /// </summary>
        /// <param name="state"> The state to push. </param>
        /// <returns> The guid of the pushed state. </returns>
        public string Push(DoodleWindowState state)
        {
            var shouldRecord = m_HistoryGuids.Count > 0;
            if (shouldRecord)
                UnityEditor.Undo.RecordObject(this, "Edit Doodle");
            var guid = Guid.NewGuid().ToString();
            var item = new DoodleWindowHistoryItem { guid = guid, layers = state.layers };
            WriteJson(guid, item);
            m_LastGuid = guid;
            m_HistoryGuids.Add(guid);
            if (shouldRecord)
            {
                EditorUtility.SetDirty(this);
                UnityEditor.Undo.IncrementCurrentGroup();
            }
            return guid;
        }

        /// <inheritdoc />
        public void OnBeforeSerialize() { }

        /// <inheritdoc />
        public void OnAfterDeserialize()
        {
            if (DomainReloadUtilities.WasDomainReloaded)
            {
                ResetWithoutClearUndo();
                return;
            }

            var guid = lastGuid;
            if (m_LastGuid != guid)
            {
                if (!string.IsNullOrEmpty(guid))
                {
                    try
                    {
                        // do not do cleanup for now, or we gonna lose the redo states
                        // CleanUpDirectory();
                        var item = ReadJson(guid);
                        InvokeItemChanged(item);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Failed to read history item {guid}: {e}");
                    }
                }
                m_LastGuid = guid;
            }
        }

        /// <summary>
        /// Called when the last item in the history refers to a new state.
        /// </summary>
        /// <param name="item"> The new state. </param>
        protected virtual void InvokeItemChanged(DoodleWindowHistoryItem item)
        {
            StateChanged?.Invoke(item);
        }

        void CleanUpDirectory()
        {
            var files = Directory.GetFiles(k_Directory);
            foreach (var file in files)
            {
                var guid = Path.GetFileNameWithoutExtension(file);
                if (!m_HistoryGuids.Contains(guid))
                    File.Delete(file);
            }
        }

        static void WriteJson(string guid, DoodleWindowHistoryItem item)
        {
            var path = Path.Combine(k_Directory, $"{guid}.json");
            using var file = File.CreateText(path);
            var serializer = new JsonSerializer { Formatting = Formatting.Indented };
            serializer.Serialize(file, item);
        }

        static DoodleWindowHistoryItem ReadJson(string guid)
        {
            var path = Path.Combine(k_Directory, $"{guid}.json");
            using var file = File.OpenText(path);
            var serializer = new JsonSerializer();
            return (DoodleWindowHistoryItem)serializer.Deserialize(file, typeof(DoodleWindowHistoryItem));
        }
    }
}
