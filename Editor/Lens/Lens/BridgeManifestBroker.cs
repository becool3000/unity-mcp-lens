using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils;

namespace Becool.UnityMcpLens.Editor.Lens
{
    sealed class BridgeToolDescriptor
    {
        public string name { get; set; }
        public string title { get; set; }
        public string description { get; set; }
        public string schemaHash { get; set; }
        public string[] groups { get; set; }
        public string[] packs { get; set; }
        public bool readOnlyHint { get; set; }
        public object inputSchema { get; set; }
        public object outputSchema { get; set; }
        public object annotations { get; set; }
    }

    sealed class BridgeManifestDelta
    {
        public BridgeToolDescriptor[] added { get; set; }
        public BridgeToolDescriptor[] updated { get; set; }
        public string[] removed { get; set; }
    }

    sealed class BridgeManifestResult
    {
        public string bridgeSessionId { get; set; }
        public long manifestVersion { get; set; }
        public string profileCatalogVersion { get; set; }
        public string[] activeToolPacks { get; set; }
        public string kind { get; set; }
        public string reason { get; set; }
        public string hashMinimal { get; set; }
        public string hashFull { get; set; }
        public BridgeToolDescriptor[] tools { get; set; }
        public BridgeManifestDelta delta { get; set; }
    }

    sealed class BridgeToolSchemasResult
    {
        public string bridgeSessionId { get; set; }
        public long manifestVersion { get; set; }
        public string[] activeToolPacks { get; set; }
        public BridgeToolDescriptor[] tools { get; set; }
    }

    sealed class BridgeToolSyncStatus
    {
        public string BridgeSessionId { get; set; }
        public long ManifestVersion { get; set; }
        public string ProfileCatalogVersion { get; set; }
        public string LastToolsChangedUtc { get; set; }
    }

    sealed class BridgeToolsChangedNotification
    {
        public string type { get; set; } = "tools_changed";
        public string bridgeSessionId { get; set; }
        public long manifestVersion { get; set; }
        public string profileCatalogVersion { get; set; }
        public string reason { get; set; }
        public string lastToolsChangedUtc { get; set; }
    }

    static class BridgeManifestBroker
    {
        sealed class ManifestHistoryEntry
        {
            public long Version { get; set; }
            public string Reason { get; set; }
            public BridgeToolDescriptor[] Tools { get; set; }
        }

        const int MaxHistoryEntries = 16;

        static readonly object s_Lock = new();
        static string s_BridgeSessionId = Guid.NewGuid().ToString("N");
        static long s_ManifestVersion;
        static string s_LastToolsChangedUtc;
        static string s_LastReason = "startup";
        static BridgeToolDescriptor[] s_CurrentTools = Array.Empty<BridgeToolDescriptor>();
        static string s_CurrentHashMinimal;
        static string s_CurrentHashFull;
        static readonly LinkedList<ManifestHistoryEntry> s_History = new();

        public static void ResetSession(string reason = "startup")
        {
            lock (s_Lock)
            {
                s_BridgeSessionId = Guid.NewGuid().ToString("N");
                s_ManifestVersion = 0;
                s_LastToolsChangedUtc = DateTime.UtcNow.ToString("O");
                s_LastReason = reason;
                s_CurrentTools = Array.Empty<BridgeToolDescriptor>();
                s_CurrentHashMinimal = null;
                s_CurrentHashFull = null;
                s_History.Clear();
                RebuildSnapshotLocked(reason);
            }
        }

        public static BridgeToolSyncStatus GetStatus()
        {
            lock (s_Lock)
            {
                EnsureCurrentSnapshotLocked();
                return new BridgeToolSyncStatus
                {
                    BridgeSessionId = s_BridgeSessionId,
                    ManifestVersion = s_ManifestVersion,
                    ProfileCatalogVersion = ToolPackCatalog.ProfileCatalogVersion,
                    LastToolsChangedUtc = s_LastToolsChangedUtc
                };
            }
        }

        public static int GetBridgeFacingToolCount()
        {
            lock (s_Lock)
            {
                EnsureCurrentSnapshotLocked();
                return s_CurrentTools?.Length ?? 0;
            }
        }

        public static int GetExportedToolCount(IEnumerable<string> activePacks)
        {
            lock (s_Lock)
            {
                EnsureCurrentSnapshotLocked();
                return FilterToolsForPacks(s_CurrentTools, activePacks, includeSchemas: false).Length;
            }
        }

