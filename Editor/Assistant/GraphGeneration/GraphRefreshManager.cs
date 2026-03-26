using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.AI.Assistant.Utils;
using Unity.AI.Toolkit;
using UnityEditor;

namespace Unity.AI.Assistant.Editor.GraphGeneration
{
    /// <summary>
    /// Incremental graph refresh manager. Processes .pending_changes.json written by
    /// GraphRefreshPostprocessor and updates the AI.CoreGraph files accordingly.
    /// </summary>
    class GraphRefreshManager
    {
        /// <summary>
        /// Shared lock for .pending_changes.json. Used by GraphRefreshPostprocessor when appending
        /// and by GraphRefreshManager when claiming (move) or deleting the inprogress file.
        /// </summary>
        internal static readonly object PendingChangesFileLock = new object();
        const double k_MinRefreshIntervalSeconds = 5.0;
        const int k_MaxOrphanedEdges = 100;

        static readonly SemaphoreSlim s_RefreshSemaphore = new SemaphoreSlim(1, 1);
        static double s_LastRefreshTime;
        static int s_RefreshCount;

        const int k_ValidateEveryNRefreshes = 10;

        readonly string _graphRoot;

        public string GraphRoot => _graphRoot;

        public GraphRefreshManager(string graphRoot)
        {
            _graphRoot = graphRoot;
        }

