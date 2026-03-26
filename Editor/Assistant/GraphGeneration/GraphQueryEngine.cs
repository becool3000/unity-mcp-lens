using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Editor.GraphGeneration
{
    /// <summary>
    /// Pure read-only query engine for the AI.CoreGraph dependency graph.
    /// Thread-safe (no Unity API calls). Caches node and edge data per graph root to avoid repeated disk I/O.
    /// </summary>
    static class GraphQueryEngine
    {
        static readonly object s_CacheLock = new object();
        static Dictionary<string, GraphCache> s_Cache = new Dictionary<string, GraphCache>();

        sealed class GraphCache
        {
            public readonly Dictionary<string, List<GraphNode>> NodeFiles = new Dictionary<string, List<GraphNode>>(StringComparer.Ordinal);
            public readonly Dictionary<string, List<GraphEdge>> EdgeFiles = new Dictionary<string, List<GraphEdge>>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Call after the graph is updated (e.g. incremental refresh) so the next query uses fresh data.
        /// </summary>
        public static void InvalidateCache(string graphRoot)
        {
            if (string.IsNullOrEmpty(graphRoot)) return;
            lock (s_CacheLock)
            {
                s_Cache.Remove(graphRoot);
            }
        }

        static GraphCache GetOrCreateCache(string graphRoot)
        {
            lock (s_CacheLock)
            {
                if (s_Cache.TryGetValue(graphRoot, out var c))
                    return c;

                var cache = new GraphCache();
                foreach (var (_, dir, file) in k_NodePrefixMap)
                {
                    var key = Path.Combine(dir, file);
                    var path = Path.Combine(graphRoot, key);
                    cache.NodeFiles[key] = LoadNodesUncached(path);
                }
                foreach (var (dir, file) in k_EdgeFiles)
                {
                    var key = Path.Combine(dir, file);
                    var path = Path.Combine(graphRoot, key);
                    cache.EdgeFiles[key] = LoadEdgesUncached(path);
                }
                s_Cache[graphRoot] = cache;
                return cache;
            }
        }

        // Node prefix → (directory, filename)
        static readonly (string prefix, string dir, string file)[] k_NodePrefixMap =
        {
            ("project_", GraphGenerationConstants.NodesProjectDir, GraphGenerationConstants.ProjectFile),
            ("scene_", GraphGenerationConstants.NodesSceneDir, GraphGenerationConstants.ScenesFile),
            ("asset_", GraphGenerationConstants.NodesAssetDir, GraphGenerationConstants.AssetsFile),
            ("tool_", GraphGenerationConstants.NodesToolDir, GraphGenerationConstants.ToolsFile),
            ("assetType_", GraphGenerationConstants.NodesAssetTypeDir, GraphGenerationConstants.AssetTypesFile),
            ("toolCategory_", GraphGenerationConstants.NodesToolCategoryDir, GraphGenerationConstants.ToolCategoriesFile),
        };

        // Edge directory → filename (matches GraphRestructurer output)
        static readonly (string dir, string file)[] k_EdgeFiles =
        {
            (GraphGenerationConstants.EdgesSceneDependsOnAssetDir, GraphGenerationConstants.DependenciesFileName),
            (GraphGenerationConstants.EdgesAssetDependsOnAssetDir, GraphGenerationConstants.DependenciesFileName),
            (GraphGenerationConstants.EdgesAssetReferencedBySceneDir, GraphGenerationConstants.ReferencesFileName),
            (GraphGenerationConstants.EdgesAssetTypeIncludeAssetDir, GraphGenerationConstants.TypeMembershipFileName),
            (GraphGenerationConstants.EdgesToolCategoryIncludeToolDir, GraphGenerationConstants.CategoryMembershipFileName),
            (GraphGenerationConstants.EdgesProjectCanUseToolCategoryDir, GraphGenerationConstants.CanUseFileName),
            (GraphGenerationConstants.EdgesProjectHasSceneDir, GraphGenerationConstants.HasFileName),
            (GraphGenerationConstants.EdgesProjectContainsAssetTypeDir, GraphGenerationConstants.ContainsFileName),
            (GraphGenerationConstants.EdgesAssetInheritsFromAssetDir, GraphGenerationConstants.InheritanceFileName),
            (GraphGenerationConstants.EdgesAssetImplementsAssetDir, GraphGenerationConstants.InterfaceImplementationFileName),
            (GraphGenerationConstants.EdgesAssetDeclaresAssetDir, GraphGenerationConstants.FieldDeclarationsFileName),
            (GraphGenerationConstants.EdgesAssetUsesAssetDir, GraphGenerationConstants.TypeUsageFileName),
        };

        /// <summary>
        /// Get a specific node by ID.
        /// </summary>
        public static string GetNode(string graphRoot, string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
                return JsonConvert.SerializeObject(new { error = "node_id is required for get_node query" }, Formatting.Indented);

            var cache = GetOrCreateCache(graphRoot);

            string targetKey = null;
            foreach (var (prefix, dir, file) in k_NodePrefixMap)
            {
                if (nodeId.StartsWith(prefix, StringComparison.Ordinal))
                {
                    targetKey = Path.Combine(dir, file);
                    break;
                }
            }

            if (targetKey != null && cache.NodeFiles.TryGetValue(targetKey, out var list))
            {
                var node = FindNodeInList(list, nodeId);
                if (node != null)
                    return JsonConvert.SerializeObject(new { node }, Formatting.Indented);
            }
            else
            {
                foreach (var kvp in cache.NodeFiles)
                {
                    var node = FindNodeInList(kvp.Value, nodeId);
                    if (node != null)
                        return JsonConvert.SerializeObject(new { node }, Formatting.Indented);
                }
            }

            return JsonConvert.SerializeObject(new { error = $"Node not found: {nodeId}" }, Formatting.Indented);
        }

        /// <summary>
        /// Get edges connected to a node.
        /// </summary>
        public static string GetEdges(string graphRoot, string nodeId, string direction, int maxResults)
        {
            if (string.IsNullOrEmpty(nodeId))
                return JsonConvert.SerializeObject(new { error = "node_id is required for get_edges query" });

            if (string.IsNullOrEmpty(direction))
                direction = "both";

            var resultEdges = new List<GraphEdgeWithDirection>();
            var cache = GetOrCreateCache(graphRoot);

            foreach (var (dir, file) in k_EdgeFiles)
            {
                var key = Path.Combine(dir, file);
                if (!cache.EdgeFiles.TryGetValue(key, out var edgeList))
                    continue;

                foreach (var edge in edgeList)
                {
                    if ((direction == "outgoing" || direction == "both") && edge.SrcId == nodeId)
                    {
                        resultEdges.Add(new GraphEdgeWithDirection
                        {
                            SrcId = edge.SrcId,
                            DstId = edge.DstId,
                            RelationType = edge.RelationType,
                            SrcType = edge.SrcType,
                            DstType = edge.DstType,
                            Direction = "outgoing"
                        });
                    }
                    else if ((direction == "incoming" || direction == "both") && edge.DstId == nodeId)
                    {
                        resultEdges.Add(new GraphEdgeWithDirection
                        {
                            SrcId = edge.SrcId,
                            DstId = edge.DstId,
                            RelationType = edge.RelationType,
                            SrcType = edge.SrcType,
                            DstType = edge.DstType,
                            Direction = "incoming"
                        });
                    }

                    if (resultEdges.Count >= maxResults)
                        break;
                }

                if (resultEdges.Count >= maxResults)
                    break;
            }

            if (resultEdges.Count > maxResults)
                resultEdges.RemoveRange(maxResults, resultEdges.Count - maxResults);

            return JsonConvert.SerializeObject(new { count = resultEdges.Count, edges = resultEdges }, Formatting.Indented);
        }

        /// <summary>
        /// Get graph metadata and staleness information.
        /// </summary>
        public static string GetMetadata(string graphRoot)
        {
            var metadataPath = Path.Combine(graphRoot, "metadata.json");
            if (!File.Exists(metadataPath))
            {
                return JsonConvert.SerializeObject(new
                {
                    error = "Graph metadata not found",
                    graph_root = graphRoot,
                    hint = "The graph has not been generated yet."
                });
            }

            GraphMetadata metadata;
            try
            {
                var json = File.ReadAllText(metadataPath);
                metadata = JsonConvert.DeserializeObject<GraphMetadata>(json);
                if (metadata == null)
                    return JsonConvert.SerializeObject(new { error = "Graph metadata was empty." });
            }
            catch (Exception e)
            {
                return JsonConvert.SerializeObject(new { error = $"Failed to load metadata: {e.Message}" });
            }

            // Build staleness info
            var pendingChangesPath = Path.Combine(graphRoot, GraphGenerationConstants.PendingChangesFile);
            var lastRefreshPath = Path.Combine(graphRoot, GraphGenerationConstants.LastRefreshTimestampFile);

            int pendingCount = 0;
            string lastRefresh = null;
            double oldestChangeAge = 0;

            if (File.Exists(pendingChangesPath))
            {
                try
                {
                    var changesJson = File.ReadAllText(pendingChangesPath);
                    var pending = JsonConvert.DeserializeObject<PendingChangesFile>(changesJson);
                    var changes = pending?.changes ?? new List<AssetChangeEvent>();
                    pendingCount = changes.Count;

                    if (pendingCount > 0)
                    {
                        var oldest = changes
                            .Select(c => c.timestamp)
                            .Where(t => !string.IsNullOrEmpty(t))
                            .OrderBy(t => t)
                            .FirstOrDefault();

                        if (!string.IsNullOrEmpty(oldest) && DateTimeOffset.TryParse(oldest, out var oldestDt))
                        {
                            oldestChangeAge = (DateTimeOffset.UtcNow - oldestDt).TotalSeconds;
                        }
                    }
                }
                catch
                {
                    // Ignore parse errors for staleness
                }
            }

            if (File.Exists(lastRefreshPath))
            {
                try
                {
                    lastRefresh = File.ReadAllText(lastRefreshPath).Trim();
                }
                catch
                {
                    // Ignore
                }
            }

            bool isStale = pendingCount > 0;
            var staleness = new
            {
                is_stale = isStale,
                pending_changes_count = pendingCount,
                last_refresh_timestamp = lastRefresh,
                oldest_change_age_seconds = oldestChangeAge,
                recommendation = isStale
                    ? "Graph has pending changes. They will be processed automatically on next query."
                    : "Graph is up to date."
            };

            return JsonConvert.SerializeObject(new { metadata, staleness }, Formatting.Indented);
        }

        /// <summary>
        /// Resolve a Unity file path to a node ID by trying common prefixes.
        /// </summary>
        public static string ResolveFilePathToNodeId(string graphRoot, string filePath)
        {
            // Normalize path
            var cleanPath = filePath.Replace("\\", "/").Trim('/');
            while (cleanPath.Contains("//"))
                cleanPath = cleanPath.Replace("//", "/");

            var normalized = GraphRestructurer.NormalizeId(cleanPath);

            // Try prefixes in order of likelihood
            foreach (var prefix in GraphGenerationConstants.NodeIdPrefixesForPathResolution)
            {
                var candidateId = prefix + normalized;
                var result = GetNode(graphRoot, candidateId);
                if (result.Contains("\"node\""))
                    return candidateId;
            }

            // Fallback (backward compatibility)
            return GraphGenerationConstants.DefaultNodeIdPrefix + normalized;
        }

        static GraphNode FindNodeInList(List<GraphNode> nodes, string nodeId)
        {
            foreach (var node in nodes)
            {
                if (node.Id == nodeId)
                    return node;
            }
            return null;
        }

        static List<GraphNode> LoadNodesUncached(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return new List<GraphNode>();

                var json = File.ReadAllText(filePath);
                var list = JsonConvert.DeserializeObject<List<GraphNode>>(json);
                return list ?? new List<GraphNode>();
            }
            catch
            {
                return new List<GraphNode>();
            }
        }

        static List<GraphEdge> LoadEdgesUncached(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return new List<GraphEdge>();

                var json = File.ReadAllText(filePath);
                var list = JsonConvert.DeserializeObject<List<GraphEdge>>(json);
                return list ?? new List<GraphEdge>();
            }
            catch
            {
                return new List<GraphEdge>();
            }
        }
    }
}