        public static bool TryGetPackEnforcementState(string connectionId, out string[] activeToolPacks)
        {
            if (BridgeLensSessionRegistry.TryGetConnectionState(connectionId, out var state) &&
                state?.Capabilities?.SupportsToolSyncLens == true)
            {
                activeToolPacks = state.ActiveToolPacks?.Length > 0
                    ? state.ActiveToolPacks.ToArray()
                    : ToolPackCatalog.DefaultActivePacks;
                return true;
            }

            activeToolPacks = ToolPackCatalog.DefaultActivePacks;
            return false;
        }

        public static McpToolInfo[] FilterAvailableToolsForConnection(string connectionId, IEnumerable<McpToolInfo> tools)
        {
            var toolArray = tools?.ToArray() ?? Array.Empty<McpToolInfo>();
            if (!TryGetPackEnforcementState(connectionId, out var activeToolPacks))
                return toolArray;

            return toolArray
                .Where(tool => IsToolAllowedForPacks(tool?.name, activeToolPacks))
                .OrderBy(tool => tool.name, StringComparer.Ordinal)
                .ToArray();
        }

        public static bool IsToolAllowedForConnection(string connectionId, string toolName)
        {
            if (!TryGetPackEnforcementState(connectionId, out var activeToolPacks))
                return true;

            return IsToolAllowedForPacks(toolName, activeToolPacks);
        }

        public static BridgeToolsChangedNotification MarkToolGraphChanged(string reason)
        {
            lock (s_Lock)
            {
                RebuildSnapshotLocked(reason);
                return new BridgeToolsChangedNotification
                {
                    bridgeSessionId = s_BridgeSessionId,
                    manifestVersion = s_ManifestVersion,
                    profileCatalogVersion = ToolPackCatalog.ProfileCatalogVersion,
                    reason = s_LastReason,
                    lastToolsChangedUtc = s_LastToolsChangedUtc
                };
            }
        }

        public static BridgeManifestResult GetManifest(
            string connectionId,
            string knownBridgeSessionId,
            long? knownManifestVersion,
            bool includeSchemas)
        {
            lock (s_Lock)
            {
                EnsureCurrentSnapshotLocked();

                var activeToolPacks = BridgeLensSessionRegistry.GetActiveToolPacks(connectionId);
                var filteredCurrent = FilterToolsForPacks(s_CurrentTools, activeToolPacks, includeSchemas);
                var currentHashes = ComputeHashes(filteredCurrent);

                if (!string.Equals(knownBridgeSessionId, s_BridgeSessionId, StringComparison.OrdinalIgnoreCase) ||
                    !knownManifestVersion.HasValue)
                {
                    BridgeLensSessionRegistry.UpdateAcknowledgedManifest(connectionId, s_BridgeSessionId, s_ManifestVersion);
                    return CreateFullResult(filteredCurrent, activeToolPacks, currentHashes, "bootstrap");
                }

                if (knownManifestVersion.Value == s_ManifestVersion)
                {
                    BridgeLensSessionRegistry.UpdateAcknowledgedManifest(connectionId, s_BridgeSessionId, s_ManifestVersion);
                    return new BridgeManifestResult
                    {
                        bridgeSessionId = s_BridgeSessionId,
                        manifestVersion = s_ManifestVersion,
                        profileCatalogVersion = ToolPackCatalog.ProfileCatalogVersion,
                        activeToolPacks = activeToolPacks,
                        kind = "unchanged",
                        reason = s_LastReason,
                        hashMinimal = currentHashes.minimal,
                        hashFull = currentHashes.full
                    };
                }

                var previousHistoryEntry = s_History.FirstOrDefault(entry => entry.Version == knownManifestVersion.Value);
                if (previousHistoryEntry == null)
                {
                    BridgeLensSessionRegistry.UpdateAcknowledgedManifest(connectionId, s_BridgeSessionId, s_ManifestVersion);
                    return CreateFullResult(filteredCurrent, activeToolPacks, currentHashes, "history_miss");
                }

                var filteredPrevious = FilterToolsForPacks(previousHistoryEntry.Tools, activeToolPacks, includeSchemas);
                var delta = BuildDelta(filteredPrevious, filteredCurrent);
                BridgeLensSessionRegistry.UpdateAcknowledgedManifest(connectionId, s_BridgeSessionId, s_ManifestVersion);
                return new BridgeManifestResult
                {
                    bridgeSessionId = s_BridgeSessionId,
                    manifestVersion = s_ManifestVersion,
                    profileCatalogVersion = ToolPackCatalog.ProfileCatalogVersion,
                    activeToolPacks = activeToolPacks,
                    kind = "delta",
                    reason = s_LastReason,
                    hashMinimal = currentHashes.minimal,
                    hashFull = currentHashes.full,
                    delta = delta
                };
            }
        }

