using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Unity.AI.Assistant.Utils;
using Unity.AI.MCP.Editor.ToolRegistry;

namespace Unity.AI.MCP.Editor.Lens
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
            if (!BridgeLensSessionRegistry.TrySetActiveToolPacks(connectionId, requestedPacks, out var normalizedPacks, out error))
                return null;

            lock (s_Lock)
            {
                EnsureCurrentSnapshotLocked();
                var filteredCurrent = FilterToolsForPacks(s_CurrentTools, normalizedPacks, includeSchemas);
                var currentHashes = ComputeHashes(filteredCurrent);
                BridgeLensSessionRegistry.UpdateAcknowledgedManifest(connectionId, s_BridgeSessionId, s_ManifestVersion);
                return CreateFullResult(filteredCurrent, normalizedPacks, currentHashes, "tool_packs_updated");
            }
        }

        public static BridgeToolSchemasResult GetToolSchemas(string connectionId, IEnumerable<string> toolNames)
        {
            lock (s_Lock)
            {
                EnsureCurrentSnapshotLocked();

                var requestedNames = new HashSet<string>(toolNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                var activeToolPacks = BridgeLensSessionRegistry.GetActiveToolPacks(connectionId);
                var filteredTools = FilterToolsForPacks(s_CurrentTools, activeToolPacks, includeSchemas: true)
                    .Where(tool => requestedNames.Contains(tool.name))
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
            var schemaHash = PayloadBudgeting.ComputeSha256(JsonConvert.SerializeObject(new
            {
                tool.inputSchema,
                tool.outputSchema,
                tool.annotations
            }, Formatting.None));

            return new BridgeToolDescriptor
            {
                name = tool.name,
                title = string.IsNullOrWhiteSpace(tool.title) ? tool.description : tool.title,
                description = tool.description,
                schemaHash = schemaHash,
                groups = groups.OrderBy(group => group, StringComparer.OrdinalIgnoreCase).ToArray(),
                packs = packs,
                readOnlyHint = ToolPackCatalog.IsReadOnlyHint(tool.name),
                inputSchema = tool.inputSchema,
                outputSchema = tool.outputSchema,
                annotations = tool.annotations
            };
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

        static BridgeToolDescriptor[] CloneTools(BridgeToolDescriptor[] tools, bool includeSchemas)
        {
            return tools?.Select(tool => CloneDescriptor(tool, includeSchemas)).ToArray() ?? Array.Empty<BridgeToolDescriptor>();
        }
    }
}
