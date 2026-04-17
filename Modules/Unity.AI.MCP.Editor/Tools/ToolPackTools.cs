using System;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry;
using Unity.AI.MCP.Editor.Lens;
using Unity.AI.MCP.Editor.Utils;
using UnityEditor;

namespace Unity.AI.MCP.Editor.Tools
{
    class SetToolPacksParams
    {
        [McpDescription("The non-foundation tool packs to activate for this connection. Foundation remains active automatically.")]
        public string[] Packs { get; set; } = Array.Empty<string>();
    }

    class ReadDetailRefParams
    {
        [McpDescription("The stored detail ref identifier to resolve.")]
        public string RefId { get; set; }
    }

    [McpTool(ToolPackCatalog.GetLensHealthToolName,
        "Returns a compact Lens health summary for the current Unity bridge connection, including active packs, exported tool count, bridge status, editor stability, and the recommended next action.",
        "Get Unity Lens Health",
        Groups = new[] { "core", "assistant" },
        EnabledByDefault = true)]
    class GetLensHealthTool : IUnityMcpTool
    {
        public Task<object> ExecuteAsync(object parameters)
        {
            var connectionId = McpToolExecutionScope.Current?.ConnectionId;
            var activeToolPacks = BridgeLensSessionRegistry.GetActiveToolPacks(connectionId);
            var bridgeSnapshot = BridgeStatusTracker.GetSnapshot();
            var blockingReasons = EditorStabilityUtility.GetBlockingReasons();
            bool isStable = blockingReasons.Count == 0;
            bool expectedRecoveryActive = IsExpectedRecoveryActive(bridgeSnapshot.ExpectedRecovery, bridgeSnapshot.ExpectedRecoveryExpiresUtc);

            return Task.FromResult<object>(Response.Success(
                "Retrieved Unity Lens health summary.",
                new
                {
                    activeToolPacks,
                    exportedToolCount = BridgeManifestBroker.GetExportedToolCount(activeToolPacks),
                    internalRegistryToolCount = BridgeManifestBroker.GetBridgeFacingToolCount(),
                    bridgeStatus = new
                    {
                        status = bridgeSnapshot.Status,
                        reason = bridgeSnapshot.Reason,
                        commandHealth = bridgeSnapshot.DirectCommandHealth,
                        toolDiscoveryMode = bridgeSnapshot.ToolDiscoveryMode,
                        manifestVersion = bridgeSnapshot.ManifestVersion,
                        profileCatalogVersion = bridgeSnapshot.ProfileCatalogVersion,
                        supportsToolSyncLens = bridgeSnapshot.SupportsToolSyncLens,
                        lastToolsChangedUtc = bridgeSnapshot.LastToolsChangedUtc,
                    },
                    editorStability = new
                    {
                        isStable,
                        state = ClassifyEditorStability(blockingReasons),
                        blockingReasons,
                        isCompiling = EditorApplication.isCompiling,
                        isUpdating = EditorApplication.isUpdating,
                        isPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode,
                        isBuildingPlayer = BuildPipeline.isBuildingPlayer,
                    },
                    expectedRecovery = new
                    {
                        isExpected = bridgeSnapshot.ExpectedRecovery,
                        isActive = expectedRecoveryActive,
                        expiresUtc = bridgeSnapshot.ExpectedRecoveryExpiresUtc,
                    },
                    lastCommandFailure = new
                    {
                        utc = bridgeSnapshot.LastCommandFailureUtc,
                        reason = bridgeSnapshot.LastCommandFailureReason,
                    },
                    recommendedNextAction = GetRecommendedNextAction(bridgeSnapshot, isStable, expectedRecoveryActive),
                }));
        }

        static bool IsExpectedRecoveryActive(bool expectedRecovery, string expectedRecoveryExpiresUtc)
        {
            if (!expectedRecovery)
                return false;

            if (string.IsNullOrWhiteSpace(expectedRecoveryExpiresUtc))
                return true;

            return DateTime.TryParse(expectedRecoveryExpiresUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var expiresUtc) &&
                expiresUtc > DateTime.UtcNow;
        }

        static string ClassifyEditorStability(System.Collections.Generic.IReadOnlyCollection<string> blockingReasons)
        {
            if (blockingReasons == null || blockingReasons.Count == 0)
                return "stable";

            if (blockingReasons.Contains("compiling"))
                return "compiling";

            if (blockingReasons.Contains("updating"))
                return "updating";

            if (blockingReasons.Contains("building_player"))
                return "building_player";

            if (blockingReasons.Contains("play_transition"))
                return "play_transition";

            return "unstable";
        }