        public static BridgeManifestResult SetToolPacks(
            string connectionId,
            IEnumerable<string> requestedPacks,
            bool includeSchemas,
            out string error)
        {
            error = null;
            if (!BridgeLensSessionRegistry.TrySetActiveToolPacks(connectionId, requestedPacks, out var normalizedPacks, out var unchanged, out error))
                return null;

            lock (s_Lock)
            {
                EnsureCurrentSnapshotLocked();
                var filteredCurrent = FilterToolsForPacks(s_CurrentTools, normalizedPacks, includeSchemas && !unchanged);
                var currentHashes = ComputeHashes(filteredCurrent);
                BridgeLensSessionRegistry.UpdateAcknowledgedManifest(connectionId, s_BridgeSessionId, s_ManifestVersion);
                if (unchanged)
                {
                    return new BridgeManifestResult
                    {
                        bridgeSessionId = s_BridgeSessionId,
                        manifestVersion = s_ManifestVersion,
                        profileCatalogVersion = ToolPackCatalog.ProfileCatalogVersion,
                        activeToolPacks = normalizedPacks,
                        kind = "unchanged",
                        reason = "tool_packs_unchanged",
                        hashMinimal = currentHashes.minimal,
                        hashFull = currentHashes.full
                    };
                }

                return CreateFullResult(filteredCurrent, normalizedPacks, currentHashes, "tool_packs_updated");
            }
        }

        public static BridgeToolSchemasResult GetToolSchemas(string connectionId, IEnumerable<string> toolNames)
        {
            lock (s_Lock)
            {
                EnsureCurrentSnapshotLocked();

                var requestedNames = new HashSet<string>(
                    (toolNames ?? Array.Empty<string>())
                        .Select(McpToolRegistry.NormalizeToolName)
                        .Where(name => !string.IsNullOrWhiteSpace(name)),
                    StringComparer.OrdinalIgnoreCase);
                var activeToolPacks = BridgeLensSessionRegistry.GetActiveToolPacks(connectionId);
                var filteredTools = FilterToolsForPacks(s_CurrentTools, activeToolPacks, includeSchemas: true)
                    .Where(tool => requestedNames.Contains(McpToolRegistry.NormalizeToolName(tool.name)))
                    .ToArray();

                BridgeLensSessionRegistry.UpdateAcknowledgedManifest(connectionId, s_BridgeSessionId, s_ManifestVersion);
                return new BridgeToolSchemasResult
                {
                    bridgeSessionId = s_BridgeSessionId,
                    manifestVersion = s_ManifestVersion,
                    activeToolPacks = activeToolPacks,
                    tools = filteredTools
                };
            }
        }