        public bool HasPendingChanges()
        {
            var path = Path.Combine(_graphRoot, GraphGenerationConstants.PendingChangesFile);
            if (!File.Exists(path))
                return false;

            try
            {
                var json = File.ReadAllText(path);
                var data = JsonConvert.DeserializeObject<PendingChangesFile>(json);
                return data?.changes != null && data.changes.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Result of a refresh operation.
        /// </summary>
        public struct RefreshResult
        {
            public string Status; // "no_changes", "debounced", "success", "error"
            public bool NeedsFullRegeneration;
            public string Error;
        }

        /// <summary>
        /// Filters AssetDatabase dependency list: exclude null/empty, exclude the given path, keep only paths under Assets/.
        /// Returns an empty list if deps is null.
        /// </summary>
        static List<string> FilterAssetDependencies(IEnumerable<string> deps, string excludePath)
        {
            if (deps == null)
                return new List<string>();
            return deps
                .Where(d => !string.IsNullOrEmpty(d) && d != excludePath && d.StartsWith("Assets/"))
                .ToList();
        }

        public async Task<RefreshResult> ProcessPendingChangesAsync()
        {
            if (!HasPendingChanges())
                return new RefreshResult { Status = "no_changes" };

            if (!await s_RefreshSemaphore.WaitAsync(TimeSpan.Zero))
                return new RefreshResult { Status = "debounced (refresh in progress)" };

            string inprogressPath = null;
            try
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                if (now - s_LastRefreshTime < k_MinRefreshIntervalSeconds)
                {
                    return new RefreshResult { Status = "debounced (rate-limited)" };
                }

                s_LastRefreshTime = now;

                // 1. Claim pending changes by moving to inprogress (under shared lock to avoid losing appends)
                lock (PendingChangesFileLock)
                {
                    var pendingPath = Path.Combine(_graphRoot, GraphGenerationConstants.PendingChangesFile);
                    if (File.Exists(pendingPath))
                    {
                        inprogressPath = Path.Combine(_graphRoot, GraphGenerationConstants.PendingChangesInProgressFile);
                        File.Move(pendingPath, inprogressPath);
                    }
                }

                if (inprogressPath == null || !File.Exists(inprogressPath))
                    return new RefreshResult { Status = "no_changes" };

                var changesJson = File.ReadAllText(inprogressPath);
                var pending = JsonConvert.DeserializeObject<PendingChangesFile>(changesJson);
                var changes = pending?.changes ?? new List<AssetChangeEvent>();

                InternalLog.Log($"[GraphRefreshManager] Processing {changes.Count} pending changes");

                // Group changes by type
                var deleted = new List<AssetChangeEvent>();
                var moved = new List<AssetChangeEvent>();
                var imported = new List<AssetChangeEvent>();

                foreach (var change in changes)
                {
                    switch (change.type)
                    {
                        case AssetChangeType.Deleted: deleted.Add(change); break;
                        case AssetChangeType.Moved: moved.Add(change); break;
                        case AssetChangeType.Imported: imported.Add(change); break;
                        // DomainReload is intentionally skipped
                        default: break;
                    }
                }

                // 2. Collect AssetDatabase data on main thread
                await EditorTask.Yield();

                var assetInfoMap = new Dictionary<string, AssetInfo>();
                foreach (var change in imported)
                {
                    var assetPath = change.path;
                    if (string.IsNullOrEmpty(assetPath)) continue;

                    var deps = AssetDatabase.GetDependencies(assetPath, false);
                    var mainType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
                    var typeName = mainType != null ? mainType.Name : AssetTypeUtils.GetAssetTypeFromPath(assetPath);

                    assetInfoMap[assetPath] = new AssetInfo
                    {
                        Path = assetPath,
                        Name = System.IO.Path.GetFileName(assetPath),
                        AssetType = typeName,
                        Dependencies = FilterAssetDependencies(deps, assetPath)
                    };
                }

                // For moved assets that may need import fallback
                foreach (var change in moved)
                {
                    var newPath = change.path;
                    if (string.IsNullOrEmpty(newPath) || assetInfoMap.ContainsKey(newPath)) continue;

                    var deps = AssetDatabase.GetDependencies(newPath, false);
                    var mainType = AssetDatabase.GetMainAssetTypeAtPath(newPath);
                    var typeName = mainType != null ? mainType.Name : AssetTypeUtils.GetAssetTypeFromPath(newPath);

                    assetInfoMap[newPath] = new AssetInfo
                    {
                        Path = newPath,
                        Name = System.IO.Path.GetFileName(newPath),
                        AssetType = typeName,
                        Dependencies = FilterAssetDependencies(deps, newPath)
                    };
                }

                // 3. Run Roslyn analysis for .cs files on background thread
                var csFiles = imported
                    .Select(c => c.path)
                    .Where(p => !string.IsNullOrEmpty(p) && p.EndsWith(".cs"))
                    .ToList();

                List<CodeDependencyInfo> codeDeps = null;
                if (csFiles.Count > 0)
                {
                    var projectRoot = Path.GetDirectoryName(UnityEngine.Application.dataPath);
                    var absolutePaths = csFiles.Select(p => Path.Combine(projectRoot, p)).ToList();
                    codeDeps = await Task.Run(() =>
                    {
                        var analyzer = new CodeDependencyAnalyzer();
                        return analyzer.AnalyzeCodeDependencies(absolutePaths);
                    });
                }

                // 4. Run all file I/O on background thread
                var graphRoot = _graphRoot;
                await Task.Run(() =>
                {
                    ProcessChangesCore(graphRoot, deleted, moved, imported, assetInfoMap, codeDeps, csFiles);
                });

                GraphQueryEngine.InvalidateCache(graphRoot);
                s_RefreshCount++;

                // 5. Validate only every Nth refresh to avoid slowing every tool call
                var validation = (s_RefreshCount % k_ValidateEveryNRefreshes == 0)
                    ? ValidateGraphConsistency()
                    : new ValidationResult { IsValid = true };
                if (!validation.IsValid)
                {
                    InternalLog.LogError("[GraphRefreshManager] Graph validation failed after refresh");
                    return new RefreshResult
                    {
                        Status = "error",
                        NeedsFullRegeneration = true,
                        Error = "Graph validation failed after incremental update"
                    };
                }

                InternalLog.Log("[GraphRefreshManager] Incremental refresh completed successfully");
                return new RefreshResult { Status = "success" };
            }
            catch (Exception e)
            {
                InternalLog.LogError($"[GraphRefreshManager] Failed to process pending changes: {e.Message}\n{e.StackTrace}");
                return new RefreshResult
                {
                    Status = "error",
                    NeedsFullRegeneration = true,
                    Error = e.Message
                };
            }
            finally
            {
                s_RefreshSemaphore.Release();
                // Clean up inprogress file if we claimed it (avoid leaving stale file on success, validation failure, or exception)
                if (inprogressPath != null)
                {
                    try
                    {
                        lock (PendingChangesFileLock)
                        {
                            if (File.Exists(inprogressPath))
                                File.Delete(inprogressPath);
                        }
                    }
                    catch
                    {
                        // Best-effort cleanup
                    }
                }
            }
        }

        struct AssetInfo
        {
            public string Path;
            public string Name;
            public string AssetType;
            public List<string> Dependencies;
        }

        /// <summary>
        /// In-memory snapshot of graph node/edge data. Loaded once at start of refresh, mutated by handlers, saved once at end.
        /// </summary>
        sealed class GraphInMemoryState
        {
            public List<GraphNode> Assets = new List<GraphNode>();
            public List<GraphNode> Scenes = new List<GraphNode>();
            public List<GraphAssetTypeNode> AssetTypes = new List<GraphAssetTypeNode>();
            public List<GraphProjectNode> Projects = new List<GraphProjectNode>();
            public GraphMetadata Metadata;
            /// <summary>Key: relative path from graph root (e.g. "edges-asset_dependsOn_asset/dependencies.json").</summary>
            public Dictionary<string, List<GraphEdge>> EdgesByRelativePath = new Dictionary<string, List<GraphEdge>>(StringComparer.Ordinal);
        }

        static string EdgeRelativePath(string graphRoot, string fullPath)
        {
            var root = Path.GetFullPath(graphRoot).TrimEnd(Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(fullPath);
            if (root.Length >= full.Length || !full.StartsWith(root))
                return null;
            return full.Substring(root.Length + (root.Length > 0 && full.Length > root.Length && full[root.Length] == Path.DirectorySeparatorChar ? 1 : 0)).Replace('\\', '/');
        }

        static GraphInMemoryState LoadGraphState(string graphRoot)
        {
            var state = new GraphInMemoryState();
            var assetsPath = Path.Combine(graphRoot, GraphGenerationConstants.AssetsPath);
            var scenesPath = Path.Combine(graphRoot, GraphGenerationConstants.ScenesPath);
            var assetTypesPath = Path.Combine(graphRoot, GraphGenerationConstants.AssetTypesPath);
            var projectPath = Path.Combine(graphRoot, GraphGenerationConstants.ProjectPath);
            var metadataPath = Path.Combine(graphRoot, GraphGenerationConstants.MetadataFile);

            state.Assets = LoadJsonList<GraphNode>(assetsPath);
            state.Scenes = LoadJsonList<GraphNode>(scenesPath);
            state.AssetTypes = LoadJsonList<GraphAssetTypeNode>(assetTypesPath);
            state.Projects = LoadJsonList<GraphProjectNode>(projectPath);
            if (File.Exists(metadataPath))
            {
                try
                {
                    state.Metadata = JsonConvert.DeserializeObject<GraphMetadata>(File.ReadAllText(metadataPath));
                }
                catch { /* leave null */ }
            }

            foreach (var edgeDir in Directory.GetDirectories(graphRoot, GraphGenerationConstants.EdgeDirPattern))
            {
                foreach (var edgeFile in Directory.GetFiles(edgeDir, "*.json"))
                {
                    var rel = EdgeRelativePath(graphRoot, edgeFile);
                    if (!string.IsNullOrEmpty(rel))
                        state.EdgesByRelativePath[rel] = LoadJsonList<GraphEdge>(edgeFile);
                }
            }

            return state;
        }

        static string EdgeKey(string dir, string file)
        {
            return Path.Combine(dir, file).Replace('\\', '/');
        }

        static List<GraphEdge> GetOrCreateEdgeList(GraphInMemoryState state, string key)
        {
            if (!state.EdgesByRelativePath.TryGetValue(key, out var list))
            {
                list = new List<GraphEdge>();
                state.EdgesByRelativePath[key] = list;
            }
            return list;
        }

        static void SaveGraphState(string graphRoot, GraphInMemoryState state)
        {
            var assetsPath = Path.Combine(graphRoot, GraphGenerationConstants.AssetsPath);
            var scenesPath = Path.Combine(graphRoot, GraphGenerationConstants.ScenesPath);
            var assetTypesPath = Path.Combine(graphRoot, GraphGenerationConstants.AssetTypesPath);
            var projectPath = Path.Combine(graphRoot, GraphGenerationConstants.ProjectPath);
            var metadataPath = Path.Combine(graphRoot, GraphGenerationConstants.MetadataFile);

            SaveJsonList(assetsPath, state.Assets);
            SaveJsonList(scenesPath, state.Scenes);
            SaveJsonList(assetTypesPath, state.AssetTypes);
            SaveJsonList(projectPath, state.Projects);
            if (state.Metadata != null)
                File.WriteAllText(metadataPath, JsonConvert.SerializeObject(state.Metadata, Formatting.Indented));

            foreach (var kv in state.EdgesByRelativePath)
            {
                var fullPath = Path.Combine(graphRoot, kv.Key);
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                SaveJsonList(fullPath, kv.Value);
            }
        }

        /// <summary>
        /// Core processing logic — runs entirely on a background thread. Loads graph into memory, mutates in memory, saves once at end.
        /// Batches edge file updates: collect all removals and ID replacements, then update each edge file once.
        /// </summary>
        static void ProcessChangesCore(
            string graphRoot,
            List<AssetChangeEvent> deleted,
            List<AssetChangeEvent> moved,
            List<AssetChangeEvent> imported,
            Dictionary<string, AssetInfo> assetInfoMap,
            List<CodeDependencyInfo> codeDeps,
            List<string> csFiles)
        {
            var nodeIdsToRemove = new HashSet<string>();
            foreach (var change in deleted)
            {
                var assetPath = change.path;
                if (string.IsNullOrEmpty(assetPath)) continue;
                nodeIdsToRemove.Add(PathToNodeId(assetPath));
                if (assetPath.EndsWith(".unity"))
                    nodeIdsToRemove.Add("scene_" + GraphRestructurer.NormalizeId(assetPath));
            }

            var idReplacements = new List<(string oldId, string newId)>();
            foreach (var change in moved)
            {
                var oldPath = change.oldPath;
                var newPath = change.path;
                if (string.IsNullOrEmpty(newPath)) continue;
                if (oldPath?.EndsWith(".unity") == true || newPath.EndsWith(".unity"))
                {
                    idReplacements.Add(("scene_" + GraphRestructurer.NormalizeId(oldPath ?? ""), "scene_" + GraphRestructurer.NormalizeId(newPath)));
                    idReplacements.Add((PathToNodeId(oldPath ?? ""), PathToNodeId(newPath)));
                }
                else if (!string.IsNullOrEmpty(oldPath))
                {
                    idReplacements.Add((PathToNodeId(oldPath), PathToNodeId(newPath)));
                }
            }

            var state = LoadGraphState(graphRoot);

            RemoveFromAllEdgeFilesBatch(state, nodeIdsToRemove);
            UpdateNodeIdInEdgeFilesBatch(state, idReplacements);

            foreach (var change in deleted)
            {
                var assetPath = change.path;
                if (!string.IsNullOrEmpty(assetPath))
                    HandleAssetDeletedNodesOnly(state, assetPath);
            }

            foreach (var change in moved)
            {
                var oldPath = change.oldPath;
                var newPath = change.path;
                if (!string.IsNullOrEmpty(newPath))
                    HandleAssetMoved(state, oldPath, newPath, assetInfoMap);
            }

            // Handle imports
            foreach (var change in imported)
            {
                var assetPath = change.path;
                if (string.IsNullOrEmpty(assetPath)) continue;

                if (assetInfoMap.TryGetValue(assetPath, out var info))
                {
                    if (assetPath.EndsWith(".unity"))
                        HandleSceneModified(state, assetPath, info);
                    else
                        HandleAssetModified(state, assetPath, info);
                }
            }

            // Handle code dependency edges for .cs files
            if (codeDeps != null && codeDeps.Count > 0)
                UpdateCodeDependencyEdges(state, codeDeps, csFiles);

            // Update metadata, project counts, project→assetType edges
            UpdateMetadataTimestamp(state);
            UpdateProjectCounts(state);
            UpdateProjectAssetTypeEdges(state);

            SaveGraphState(graphRoot, state);
            RecordRefreshTimestamp(graphRoot);
        }

        // ----- Deletion -----
        // Edge removal is done in batch in ProcessChangesCore. This only updates node lists.

        static void HandleAssetDeletedNodesOnly(GraphInMemoryState state, string assetPath)
        {
            var nodeId = PathToNodeId(assetPath);
            RemoveNodeFromList(state.Assets, nodeId);

            if (assetPath.EndsWith(".unity"))
            {
                var sceneNodeId = "scene_" + GraphRestructurer.NormalizeId(assetPath);
                RemoveNodeFromList(state.Scenes, sceneNodeId);
                UpdateProjectSceneEdges(state);
            }
        }

        // ----- Move -----

        static void HandleAssetMoved(GraphInMemoryState state, string oldPath, string newPath,
            Dictionary<string, AssetInfo> assetInfoMap)
        {
            if (string.IsNullOrEmpty(oldPath))
            {
                if (assetInfoMap.TryGetValue(newPath, out var info))
                {
                    if (newPath.EndsWith(".unity"))
                        HandleSceneModified(state, newPath, info);
                    else
                        HandleAssetModified(state, newPath, info);
                }
                return;
            }

            if (oldPath.EndsWith(".unity") || newPath.EndsWith(".unity"))
            {
                HandleSceneMoved(state, oldPath, newPath, assetInfoMap);
                return;
            }

            var oldNodeId = PathToNodeId(oldPath);
            bool found = false;
            foreach (var asset in state.Assets)
            {
                if (asset.Id == oldNodeId)
                {
                    asset.Id = PathToNodeId(newPath);
                    asset.Path = newPath;
                    asset.Name = System.IO.Path.GetFileName(newPath);
                    found = true;
                    break;
                }
            }

            if (!found && assetInfoMap.TryGetValue(newPath, out var movedInfo))
                HandleAssetModified(state, newPath, movedInfo);
        }

        static void HandleSceneMoved(GraphInMemoryState state, string oldPath, string newPath,
            Dictionary<string, AssetInfo> assetInfoMap)
        {
            var oldSceneNodeId = "scene_" + GraphRestructurer.NormalizeId(oldPath);
            var newSceneNodeId = "scene_" + GraphRestructurer.NormalizeId(newPath);
            var oldAssetNodeId = PathToNodeId(oldPath);
            var newAssetNodeId = PathToNodeId(newPath);

            bool foundInScenes = false;
            foreach (var node in state.Scenes)
            {
                if (node.Id == oldSceneNodeId)
                {
                    node.Id = newSceneNodeId;
                    node.Path = newPath;
                    node.Name = System.IO.Path.GetFileNameWithoutExtension(newPath);
                    foundInScenes = true;
                    break;
                }
            }

            if (foundInScenes)
            {
                foreach (var node in state.Assets)
                {
                    if (node.Id == oldAssetNodeId)
                    {
                        node.Id = newAssetNodeId;
                        node.Path = newPath;
                        node.Name = System.IO.Path.GetFileName(newPath);
                        break;
                    }
                }
                UpdateProjectSceneEdges(state);
            }
            else if (assetInfoMap.TryGetValue(newPath, out var info))
                HandleSceneModified(state, newPath, info);
        }

        // ----- Import / Modify -----

        static void HandleAssetModified(GraphInMemoryState state, string assetPath, AssetInfo info)
        {
            var nodeId = PathToNodeId(assetPath);
            int dependenciesCount = info.Dependencies.Count;
            int dependentsCount = 0;
            var existing = FindNodeInList(state.Assets, nodeId);
            if (existing != null)
            {
                if (existing.DependentsCount.HasValue)
                    dependentsCount = existing.DependentsCount.Value;
                if (existing.DependenciesCount.HasValue)
                    dependenciesCount = existing.DependenciesCount.Value;
            }

            var assetNode = new GraphNode
            {
                Id = nodeId,
                Type = "asset",
                AssetType = info.AssetType,
                Path = assetPath,
                Name = info.Name,
                DependenciesCount = dependenciesCount,
                DependentsCount = dependentsCount
            };

            UpsertNodeInList(state.Assets, nodeId, assetNode);

            UpdateAssetTypeNode(state, info.AssetType);
            UpdateAssetTypeEdge(state, nodeId, info.AssetType);
            UpdateAssetDependencies(state, nodeId, info.Dependencies);
        }

        static void HandleSceneModified(GraphInMemoryState state, string scenePath, AssetInfo info)
        {
            var assetNodeId = PathToNodeId(scenePath);
            var sceneNodeId = "scene_" + GraphRestructurer.NormalizeId(scenePath);

            int sceneDepsCount = info.Dependencies.Count;
            var existingScene = FindNodeInList(state.Scenes, sceneNodeId);
            if (existingScene?.DependenciesCount != null)
                sceneDepsCount = existingScene.DependenciesCount.Value;

            var sceneNode = new GraphNode
            {
                Id = sceneNodeId,
                Type = "scene",
                Path = scenePath,
                Name = System.IO.Path.GetFileNameWithoutExtension(scenePath),
                DependenciesCount = sceneDepsCount
            };
            UpsertNodeInList(state.Scenes, sceneNodeId, sceneNode);

            int assetDepsCount = info.Dependencies.Count;
            int assetDependentsCount = 0;
            var existingAsset = FindNodeInList(state.Assets, assetNodeId);
            if (existingAsset != null)
            {
                if (existingAsset.DependentsCount.HasValue)
                    assetDependentsCount = existingAsset.DependentsCount.Value;
                if (existingAsset.DependenciesCount.HasValue)
                    assetDepsCount = existingAsset.DependenciesCount.Value;
            }

            var assetNode = new GraphNode
            {
                Id = assetNodeId,
                Type = "asset",
                AssetType = info.AssetType,
                Path = scenePath,
                Name = info.Name,
                DependenciesCount = assetDepsCount,
                DependentsCount = assetDependentsCount
            };
            UpsertNodeInList(state.Assets, assetNodeId, assetNode);

            var sceneEdgesKey = EdgeKey(GraphGenerationConstants.EdgesSceneDependsOnAssetDir, GraphGenerationConstants.DependenciesFileName);
            var sceneEdges = GetOrCreateEdgeList(state, sceneEdgesKey);
            sceneEdges.RemoveAll(e => e.SrcId == sceneNodeId);
            foreach (var dep in info.Dependencies)
            {
                sceneEdges.Add(new GraphEdge
                {
                    SrcId = sceneNodeId,
                    DstId = PathToNodeId(dep),
                    RelationType = GraphRelationType.DependsOn,
                    SrcType = GraphNodeType.Scene,
                    DstType = GraphNodeType.Asset
                });
            }

            var refKey = EdgeKey(GraphGenerationConstants.EdgesAssetReferencedBySceneDir, GraphGenerationConstants.ReferencesFileName);
            var revEdges = GetOrCreateEdgeList(state, refKey);
            revEdges.RemoveAll(e => e.DstId == sceneNodeId);
            foreach (var dep in info.Dependencies)
            {
                revEdges.Add(new GraphEdge
                {
                    SrcId = PathToNodeId(dep),
                    DstId = sceneNodeId,
                    RelationType = GraphRelationType.ReferencedBy,
                    SrcType = GraphNodeType.Asset,
                    DstType = GraphNodeType.Scene
                });
            }

            UpdateAssetDependencies(state, assetNodeId, info.Dependencies);
            UpdateAssetTypeNode(state, info.AssetType);
            UpdateAssetTypeEdge(state, assetNodeId, info.AssetType);
            UpdateProjectSceneEdges(state);
        }

        // ----- Code Dependency Edges -----

        static void UpdateCodeDependencyEdges(GraphInMemoryState state, List<CodeDependencyInfo> codeDeps,
            List<string> csFiles)
        {
            var classNameToId = new Dictionary<string, string>();
            foreach (var asset in state.Assets)
            {
                var path = asset.Path;
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".cs"))
                {
                    var stem = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (!classNameToId.ContainsKey(stem))
                        classNameToId[stem] = asset.Id;
                }
            }

            var scriptNodeIds = new HashSet<string>(csFiles.Select(PathToNodeId));

            var edgeTypes = new (CodeDependencyType depType, string dir, string file, GraphRelationType relation)[]
            {
                (CodeDependencyType.InheritsFrom, GraphGenerationConstants.EdgesAssetInheritsFromAssetDir, GraphGenerationConstants.InheritanceFileName, GraphRelationType.InheritsFrom),
                (CodeDependencyType.Implements, GraphGenerationConstants.EdgesAssetImplementsAssetDir, GraphGenerationConstants.InterfaceImplementationFileName, GraphRelationType.Implements),
                (CodeDependencyType.Declares, GraphGenerationConstants.EdgesAssetDeclaresAssetDir, GraphGenerationConstants.FieldDeclarationsFileName, GraphRelationType.Declares),
                (CodeDependencyType.Uses, GraphGenerationConstants.EdgesAssetUsesAssetDir, GraphGenerationConstants.TypeUsageFileName, GraphRelationType.Uses),
            };

            foreach (var (depType, dir, file, relation) in edgeTypes)
            {
                var key = EdgeKey(dir, file);
                var edges = GetOrCreateEdgeList(state, key);
                edges.RemoveAll(e => scriptNodeIds.Contains(e.SrcId));

                var relevantDeps = codeDeps.Where(d => d.type == depType).ToList();
                foreach (var dep in relevantDeps)
                {
                    var fromPath = dep.from;
                    var toName = dep.to;
                    if (string.IsNullOrEmpty(fromPath) || string.IsNullOrEmpty(toName)) continue;

                    var srcId = PathToNodeId(fromPath);
                    if (!classNameToId.TryGetValue(toName, out var dstId)) continue;
                    if (srcId == dstId) continue;

                    edges.Add(new GraphEdge
                    {
                        SrcId = srcId,
                        DstId = dstId,
                        RelationType = relation,
                        SrcType = GraphNodeType.Asset,
                        DstType = GraphNodeType.Asset
                    });
                }
            }
        }

