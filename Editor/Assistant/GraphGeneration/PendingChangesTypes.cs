using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Editor.GraphGeneration
{
    /// <summary>
    /// Change type for entries in .pending_changes.json. Shared by GraphRefreshPostprocessor and GraphRefreshManager.
    /// </summary>
    internal enum AssetChangeType
    {
        Imported,
        Deleted,
        Moved,
        DomainReload
    }

    internal class AssetChangeTypeJsonConverter : JsonConverter<AssetChangeType>
    {
        private const string ImportedStr = "imported";
        private const string DeletedStr = "deleted";
        private const string MovedStr = "moved";
        private const string DomainReloadStr = "domain_reload";

        public override void WriteJson(JsonWriter writer, AssetChangeType value, JsonSerializer serializer)
        {
            writer.WriteValue(ToJsonString(value));
        }

        public override AssetChangeType ReadJson(JsonReader reader, Type objectType, AssetChangeType existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var s = reader.Value?.ToString();
            return FromJsonString(s);
        }

        internal static string ToJsonString(AssetChangeType value)
        {
            return value switch
            {
                AssetChangeType.Imported => ImportedStr,
                AssetChangeType.Deleted => DeletedStr,
                AssetChangeType.Moved => MovedStr,
                AssetChangeType.DomainReload => DomainReloadStr,
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
            };
        }

        internal static AssetChangeType FromJsonString(string value)
        {
            return value switch
            {
                ImportedStr => AssetChangeType.Imported,
                DeletedStr => AssetChangeType.Deleted,
                MovedStr => AssetChangeType.Moved,
                DomainReloadStr => AssetChangeType.DomainReload,
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown AssetChangeType")
            };
        }
    }

    /// <summary>
    /// Root structure of .pending_changes.json. Single source of truth for GraphRefreshPostprocessor and GraphRefreshManager.
    /// </summary>
    [Serializable]
    internal class PendingChangesFile
    {
        public string version;
        public string lastUpdate;
        public int totalChanges;
        public List<AssetChangeEvent> changes;
    }

    [Serializable]
    internal class AssetChangeEvent
    {
        [JsonConverter(typeof(AssetChangeTypeJsonConverter))]
        public AssetChangeType type;
        public string path;
        public string oldPath; // For "moved" events
        public string timestamp;
    }
}