        public static object DescribeTools(
            string connectionId,
            string toolName,
            bool includeSchemas,
            bool includePackRequirements,
            bool includeExamples,
            int maxTools)
        {
            lock (s_Lock)
            {
                EnsureCurrentSnapshotLocked();

                maxTools = Math.Max(1, Math.Min(500, maxTools));
                var activeToolPacks = BridgeLensSessionRegistry.GetActiveToolPacks(connectionId);
                var normalizedQuery = McpToolRegistry.NormalizeToolName(toolName);
                var allTools = s_CurrentTools ?? Array.Empty<BridgeToolDescriptor>();
                var matches = allTools.AsEnumerable();
                bool exactMatch = false;

                if (!string.IsNullOrWhiteSpace(normalizedQuery))
                {
                    var exactMatches = allTools
                        .Where(tool => string.Equals(McpToolRegistry.NormalizeToolName(tool.name), normalizedQuery, StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    exactMatch = exactMatches.Length > 0;
                    matches = exactMatch
                        ? exactMatches
                        : allTools.Where(tool =>
                            (tool.name?.IndexOf(toolName, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                            (McpToolRegistry.NormalizeToolName(tool.name)?.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                            (tool.title?.IndexOf(toolName, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                            (tool.description?.IndexOf(toolName, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
                }

                var matchedTools = matches
                    .OrderBy(tool => tool.name, StringComparer.Ordinal)
                    .ToArray();
                var returnedTools = matchedTools
                    .Take(maxTools)
                    .Select(tool => BuildToolDescription(CloneDescriptor(tool, includeSchemas), includePackRequirements, includeExamples))
                    .ToArray();

                BridgeLensSessionRegistry.UpdateAcknowledgedManifest(connectionId, s_BridgeSessionId, s_ManifestVersion);
                return new
                {
                    bridgeSessionId = s_BridgeSessionId,
                    manifestVersion = s_ManifestVersion,
                    profileCatalogVersion = ToolPackCatalog.ProfileCatalogVersion,
                    activeToolPacks,
                    query = string.IsNullOrWhiteSpace(toolName) ? null : toolName,
                    exactMatch,
                    includeSchemas,
                    includePackRequirements,
                    includeExamples,
                    totalToolCount = allTools.Length,
                    matchedToolCount = matchedTools.Length,
                    returnedToolCount = returnedTools.Length,
                    truncated = matchedTools.Length > returnedTools.Length,
                    maxTools,
                    clientSurfaceFallback = BuildClientSurfaceFallback(),
                    tools = returnedTools
                };
            }
        }

        public static object GetToolMenu(string connectionId, int maxToolsPerPack)
        {
            lock (s_Lock)
            {
                EnsureCurrentSnapshotLocked();

                maxToolsPerPack = Math.Max(1, Math.Min(100, maxToolsPerPack));
                var activeToolPacks = BridgeLensSessionRegistry.GetActiveToolPacks(connectionId);
                bool fullSurface = activeToolPacks.Contains(ToolPackCatalog.FullPackId, StringComparer.OrdinalIgnoreCase);
                var allTools = s_CurrentTools ?? Array.Empty<BridgeToolDescriptor>();
                var packs = ToolPackCatalog.GetPackSummaries(activeToolPacks)
                    .Where(pack => !string.Equals(pack.packId, ToolPackCatalog.FullPackId, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(pack => GetMenuPackOrder(pack.packId))
                    .ThenBy(pack => pack.packId, StringComparer.Ordinal)
                    .Select(pack =>
                    {
                        var packTools = allTools
                            .Where(tool => (tool.packs ?? Array.Empty<string>()).Contains(pack.packId, StringComparer.OrdinalIgnoreCase))
                            .OrderBy(tool => tool.name, StringComparer.Ordinal)
                            .ToArray();
                        var returnedTools = packTools
                            .Take(maxToolsPerPack)
                            .Select(tool => new
                            {
                                tool.name,
                                tool.title,
                                readOnlyHint = tool.readOnlyHint,
                                mutationHint = tool.readOnlyHint ? "read_only" : "mutating"
                            })
                            .ToArray();

                        return new
                        {
                            pack.packId,
                            pack.title,
                            pack.description,
                            pack.alwaysOn,
                            pack.adminOnly,
                            isActive = fullSurface || pack.isActive,
                            toolCount = packTools.Length,
                            readOnlyToolCount = packTools.Count(tool => tool.readOnlyHint),
                            mutatingToolCount = packTools.Count(tool => !tool.readOnlyHint),
                            truncated = packTools.Length > returnedTools.Length,
                            tools = returnedTools
                        };
                    })
                    .ToArray();

                BridgeLensSessionRegistry.UpdateAcknowledgedManifest(connectionId, s_BridgeSessionId, s_ManifestVersion);
                return new
                {
                    toolSurfaceMode = fullSurface ? "static_all" : "dynamic_packs",
                    bridgeSessionId = s_BridgeSessionId,
                    manifestVersion = s_ManifestVersion,
                    profileCatalogVersion = ToolPackCatalog.ProfileCatalogVersion,
                    activeToolPacks,
                    totalToolCount = allTools.Length,
                    maxToolsPerPack,
                    packs,
                    clientSurfaceFallback = BuildClientSurfaceFallback(),
                    workflowRecommendations = BuildToolMenuRecommendations(fullSurface, activeToolPacks)
                };
            }
        }

        static BridgeManifestResult CreateFullResult(
            BridgeToolDescriptor[] tools,
            string[] activeToolPacks,
            (string minimal, string full) hashes,
            string reason)
        {
            return new BridgeManifestResult
            {
                bridgeSessionId = s_BridgeSessionId,
                manifestVersion = s_ManifestVersion,
                profileCatalogVersion = ToolPackCatalog.ProfileCatalogVersion,
                activeToolPacks = activeToolPacks,
                kind = "full",
                reason = reason,
                hashMinimal = hashes.minimal,
                hashFull = hashes.full,
                tools = tools
            };
        }

        static void EnsureCurrentSnapshotLocked()
        {
            if (s_ManifestVersion <= 0 || s_CurrentTools == null || s_CurrentTools.Length == 0)
                RebuildSnapshotLocked(s_LastReason);
        }

        static void RebuildSnapshotLocked(string reason)
        {
            var builtTools = BuildAllTools();
            s_CurrentTools = builtTools;
            s_ManifestVersion++;
            s_LastToolsChangedUtc = DateTime.UtcNow.ToString("O");
            s_LastReason = string.IsNullOrWhiteSpace(reason) ? "tool_registry_changed" : reason;
            s_CurrentHashMinimal = PayloadBudgeting.ComputeSha256(JsonConvert.SerializeObject(FilterToolsForPacks(builtTools, new[] { ToolPackCatalog.FullPackId }, includeSchemas: false), Formatting.None));
            s_CurrentHashFull = PayloadBudgeting.ComputeSha256(JsonConvert.SerializeObject(FilterToolsForPacks(builtTools, new[] { ToolPackCatalog.FullPackId }, includeSchemas: true), Formatting.None));

            s_History.AddLast(new ManifestHistoryEntry
            {
                Version = s_ManifestVersion,
                Reason = s_LastReason,
                Tools = CloneTools(builtTools, includeSchemas: true)
            });

            while (s_History.Count > MaxHistoryEntries)
                s_History.RemoveFirst();
        }

        static BridgeToolDescriptor[] BuildAllTools()
        {
            var enabledTools = McpToolRegistry.GetAvailableTools();
            var allTools = McpToolRegistry.GetAvailableTools(ignoreEnabledState: true)
                .ToDictionary(tool => tool.name, tool => tool, StringComparer.OrdinalIgnoreCase);

            var mergedTools = new Dictionary<string, McpToolInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var tool in enabledTools)
                mergedTools[tool.name] = tool;

            foreach (var alwaysVisibleTool in ToolPackCatalog.AlwaysVisibleToolNames)
            {
                if (!mergedTools.ContainsKey(alwaysVisibleTool) && allTools.TryGetValue(alwaysVisibleTool, out var hiddenTool))
                    mergedTools[hiddenTool.name] = hiddenTool;
            }

            return mergedTools.Values
                .OrderBy(tool => tool.name, StringComparer.Ordinal)
                .Select(BuildDescriptor)
                .ToArray();
        }

        static BridgeToolDescriptor BuildDescriptor(McpToolInfo tool)
        {
            var handler = McpToolRegistry.GetTool(tool.name);
            var groups = handler?.Attribute?.Groups ?? Array.Empty<string>();
            var packs = ToolPackCatalog.GetMatchingPackIds(tool.name, groups);
            var annotations = ToolMetadataPolicy.BuildAnnotations(tool.name, tool.annotations);
            var schemaHash = PayloadBudgeting.ComputeSha256(JsonConvert.SerializeObject(new
            {
                tool.inputSchema,
                tool.outputSchema,
                annotations
            }, Formatting.None));

            return new BridgeToolDescriptor
            {
                name = tool.name,
                title = string.IsNullOrWhiteSpace(tool.title) ? tool.description : tool.title,
                description = tool.description,
                schemaHash = schemaHash,
                groups = groups.OrderBy(group => group, StringComparer.OrdinalIgnoreCase).ToArray(),
                packs = packs,
                readOnlyHint = ToolMetadataPolicy.IsReadOnlyHint(tool.name),
                inputSchema = tool.inputSchema,
                outputSchema = tool.outputSchema,
                annotations = annotations
            };
        }

        static bool IsToolAllowedForPacks(string toolName, IEnumerable<string> activeToolPacks)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                return false;

            var handler = McpToolRegistry.GetTool(toolName);
            var groups = handler?.Attribute?.Groups ?? Array.Empty<string>();
            return ToolPackCatalog.ShouldIncludeTool(toolName, groups, activeToolPacks);
        }

        static BridgeManifestDelta BuildDelta(BridgeToolDescriptor[] previousTools, BridgeToolDescriptor[] currentTools)
        {
            var previousByName = previousTools.ToDictionary(tool => tool.name, tool => tool, StringComparer.OrdinalIgnoreCase);
            var currentByName = currentTools.ToDictionary(tool => tool.name, tool => tool, StringComparer.OrdinalIgnoreCase);

            var added = new List<BridgeToolDescriptor>();
            var updated = new List<BridgeToolDescriptor>();
            var removed = new List<string>();

            foreach (var (toolName, currentTool) in currentByName)
            {
                if (!previousByName.TryGetValue(toolName, out var previousTool))
                {
                    added.Add(currentTool);
                    continue;
                }

                if (!string.Equals(ComputeDescriptorHash(previousTool), ComputeDescriptorHash(currentTool), StringComparison.Ordinal))
                    updated.Add(currentTool);
            }

            foreach (var (toolName, _) in previousByName)
            {
                if (!currentByName.ContainsKey(toolName))
                    removed.Add(toolName);
            }

            return new BridgeManifestDelta
            {
                added = added.OrderBy(tool => tool.name, StringComparer.Ordinal).ToArray(),
                updated = updated.OrderBy(tool => tool.name, StringComparer.Ordinal).ToArray(),
                removed = removed.OrderBy(tool => tool, StringComparer.Ordinal).ToArray()
            };
        }

        static BridgeToolDescriptor[] FilterToolsForPacks(BridgeToolDescriptor[] tools, IEnumerable<string> activePacks, bool includeSchemas)
        {
            var activePackSet = new HashSet<string>(ToolPackCatalog.NormalizeRequestedPacks(activePacks), StringComparer.OrdinalIgnoreCase);
            return tools
                .Where(tool => ToolPackCatalog.ShouldIncludeTool(tool.name, tool.groups, activePackSet))
                .OrderBy(tool => tool.name, StringComparer.Ordinal)
                .Select(tool => CloneDescriptor(tool, includeSchemas))
                .ToArray();
        }

        static (string minimal, string full) ComputeHashes(BridgeToolDescriptor[] tools)
        {
            var minimalHash = PayloadBudgeting.ComputeSha256(JsonConvert.SerializeObject(CloneTools(tools, includeSchemas: false), Formatting.None));
            var fullHash = PayloadBudgeting.ComputeSha256(JsonConvert.SerializeObject(CloneTools(tools, includeSchemas: true), Formatting.None));
            return (minimalHash, fullHash);
        }

        static string ComputeDescriptorHash(BridgeToolDescriptor tool)
        {
            return PayloadBudgeting.ComputeSha256(JsonConvert.SerializeObject(tool, Formatting.None));
        }

        static BridgeToolDescriptor CloneDescriptor(BridgeToolDescriptor tool, bool includeSchemas)
        {
            return new BridgeToolDescriptor
            {
                name = tool.name,
                title = tool.title,
                description = tool.description,
                schemaHash = tool.schemaHash,
                groups = tool.groups?.ToArray() ?? Array.Empty<string>(),
                packs = tool.packs?.ToArray() ?? Array.Empty<string>(),
                readOnlyHint = tool.readOnlyHint,
                inputSchema = includeSchemas ? tool.inputSchema : null,
                outputSchema = includeSchemas ? tool.outputSchema : null,
                annotations = includeSchemas ? tool.annotations : null
            };
        }

        static object BuildToolDescription(BridgeToolDescriptor tool, bool includePackRequirements, bool includeExamples)
        {
            var requiredPacks = GetRequiredActivationPacks(tool);
            return new
            {
                tool.name,
                tool.title,
                tool.description,
                tool.schemaHash,
                groups = tool.groups ?? Array.Empty<string>(),
                packs = tool.packs ?? Array.Empty<string>(),
                requiredPacks = includePackRequirements ? requiredPacks : null,
                tool.readOnlyHint,
                tool.inputSchema,
                tool.outputSchema,
                tool.annotations,
                example = includeExamples
                    ? new
                    {
                        setToolPacks = requiredPacks,
                        call = new
                        {
                            tool = tool.name,
                            arguments = new { }
                        },
                        facadeFallback = new
                        {
                            list = new
                            {
                                tool = "Unity.Tools.List",
                                arguments = new { groupBy = "pack" }
                            },
                            invoke = new
                            {
                                tool = "Unity.Tools.Invoke",
                                arguments = new
                                {
                                    toolName = tool.name,
                                    arguments = new { }
                                }
                            },
                            batchInvoke = new
                            {
                                tool = "Unity.Tools.BatchInvoke",
                                arguments = new
                                {
                                    calls = new[]
                                    {
                                        new
                                        {
                                            toolName = tool.name,
                                            arguments = new { }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    : null
            };
        }

        static object BuildClientSurfaceFallback()
        {
            return new
            {
                listTool = "Unity.Tools.List",
                invokeTool = "Unity.Tools.Invoke",
                batchInvokeTool = "Unity.Tools.BatchInvoke",
                note = "If a direct native tool is not callable in the MCP client, use Unity.Tools.List to confirm the host-visible name, then Unity.Tools.Invoke or Unity.Tools.BatchInvoke to call it through the stable facade."
            };
        }

        static string[] GetRequiredActivationPacks(BridgeToolDescriptor tool)
        {
            return (tool.packs ?? Array.Empty<string>())
                .Where(pack =>
                    !string.Equals(pack, ToolPackCatalog.FoundationPackId, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(pack, ToolPackCatalog.FullPackId, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(pack => pack, StringComparer.Ordinal)
                .ToArray();
        }

        static int GetMenuPackOrder(string packId)
        {
            if (string.Equals(packId, ToolPackCatalog.FoundationPackId, StringComparison.OrdinalIgnoreCase))
                return 0;
            if (string.Equals(packId, ToolPackCatalog.ConsolePackId, StringComparison.OrdinalIgnoreCase))
                return 10;
            if (string.Equals(packId, ToolPackCatalog.ProjectPackId, StringComparison.OrdinalIgnoreCase))
                return 20;
            if (string.Equals(packId, ToolPackCatalog.ScriptingPackId, StringComparison.OrdinalIgnoreCase))
                return 30;
            if (string.Equals(packId, ToolPackCatalog.ScenePackId, StringComparison.OrdinalIgnoreCase))
                return 40;
            if (string.Equals(packId, ToolPackCatalog.UiPackId, StringComparison.OrdinalIgnoreCase))
                return 50;
            if (string.Equals(packId, ToolPackCatalog.RuntimePackId, StringComparison.OrdinalIgnoreCase))
                return 60;
            if (string.Equals(packId, ToolPackCatalog.AssetsPackId, StringComparison.OrdinalIgnoreCase))
                return 70;
            if (string.Equals(packId, ToolPackCatalog.DebugPackId, StringComparison.OrdinalIgnoreCase))
                return 80;

            return 100;
        }

        static string[] BuildToolMenuRecommendations(bool fullSurface, string[] activeToolPacks)
        {
            if (fullSurface)
            {
                return new[]
                {
                    "Call real native tools directly; no Unity.SetToolPacks step is required in static_all mode.",
                    "If a direct native tool is unavailable in the MCP client, use Unity.Tools.List and call it through Unity.Tools.Invoke or Unity.Tools.BatchInvoke.",
                    "Use Unity.Tools.Describe when a named tool needs exact schema or pack metadata.",
                    "Prefer read-only, preview, or verify tools before mutating apply tools."
                };
            }

            var nextPacks = ToolPackCatalog.GetRecommendedNextPacks(activeToolPacks);
            var activationHint = nextPacks.Length > 0
                ? $"Use Unity.SetToolPacks to activate one or two needed packs. Recommended next packs: {string.Join(", ", nextPacks)}."
                : "Use Unity.SetToolPacks to activate one or two needed packs before calling pack-gated tools.";

            return new[]
            {
                activationHint,
                "If a direct native tool is unavailable in the MCP client, use Unity.Tools.List and call it through Unity.Tools.Invoke or Unity.Tools.BatchInvoke.",
                "Use Unity.Tools.Describe when a named tool needs exact schema or pack metadata.",
                "Prefer read-only, preview, or verify tools before mutating apply tools."
            };
        }

        static BridgeToolDescriptor[] CloneTools(BridgeToolDescriptor[] tools, bool includeSchemas)
        {
            return tools?.Select(tool => CloneDescriptor(tool, includeSchemas)).ToArray() ?? Array.Empty<BridgeToolDescriptor>();
        }
    }
}