        // ----- Helpers -----

        static void UpdateAssetDependencies(GraphInMemoryState state, string nodeId, List<string> dependencies)
        {
            var key = EdgeKey(GraphGenerationConstants.EdgesAssetDependsOnAssetDir, GraphGenerationConstants.DependenciesFileName);
            var edges = GetOrCreateEdgeList(state, key);
            edges.RemoveAll(e => e.SrcId == nodeId);

            foreach (var dep in dependencies)
            {
                edges.Add(new GraphEdge
                {
                    SrcId = nodeId,
                    DstId = PathToNodeId(dep),
                    RelationType = GraphRelationType.DependsOn,
                    SrcType = GraphNodeType.Asset,
                    DstType = GraphNodeType.Asset
                });
            }
        }

        static void UpdateAssetTypeNode(GraphInMemoryState state, string assetType)
        {
            var types = state.AssetTypes;
            var typeId = $"assetType_{GraphRestructurer.NormalizeId(assetType)}";
            var existing = types.FirstOrDefault(t => t.Id == typeId);

            if (existing != null)
                existing.AssetCount = state.Assets.Count(a => a.AssetType == assetType);
            else
            {
                types.Add(new GraphAssetTypeNode
                {
                    Id = typeId,
                    Type = "assetType",
                    Name = assetType,
                    Description = $"{assetType} assets",
                    AssetCount = 1
                });
            }
        }