        static string GetRecommendedNextAction(BridgeStatusSnapshot bridgeSnapshot, bool isStable, bool expectedRecoveryActive)
        {
            if (expectedRecoveryActive)
                return "Wait for Unity compile/reload recovery to finish before retrying broader Lens tool calls.";

            if (!isStable)
                return "Wait for the editor to reach a stable idle state before widening packs or running heavier Lens tools.";

            if (string.Equals(bridgeSnapshot.Status, "disconnected", StringComparison.OrdinalIgnoreCase))
                return "Reconnect or restart the Unity MCP bridge before using Lens tools.";

            if (string.Equals(bridgeSnapshot.DirectCommandHealth, "failed", StringComparison.OrdinalIgnoreCase))
                return "Retry one lightweight Lens probe. If it still fails, reconnect the Unity MCP bridge.";

            if (string.Equals(bridgeSnapshot.Status, "transport_degraded", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bridgeSnapshot.Status, "transport_recovering", StringComparison.OrdinalIgnoreCase))
            {
                return "Retry a lightweight Lens probe or wait briefly for bridge recovery before using broader tools.";
            }

            if (string.Equals(bridgeSnapshot.Status, "ready", StringComparison.OrdinalIgnoreCase))
                return "Proceed with Lens tools. Activate additional packs only when they are needed.";

            return "Use a lightweight Lens probe before broader Unity operations.";
        }
    }

    [McpTool(ToolPackCatalog.ListToolPacksToolName,
        "Lists the available Unity MCP tool packs, the active packs for this connection, and recommended next expansions.",
        "List Unity Tool Packs",
        Groups = new[] { "core", "assistant" },
        EnabledByDefault = true)]
    class ListToolPacksTool : IUnityMcpTool
    {
        public Task<object> ExecuteAsync(object parameters)
        {
            var connectionId = McpToolExecutionScope.Current?.ConnectionId;
            var activeToolPacks = BridgeLensSessionRegistry.GetActiveToolPacks(connectionId);

            return Task.FromResult<object>(Response.Success(
                "Retrieved available Unity MCP tool packs.",
                new
                {
                    activeToolPacks,
                    maxAdditionalPacks = ToolPackCatalog.MaxAdditionalPacks,
                    recommendedNextPacks = ToolPackCatalog.GetRecommendedNextPacks(activeToolPacks),
                    packs = ToolPackCatalog.GetPackSummaries(activeToolPacks).ToArray()
                }));
        }
    }

    [McpTool(ToolPackCatalog.SetToolPacksToolName,
        "Sets the active Unity MCP tool packs for this connection. Foundation stays active automatically and at most two additional packs may be selected.",
        "Set Unity Tool Packs",
        Groups = new[] { "core", "assistant" },
        EnabledByDefault = true)]
    class SetToolPacksTool : IUnityMcpTool<SetToolPacksParams>
    {
        public Task<object> ExecuteAsync(SetToolPacksParams parameters)
        {
            var connectionId = McpToolExecutionScope.Current?.ConnectionId;
            if (string.IsNullOrWhiteSpace(connectionId))
                return Task.FromResult<object>(Response.Error("Unity.SetToolPacks requires an active Lens MCP bridge connection."));

            if (!BridgeLensSessionRegistry.TrySetActiveToolPacks(connectionId, parameters?.Packs, out var normalizedPacks, out var error))
                return Task.FromResult<object>(Response.Error(error ?? "Failed to update active tool packs."));

            var manifest = BridgeManifestBroker.SetToolPacks(connectionId, normalizedPacks, includeSchemas: false, out error);
            if (!string.IsNullOrWhiteSpace(error) || manifest == null)
                return Task.FromResult<object>(Response.Error(error ?? "Failed to rebuild tool manifest after updating tool packs."));

            return Task.FromResult<object>(Response.Success(
                "Updated active Unity MCP tool packs.",
                new
                {
                    activeToolPacks = manifest.activeToolPacks,
                    manifestVersion = manifest.manifestVersion,
                    bridgeSessionId = manifest.bridgeSessionId,
                    toolCount = manifest.tools?.Length ?? 0,
                    recommendedNextPacks = ToolPackCatalog.GetRecommendedNextPacks(manifest.activeToolPacks)
                }));
        }
    }

    [McpTool(ToolPackCatalog.ReadDetailRefToolName,
        "Reads a stored detail ref payload previously returned by a compact Unity MCP tool result.",
        "Read Unity Detail Ref",
        Groups = new[] { "core", "assistant" },
        EnabledByDefault = true)]
    class ReadDetailRefTool : IUnityMcpTool<ReadDetailRefParams>
    {
        public Task<object> ExecuteAsync(ReadDetailRefParams parameters)
        {
            var connectionId = McpToolExecutionScope.Current?.ConnectionId;
            if (string.IsNullOrWhiteSpace(connectionId))
                return Task.FromResult<object>(Response.Error("Unity.ReadDetailRef requires an active Lens MCP bridge connection."));

            if (string.IsNullOrWhiteSpace(parameters?.RefId))
                return Task.FromResult<object>(Response.Error("A non-empty RefId is required."));

            if (!ToolDetailRefStore.TryRead(connectionId, parameters.RefId, out var payload))
            {
                return Task.FromResult<object>(Response.Error(
                    $"Detail ref '{parameters.RefId}' was not found in the active Lens detail cache.",
                    new { refId = parameters.RefId, availableRefs = ToolDetailRefStore.GetStoredRefIds(connectionId) }));
            }

            return Task.FromResult<object>(Response.Success(
                $"Resolved detail ref '{parameters.RefId}'.",
                new
                {
                    payload.refId,
                    payload.contentType,
                    payload.createdUtc,
                    payload.meta,
                    payload = payload.payload
                }));
        }
    }
}
