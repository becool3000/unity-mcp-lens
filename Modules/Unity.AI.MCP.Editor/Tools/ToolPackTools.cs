using System;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry;
using Unity.AI.MCP.Editor.Lens;

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

    [McpTool(ToolPackCatalog.ListToolPacksToolName,
        "Lists the available Unity MCP tool packs, the active packs for this connection, and recommended next expansions.",
        "List Unity Tool Packs",
        Groups = new[] { "core", "assistant" },
        EnabledByDefault = true)]
    class ListToolPacksTool : IUnityMcpTool
    {
        public Task<object> ExecuteAsync(object parameters)
        {
            var connectionId = ExternalToolExecutionScope.Current?.ConnectionId;
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
            var connectionId = ExternalToolExecutionScope.Current?.ConnectionId;
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
            var connectionId = ExternalToolExecutionScope.Current?.ConnectionId;
            if (string.IsNullOrWhiteSpace(connectionId))
                return Task.FromResult<object>(Response.Error("Unity.ReadDetailRef requires an active Lens MCP bridge connection."));

            if (string.IsNullOrWhiteSpace(parameters?.RefId))
                return Task.FromResult<object>(Response.Error("A non-empty RefId is required."));

            if (!ToolDetailRefStore.TryRead(connectionId, parameters.RefId, out var payload))
            {
                return Task.FromResult<object>(Response.Error(
                    $"Detail ref '{parameters.RefId}' was not found for the current connection.",
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