        static void UpdateAssetTypeEdge(GraphInMemoryState state, string assetNodeId, string assetType)
        {
            var key = EdgeKey(GraphGenerationConstants.EdgesAssetTypeIncludeAssetDir, GraphGenerationConstants.TypeMembershipFileName);
            var edges = GetOrCreateEdgeList(state, key);
            edges.RemoveAll(e => e.DstId == assetNodeId);

            var typeId = $"assetType_{GraphRestructurer.NormalizeId(assetType)}";
            edges.Add(new GraphEdge
            {
                SrcId = typeId,
                DstId = assetNodeId,
                RelationType = GraphRelationType.Includes,
                SrcType = GraphNodeType.AssetType,
                DstType = GraphNodeType.Asset
            });
        }

        static void UpdateProjectSceneEdges(GraphInMemoryState state)
        {
            var key = EdgeKey(GraphGenerationConstants.EdgesProjectHasSceneDir, GraphGenerationConstants.HasFileName);
            var edges = new List<GraphEdge>();
            foreach (var scene in state.Scenes)
            {
                var sceneId = scene.Id;
                if (!string.IsNullOrEmpty(sceneId))
                {
                    edges.Add(new GraphEdge
                    {
                        SrcId = "project_root",
                        DstId = sceneId,
                        RelationType = GraphRelationType.Has,
                        SrcType = GraphNodeType.Project,
                        DstType = GraphNodeType.Scene
                    });
                }
            }
            state.EdgesByRelativePath[key] = edges;
        }

