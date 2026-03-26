using System;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Editor.GraphGeneration
{
    /// <summary>
    /// Relation type for graph edges. Serializes to camelCase string in JSON (e.g. DependsOn -> "dependsOn").
    /// The 10 relation_type values used in AI.CoreGraph JSON (covers all 12 edge directories).
    /// </summary>
    internal enum GraphRelationType
    {
        DependsOn,
        ReferencedBy,
        InheritsFrom,
        Implements,
        Declares,
        Uses,
        Includes,
        Has,
        Contains,
        CanUse
    }

    /// <summary>
    /// Node type for graph edges (src_type / dst_type). Serializes to string in JSON.
    /// </summary>
    internal enum GraphNodeType
    {
        Asset,
        Scene,
        AssetType,
        Project,
        Tool,
        ToolCategory
    }

    internal static class GraphEnumJson
    {
        public static string ToCamelCase(string s)
        {
            if (string.IsNullOrEmpty(s) || char.IsLower(s[0])) return s;
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }
    }

    /// <summary>
    /// Serializes enum to camelCase string and deserializes from string (case-insensitive).
    /// </summary>
    internal sealed class GraphRelationTypeJsonConverter : JsonConverter<GraphRelationType>
    {
        public override void WriteJson(JsonWriter writer, GraphRelationType value, JsonSerializer serializer)
            => writer.WriteValue(ToJsonString(value));

        public override GraphRelationType ReadJson(JsonReader reader, Type objectType, GraphRelationType existingValue, bool hasExistingValue, JsonSerializer serializer)
            => FromJsonString(reader.Value?.ToString());

        public static string ToJsonString(GraphRelationType value) => GraphEnumJson.ToCamelCase(value.ToString());

        public static GraphRelationType FromJsonString(string value)
        {
            if (string.IsNullOrEmpty(value)) return default;
            return Enum.TryParse<GraphRelationType>(value, true, out var result) ? result : default;
        }
    }

    internal sealed class GraphNodeTypeJsonConverter : JsonConverter<GraphNodeType>
    {
        public override void WriteJson(JsonWriter writer, GraphNodeType value, JsonSerializer serializer)
            => writer.WriteValue(ToJsonString(value));

        public override GraphNodeType ReadJson(JsonReader reader, Type objectType, GraphNodeType existingValue, bool hasExistingValue, JsonSerializer serializer)
            => FromJsonString(reader.Value?.ToString());

        public static string ToJsonString(GraphNodeType value) => GraphEnumJson.ToCamelCase(value.ToString());

        public static GraphNodeType FromJsonString(string value)
        {
            if (string.IsNullOrEmpty(value)) return default;
            return Enum.TryParse<GraphNodeType>(value, true, out var result) ? result : default;
        }
    }

    /// <summary>
    /// Strong type for a node in the dependency graph. Matches the JSON shape written by GraphRestructurer.
    /// </summary>
    internal class GraphNode
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("dependencies_count")]
        public int? DependenciesCount { get; set; }

        [JsonProperty("dependents_count")]
        public int? DependentsCount { get; set; }

        [JsonProperty("asset_type")]
        public string AssetType { get; set; }
    }

    /// <summary>
    /// Strong type for an edge in the dependency graph. Matches the JSON shape written by GraphRestructurer.
    /// </summary>
    internal class GraphEdge
    {
        [JsonProperty("src_id")]
        public string SrcId { get; set; }

        [JsonProperty("dst_id")]
        public string DstId { get; set; }

        [JsonProperty("relation_type")]
        [JsonConverter(typeof(GraphRelationTypeJsonConverter))]
        public GraphRelationType RelationType { get; set; }

        [JsonProperty("src_type")]
        [JsonConverter(typeof(GraphNodeTypeJsonConverter))]
        public GraphNodeType SrcType { get; set; }

        [JsonProperty("dst_type")]
        [JsonConverter(typeof(GraphNodeTypeJsonConverter))]
        public GraphNodeType DstType { get; set; }
    }

    /// <summary>
    /// Edge plus direction for GetEdges API response (snake_case for tool contract).
    /// </summary>
    internal class GraphEdgeWithDirection
    {
        [JsonProperty("src_id")]
        public string SrcId { get; set; }

        [JsonProperty("dst_id")]
        public string DstId { get; set; }

        [JsonProperty("relation_type")]
        [JsonConverter(typeof(GraphRelationTypeJsonConverter))]
        public GraphRelationType RelationType { get; set; }

        [JsonProperty("src_type")]
        [JsonConverter(typeof(GraphNodeTypeJsonConverter))]
        public GraphNodeType SrcType { get; set; }

        [JsonProperty("dst_type")]
        [JsonConverter(typeof(GraphNodeTypeJsonConverter))]
        public GraphNodeType DstType { get; set; }

        [JsonProperty("direction")]
        public string Direction { get; set; }
    }

    /// <summary>
    /// Strong type for metadata.json. Matches the shape written by GraphRestructurer and read/updated by GraphRefreshManager.
    /// </summary>
    internal class GraphMetadata
    {
        [JsonProperty("total_assets")]
        public int TotalAssets { get; set; }

        [JsonProperty("last_updated")]
        public string LastUpdated { get; set; }
    }

    /// <summary>
    /// Strong type for asset type nodes in the dependency graph (e.g. assetTypes.json).
    /// </summary>
    internal class GraphAssetTypeNode
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("asset_count")]
        public int AssetCount { get; set; }
    }

    /// <summary>
    /// Minimal node reference for validation when only the id is needed (e.g. orphan check).
    /// </summary>
    internal class GraphNodeRef
    {
        [JsonProperty("id")]
        public string Id { get; set; }
    }

    /// <summary>
    /// Strong type for project node in project.json (counts updated by GraphRefreshManager).
    /// </summary>
    internal class GraphProjectNode
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("asset_count")]
        public int AssetCount { get; set; }

        [JsonProperty("scene_count")]
        public int SceneCount { get; set; }

        [JsonProperty("asset_type_count")]
        public int AssetTypeCount { get; set; }
    }
}
