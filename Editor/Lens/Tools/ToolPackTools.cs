using System;
using System.Linq;
using System.Threading.Tasks;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Utils;
using UnityEditor;

namespace Becool.UnityMcpLens.Editor.Tools
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

    class ToolsDescribeParams
    {
        [McpDescription("Optional tool name to describe. Dot and underscore forms are equivalent.", Required = false)]
        public string ToolName { get; set; }

        [McpDescription("Include input/output schemas and annotations in the result.", Required = false, Default = true)]
        public bool IncludeSchemas { get; set; } = true;

        [McpDescription("Include the non-foundation packs that must be activated before calling each tool.", Required = false, Default = true)]
        public bool IncludePackRequirements { get; set; } = true;

        [McpDescription("Include compact example call metadata.", Required = false, Default = false)]
        public bool IncludeExamples { get; set; } = false;

        [McpDescription("Maximum number of matching tools to return.", Required = false, Default = 100)]
        public int MaxTools { get; set; } = 100;
    }

    class ToolsMenuParams
    {
        [McpDescription("Maximum number of tool rows to include per pack.", Required = false, Default = 40)]
        public int MaxToolsPerPack { get; set; } = 40;
    }

    class ToolsActivateAndVerifyParams
    {
        [McpDescription("The non-foundation tool packs to activate for this connection. Foundation remains active automatically.")]
        public string[] Packs { get; set; } = Array.Empty<string>();

        [McpDescription("Expected tools that should be present after activation. Dot and underscore forms are equivalent.", Required = false)]
        public string[] ExpectedTools { get; set; } = Array.Empty<string>();

        [McpDescription("Include schemas in the verification manifest.", Required = false, Default = false)]
        public bool IncludeSchemas { get; set; } = false;
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
                    toolSurfaceMode = activeToolPacks.Contains(ToolPackCatalog.FullPackId, StringComparer.OrdinalIgnoreCase) ? "static_all" : "dynamic_packs",
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

            var manifest = BridgeManifestBroker.SetToolPacks(connectionId, parameters?.Packs, includeSchemas: false, out var error);
            if (!string.IsNullOrWhiteSpace(error) || manifest == null)
                return Task.FromResult<object>(Response.Error(error ?? "Failed to rebuild tool manifest after updating tool packs."));

            bool unchanged = string.Equals(manifest.kind, "unchanged", StringComparison.OrdinalIgnoreCase);
            return Task.FromResult<object>(Response.Success(
                unchanged ? "Active Unity MCP tool packs unchanged." : "Updated active Unity MCP tool packs.",
                new
                {
                    activeToolPacks = manifest.activeToolPacks,
                    manifestVersion = manifest.manifestVersion,
                    bridgeSessionId = manifest.bridgeSessionId,
                    unchanged,
                    manifestKind = manifest.kind,
                    toolCount = manifest.tools?.Length ?? BridgeManifestBroker.GetExportedToolCount(manifest.activeToolPacks),
                    recommendedNextPacks = ToolPackCatalog.GetRecommendedNextPacks(manifest.activeToolPacks),
                    clientSurface = new
                    {
                        expectedRefresh = !unchanged,
                        note = unchanged
                            ? "Active packs are unchanged, so no client tool-surface refresh is expected."
                            : "The Lens bridge manifest changed. MCP clients should refresh their callable tool surface after notifications/tools/list_changed; if Codex still cannot call described tools, use Unity.Tools.Describe or helper scripts until the client session refreshes."
                    }
                }));
        }
    }

    [McpTool(ToolPackCatalog.ToolsDescribeToolName,
        "Describes live Unity MCP Lens tools, including current active packs, manifest version, required packs, and schemas when requested.",
        "Describe Unity Tools",
        Groups = new[] { "core", "assistant" },
        EnabledByDefault = true)]
    class ToolsDescribeTool : IUnityMcpTool<ToolsDescribeParams>
    {
        public Task<object> ExecuteAsync(ToolsDescribeParams parameters)
        {
            parameters ??= new ToolsDescribeParams();
            var connectionId = McpToolExecutionScope.Current?.ConnectionId;
            var rawData = BridgeManifestBroker.DescribeTools(
                connectionId,
                parameters.ToolName,
                parameters.IncludeSchemas,
                parameters.IncludePackRequirements,
                parameters.IncludeExamples,
                parameters.MaxTools <= 0 ? 100 : parameters.MaxTools);

            return Task.FromResult<object>(Response.Success(
                string.IsNullOrWhiteSpace(parameters.ToolName)
                    ? "Described live Unity MCP Lens tools."
                    : $"Described live Unity MCP Lens tool metadata for '{parameters.ToolName}'.",
                rawData));
        }
    }

    [McpTool(ToolPackCatalog.ToolsMenuToolName,
        "Returns a compact pack-grouped menu of real Unity MCP Lens tools, including read-only hints and workflow recommendations. It does not route calls; call the real native tools directly.",
        "Unity Tools Menu",
        Groups = new[] { "core", "assistant" },
        EnabledByDefault = true)]
    class ToolsMenuTool : IUnityMcpTool<ToolsMenuParams>
    {
        public Task<object> ExecuteAsync(ToolsMenuParams parameters)
        {
            parameters ??= new ToolsMenuParams();
            var connectionId = McpToolExecutionScope.Current?.ConnectionId;
            var rawData = BridgeManifestBroker.GetToolMenu(
                connectionId,
                parameters.MaxToolsPerPack <= 0 ? 40 : parameters.MaxToolsPerPack);

            return Task.FromResult<object>(Response.Success(
                "Retrieved Unity MCP Lens tool menu.",
                rawData));
        }
    }

    [McpTool(ToolPackCatalog.ToolsActivateAndVerifyToolName,
        "Activates Unity MCP Lens tool packs and verifies expected tools against the Lens bridge-visible tool surface.",
        "Activate And Verify Unity Tools",
        Groups = new[] { "core", "assistant" },
        EnabledByDefault = true)]
    class ToolsActivateAndVerifyTool : IUnityMcpTool<ToolsActivateAndVerifyParams>
    {
        public Task<object> ExecuteAsync(ToolsActivateAndVerifyParams parameters)
        {
            parameters ??= new ToolsActivateAndVerifyParams();
            var connectionId = McpToolExecutionScope.Current?.ConnectionId;
            if (string.IsNullOrWhiteSpace(connectionId))
                return Task.FromResult<object>(Response.Error("Unity.Tools.ActivateAndVerify requires an active Lens MCP bridge connection."));

            var manifest = BridgeManifestBroker.SetToolPacks(connectionId, parameters.Packs, includeSchemas: false, out var error);
            if (!string.IsNullOrWhiteSpace(error) || manifest == null)
                return Task.FromResult<object>(Response.Error(error ?? "Failed to activate Unity MCP tool packs."));

            var verificationManifest = BridgeManifestBroker.GetManifest(
                connectionId,
                knownBridgeSessionId: null,
                knownManifestVersion: null,
                includeSchemas: parameters.IncludeSchemas);
            var exportedToolNames = (verificationManifest.tools ?? Array.Empty<BridgeToolDescriptor>())
                .Select(tool => tool.name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var expectedTools = NormalizeToolNames(parameters.ExpectedTools);
            var matchedExpectedTools = expectedTools
                .Where(expected => exportedToolNames.Any(actual => ToolNamesMatch(actual, expected)))
                .ToArray();
            var missingExpectedTools = expectedTools
                .Where(expected => !exportedToolNames.Any(actual => ToolNamesMatch(actual, expected)))
                .ToArray();
            bool success = missingExpectedTools.Length == 0;
            bool unchanged = string.Equals(manifest.kind, "unchanged", StringComparison.OrdinalIgnoreCase);
            var rawData = new
            {
                success,
                activeToolPacks = verificationManifest.activeToolPacks,
                manifestVersion = verificationManifest.manifestVersion,
                bridgeSessionId = verificationManifest.bridgeSessionId,
                profileCatalogVersion = verificationManifest.profileCatalogVersion,
                manifestKind = manifest.kind,
                unchanged,
                exportedToolCount = exportedToolNames.Length,
                expectedTools,
                matchedExpectedTools,
                missingFromServerSurface = missingExpectedTools,
                missingFromClient = missingExpectedTools,
                toolsListChangedNotificationSent = (bool?)null,
                clientSurface = new
                {
                    serverSurfaceVerified = success,
                    clientCallableState = "not_observable_from_unity_bridge",
                    note = success
                        ? "Expected tools are active in the Lens bridge surface. If Codex still cannot call them directly, classify that as client dynamic-indexing drift and use the MCP host or batch helper fallback."
                        : "One or more expected tools were not active in the Lens bridge surface after pack activation."
                },
                workaroundHint = success
                    ? "Use described tool metadata or Invoke-UnityMcpBatch if the MCP client callable list remains stale after activation."
                    : "Activate only packs that contain the missing tools, then rerun verification."
            };

            return Task.FromResult<object>(success
                ? Response.Success("Activated Unity MCP tool packs and verified expected bridge-visible tools.", rawData)
                : Response.Error("Activated Unity MCP tool packs, but expected tools were missing from the bridge-visible surface.", rawData));
        }

        static string[] NormalizeToolNames(string[] toolNames)
        {
            return (toolNames ?? Array.Empty<string>())
                .Select(McpToolRegistry.NormalizeToolName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }

        static bool ToolNamesMatch(string actualToolName, string expectedToolName)
        {
            return string.Equals(
                McpToolRegistry.NormalizeToolName(actualToolName),
                McpToolRegistry.NormalizeToolName(expectedToolName),
                StringComparison.OrdinalIgnoreCase);
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
