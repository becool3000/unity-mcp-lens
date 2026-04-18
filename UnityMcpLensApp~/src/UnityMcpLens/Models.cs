using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnityMcpLens;

sealed class BridgeStatusFile
{
    [JsonPropertyName("connection_path")]
    public string? ConnectionPath { get; set; }

    [JsonPropertyName("connection_type")]
    public string? ConnectionType { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("expected_recovery")]
    public bool ExpectedRecovery { get; set; }

    [JsonPropertyName("project_path")]
    public string? ProjectPath { get; set; }

    [JsonPropertyName("project_root")]
    public string? ProjectRoot { get; set; }

    [JsonPropertyName("last_heartbeat")]
    public string? LastHeartbeat { get; set; }

    [JsonPropertyName("bridge_session_id")]
    public string? BridgeSessionId { get; set; }

    [JsonPropertyName("manifest_version")]
    public long ManifestVersion { get; set; }

    [JsonPropertyName("supports_tool_sync_lens")]
    public bool SupportsToolSyncLens { get; set; }
}

sealed class BridgeEnvelope<T>
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("result")]
    public T? Result { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }
}

sealed class RegisterClientResult
{
    [JsonPropertyName("bridgeSessionId")]
    public string? BridgeSessionId { get; set; }

    [JsonPropertyName("manifestVersion")]
    public long ManifestVersion { get; set; }

    [JsonPropertyName("profileCatalogVersion")]
    public string? ProfileCatalogVersion { get; set; }

    [JsonPropertyName("activeToolPacks")]
    public string[] ActiveToolPacks { get; set; } = [];
}

sealed class BridgeToolDescriptor
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("schemaHash")]
    public string? SchemaHash { get; set; }

    [JsonPropertyName("groups")]
    public string[] Groups { get; set; } = [];

    [JsonPropertyName("packs")]
    public string[] Packs { get; set; } = [];

    [JsonPropertyName("readOnlyHint")]
    public bool ReadOnlyHint { get; set; }

    [JsonPropertyName("inputSchema")]
    public JsonElement InputSchema { get; set; }

    [JsonPropertyName("outputSchema")]
    public JsonElement OutputSchema { get; set; }

    [JsonPropertyName("annotations")]
    public JsonElement Annotations { get; set; }
}

sealed class BridgeManifestDelta
{
    [JsonPropertyName("added")]
    public BridgeToolDescriptor[] Added { get; set; } = [];

    [JsonPropertyName("updated")]
    public BridgeToolDescriptor[] Updated { get; set; } = [];

    [JsonPropertyName("removed")]
    public string[] Removed { get; set; } = [];
}

sealed class BridgeManifestResult
{
    [JsonPropertyName("bridgeSessionId")]
    public string? BridgeSessionId { get; set; }

    [JsonPropertyName("manifestVersion")]
    public long ManifestVersion { get; set; }

    [JsonPropertyName("profileCatalogVersion")]
    public string? ProfileCatalogVersion { get; set; }

    [JsonPropertyName("activeToolPacks")]
    public string[] ActiveToolPacks { get; set; } = [];

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "full";

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("hashMinimal")]
    public string? HashMinimal { get; set; }

    [JsonPropertyName("hashFull")]
    public string? HashFull { get; set; }

    [JsonPropertyName("tools")]
    public BridgeToolDescriptor[] Tools { get; set; } = [];

    [JsonPropertyName("delta")]
    public BridgeManifestDelta? Delta { get; set; }
}

sealed class BridgeToolSchemasResult
{
    [JsonPropertyName("bridgeSessionId")]
    public string? BridgeSessionId { get; set; }

    [JsonPropertyName("manifestVersion")]
    public long ManifestVersion { get; set; }

    [JsonPropertyName("activeToolPacks")]
    public string[] ActiveToolPacks { get; set; } = [];

    [JsonPropertyName("tools")]
    public BridgeToolDescriptor[] Tools { get; set; } = [];
}

sealed class BridgeToolsChangedNotification
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("bridgeSessionId")]
    public string? BridgeSessionId { get; set; }

    [JsonPropertyName("manifestVersion")]
    public long ManifestVersion { get; set; }

    [JsonPropertyName("profileCatalogVersion")]
    public string? ProfileCatalogVersion { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("lastToolsChangedUtc")]
    public string? LastToolsChangedUtc { get; set; }
}