        static void UpdateProjectAssetTypeEdges(GraphInMemoryState state)
        {
            var key = EdgeKey(GraphGenerationConstants.EdgesProjectContainsAssetTypeDir, GraphGenerationConstants.ContainsFileName);
            var edges = new List<GraphEdge>();
            foreach (var at in state.AssetTypes)
            {
                var atId = at.Id;
                if (!string.IsNullOrEmpty(atId))
                {
                    edges.Add(new GraphEdge
                    {
                        SrcId = "project_root",
                        DstId = atId,
                        RelationType = GraphRelationType.Contains,
                        SrcType = GraphNodeType.Project,
                        DstType = GraphNodeType.AssetType
                    });
                }
            }
            state.EdgesByRelativePath[key] = edges;
        }

        static void UpdateProjectCounts(GraphInMemoryState state)
        {
            if (state.Projects.Count == 0) return;
            var project = state.Projects[0];
            project.AssetCount = state.Assets.Count;
            project.SceneCount = state.Scenes.Count;
            project.AssetTypeCount = state.AssetTypes.Count;
        }

        static void UpdateMetadataTimestamp(GraphInMemoryState state)
        {
            if (state.Metadata != null)
                state.Metadata.LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        }

        static void ClearPendingChanges(string graphRoot)
        {
            var path = Path.Combine(graphRoot, GraphGenerationConstants.PendingChangesFile);
            try
            {
                var empty = new PendingChangesFile
                {
                    version = "1.0",
                    lastUpdate = "",
                    totalChanges = 0,
                    changes = new List<AssetChangeEvent>()
                };
                File.WriteAllText(path, JsonConvert.SerializeObject(empty, Formatting.Indented));
            }
            catch (Exception e)
            {
                InternalLog.LogWarning($"[GraphRefreshManager] Could not clear pending changes: {e.Message}");
            }
        }

