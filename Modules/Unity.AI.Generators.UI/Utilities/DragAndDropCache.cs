using System;
using Unity.AI.Toolkit.Utility;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Generators.UI.Utilities
{
    [Serializable]
    [FilePath("UserSettings/AI.Generators/DragAndDropCache.asset", FilePathAttribute.Location.ProjectFolder)]
    class DragAndDropCache : ScriptableSingleton<DragAndDropCache>
    {
        [SerializeField]
        public SerializableDictionary<string, string> entries = new();

        public void EnsureSaved()
        {
            Save(true);
        }
    }
}