        static void RecordRefreshTimestamp(string graphRoot)
        {
            var path = Path.Combine(graphRoot, GraphGenerationConstants.LastRefreshTimestampFile);
            try
            {
                File.WriteAllText(path, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            }
            catch (Exception e)
            {
                InternalLog.LogWarning($"[GraphRefreshManager] Could not record refresh timestamp: {e.Message}");
            }
        }

        static GraphNode FindNodeInList(List<GraphNode> list, string nodeId)
        {
            return list?.FirstOrDefault(n => n.Id == nodeId);
        }

        static void RemoveNodeFromList(List<GraphNode> list, string nodeId)
        {
            if (list == null) return;
            list.RemoveAll(n => n.Id == nodeId);
        }

        static void UpsertNodeInList(List<GraphNode> list, string nodeId, GraphNode node)
        {
            var idx = list.FindIndex(n => n.Id == nodeId);
            if (idx >= 0)
                list[idx] = node;
            else
                list.Add(node);
        }

        /// <summary>
        /// Remove all edges referencing any of the given node IDs from in-memory edge lists.
        /// </summary>
        static void RemoveFromAllEdgeFilesBatch(GraphInMemoryState state, HashSet<string> nodeIdsToRemove)
        {
            if (nodeIdsToRemove == null || nodeIdsToRemove.Count == 0) return;

            foreach (var edges in state.EdgesByRelativePath.Values)
            {
                edges.RemoveAll(e =>
                    nodeIdsToRemove.Contains(e.SrcId) ||
                    nodeIdsToRemove.Contains(e.DstId));
            }
        }

        /// <summary>
        /// Replace old node IDs with new in all in-memory edge lists.
        /// </summary>
        static void UpdateNodeIdInEdgeFilesBatch(GraphInMemoryState state, List<(string oldId, string newId)> replacements)
        {
            if (replacements == null || replacements.Count == 0) return;

            foreach (var edges in state.EdgesByRelativePath.Values)
            {
                foreach (var edge in edges)
                {
                    foreach (var (oldId, newId) in replacements)
                    {
                        if (edge.SrcId == oldId)
                            edge.SrcId = newId;
                        if (edge.DstId == oldId)
                            edge.DstId = newId;
                    }
                }
            }
        }

        static string PathToNodeId(string assetPath)
        {
            return "asset_" + GraphRestructurer.NormalizeId(assetPath);
        }

        struct ValidationResult
        {
            public bool IsValid;
        }

        ValidationResult ValidateGraphConsistency()
        {
            try
            {
                // Check required directories
                string[] requiredDirs = GraphGenerationConstants.RequiredNodeDirs;
                foreach (var dir in requiredDirs)
                {
                    if (!Directory.Exists(Path.Combine(_graphRoot, dir)))
                        return new ValidationResult { IsValid = false };
                }

                // Check metadata.json
                var metadataPath = Path.Combine(_graphRoot, GraphGenerationConstants.MetadataFile);
                if (!File.Exists(metadataPath))
                    return new ValidationResult { IsValid = false };

                // Check asset count within 10% tolerance
                var assetsFile = Path.Combine(_graphRoot, GraphGenerationConstants.AssetsPath);
                if (File.Exists(assetsFile) && File.Exists(metadataPath))
                {
                    var assets = LoadJsonList<GraphNode>(assetsFile);
                    var metadata = JsonConvert.DeserializeObject<GraphMetadata>(File.ReadAllText(metadataPath));
                    if (metadata != null)
                    {
                        var expected = metadata.TotalAssets;
                        var actual = assets.Count;
                        if (expected > 0 && Math.Abs(expected - actual) > expected * 0.1)
                            return new ValidationResult { IsValid = false };
                    }
                }

                // Check for orphaned edges
                var nodeIds = new HashSet<string>();
                foreach (var nodeDir in Directory.GetDirectories(_graphRoot, GraphGenerationConstants.NodesDirPattern))
                {
                    foreach (var nodeFile in Directory.GetFiles(nodeDir, "*.json"))
                    {
                        foreach (var node in LoadJsonList<GraphNodeRef>(nodeFile))
                        {
                            var id = node.Id;
                            if (!string.IsNullOrEmpty(id))
                                nodeIds.Add(id);
                        }
                    }
                }

                int orphanedCount = 0;
                foreach (var edgeDir in Directory.GetDirectories(_graphRoot, GraphGenerationConstants.EdgeDirPattern))
                {
                    foreach (var edgeFile in Directory.GetFiles(edgeDir, "*.json"))
                    {
                        foreach (var edge in LoadJsonList<GraphEdge>(edgeFile))
                        {
                            var src = edge.SrcId;
                            var dst = edge.DstId;
                            if (!string.IsNullOrEmpty(src) && !nodeIds.Contains(src)) orphanedCount++;
                            if (!string.IsNullOrEmpty(dst) && !nodeIds.Contains(dst)) orphanedCount++;
                        }
                    }
                }

                if (orphanedCount > k_MaxOrphanedEdges)
                    return new ValidationResult { IsValid = false };

                return new ValidationResult { IsValid = true };
            }
            catch
            {
                return new ValidationResult { IsValid = false };
            }
        }

        // ----- JSON I/O -----
        // Graph data is loaded once at start of refresh (LoadGraphState), mutated in memory, and saved once at end (SaveGraphState).

        static List<T> LoadJsonList<T>(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return new List<T>();
                var json = File.ReadAllText(filePath);
                var list = JsonConvert.DeserializeObject<List<T>>(json);
                return list ?? new List<T>();
            }
            catch
            {
                return new List<T>();
            }
        }

        static void SaveJsonList<T>(string filePath, List<T> data)
        {
            try
            {
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch (Exception e)
            {
                InternalLog.LogError($"[GraphRefreshManager] Error saving {filePath}: {e.Message}");
            }
        }
    }
}
