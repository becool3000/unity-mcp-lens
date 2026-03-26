using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Editor.GraphGeneration
{
    /// <summary>
    /// Restructures a DependencyGraph into the AI.CoreGraph/ folder structure.
    /// Ported 1:1 from restructure.py. Runs on a background thread (pure file I/O).
    /// </summary>
    static class GraphRestructurer
    {
        internal static string NormalizeId(string pathOrId)
        {
            return pathOrId
                .Replace("/", "_")
                .Replace("\\", "_")
                .Replace(" ", "_")
                .Replace(".", "_")
                .Replace("-", "_");
        }

        static void WriteJson(string filePath, object data)
        {
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

        static void WriteNodeDir(string outputDir, string dirName, string fileName, object data)
        {
            var dir = Path.Combine(outputDir, dirName);
            Directory.CreateDirectory(dir);
            WriteJson(Path.Combine(dir, fileName), data);
        }

        public static void RestructureGraph(DependencyGraph graph, string outputDir)
        {
            var nodeIds = new Dictionary<string, string>();

            // --- Scene nodes ---
            var sceneNodes = new List<Dictionary<string, object>>();
            foreach (var scene in graph.scenes)
            {
                var nodeId = $"scene_{NormalizeId(scene.path)}";
                nodeIds[scene.path] = nodeId;
                sceneNodes.Add(new Dictionary<string, object>
                {
                    { "id", nodeId },
                    { "type", "scene" },
                    { "path", scene.path },
                    { "name", scene.name },
                    { "dependencies_count", scene.dependencies?.Count ?? 0 }
                });
            }

            // --- Asset nodes ---
            var assetNodes = new List<Dictionary<string, object>>();
            foreach (var asset in graph.assetDependencies)
            {
                if (asset.path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) continue;

                var assetType = !string.IsNullOrEmpty(asset.assetType)
                    ? asset.assetType
                    : AssetTypeUtils.GetAssetTypeFromPath(asset.path);
                var nodeId = $"asset_{NormalizeId(asset.path)}";
                nodeIds[asset.path] = nodeId;
                assetNodes.Add(new Dictionary<string, object>
                {
                    { "id", nodeId },
                    { "type", "asset" },
                    { "asset_type", assetType },
                    { "path", asset.path },
                    { "name", asset.name },
                    { "dependencies_count", asset.dependencies?.Count ?? 0 },
                    { "dependents_count", asset.dependents?.Count ?? 0 }
                });
            }

            // --- Asset type nodes ---
            var typeDescriptions = AssetTypeUtils.GetAssetTypeDescriptions();
            var typeCounts = new Dictionary<string, int>();
            foreach (var asset in graph.assetDependencies)
            {
                var assetType = !string.IsNullOrEmpty(asset.assetType)
                    ? asset.assetType
                    : AssetTypeUtils.GetAssetTypeFromPath(asset.path);
                typeCounts[assetType] = typeCounts.GetValueOrDefault(assetType) + 1;
            }
            var assetTypeNodes = new List<Dictionary<string, object>>();
            foreach (var kvp in typeCounts)
            {
                assetTypeNodes.Add(new Dictionary<string, object>
                {
                    { "id", $"assetType_{NormalizeId(kvp.Key)}" },
                    { "type", "assetType" },
                    { "name", kvp.Key },
                    { "description", typeDescriptions.GetValueOrDefault(kvp.Key, $"Assets of type {kvp.Key}") },
                    { "asset_count", kvp.Value }
                });
            }

            // --- Project node ---
            var projectNode = new Dictionary<string, object>
            {
                { "id", "project_root" },
                { "type", "project" },
                { "name", graph.projectName ?? "" },
                { "unity_version", graph.unityVersion ?? "" },
                { "description", $"Central project node for {graph.projectName}. Connects to all scenes, asset types, and tool categories." },
                { "scene_count", sceneNodes.Count },
                { "asset_count", assetNodes.Count },
                { "tool_count", 0 },
                { "asset_type_count", assetTypeNodes.Count },
                { "tool_category_count", 0 }
            };

            // --- scene_dependsOn_asset edges ---
            var sceneAssetEdges = new List<Dictionary<string, object>>();
            foreach (var scene in graph.scenes)
            {
                if (!nodeIds.TryGetValue(scene.path, out var sceneId)) continue;
                foreach (var dep in scene.dependencies ?? new List<string>())
                {
                    if (nodeIds.TryGetValue(dep, out var assetId))
                    {
                        sceneAssetEdges.Add(new Dictionary<string, object>
                        {
                            { "src_id", sceneId }, { "dst_id", assetId },
                            { "relation_type", "dependsOn" }, { "src_type", "scene" }, { "dst_type", "asset" }
                        });
                    }
                }
            }

            // --- asset_dependsOn_asset edges ---
            var assetAssetEdges = new List<Dictionary<string, object>>();
            foreach (var asset in graph.assetDependencies)
            {
                if (!nodeIds.TryGetValue(asset.path, out var assetId)) continue;
                foreach (var dep in asset.dependencies ?? new List<string>())
                {
                    if (nodeIds.TryGetValue(dep, out var depId))
                    {
                        assetAssetEdges.Add(new Dictionary<string, object>
                        {
                            { "src_id", assetId }, { "dst_id", depId },
                            { "relation_type", "dependsOn" }, { "src_type", "asset" }, { "dst_type", "asset" }
                        });
                    }
                }
            }

            // --- asset_referencedBy_scene edges ---
            var assetSceneEdges = new List<Dictionary<string, object>>();
            foreach (var asset in graph.assetDependencies)
            {
                if (!nodeIds.TryGetValue(asset.path, out var assetId)) continue;
                foreach (var dependent in asset.dependents ?? Enumerable.Empty<string>())
                {
                    if (dependent.EndsWith(".unity") && nodeIds.TryGetValue(dependent, out var sceneId))
                    {
                        assetSceneEdges.Add(new Dictionary<string, object>
                        {
                            { "src_id", assetId }, { "dst_id", sceneId },
                            { "relation_type", "referencedBy" }, { "src_type", "asset" }, { "dst_type", "scene" }
                        });
                    }
                }
            }

            // --- assetType_include_asset edges ---
            var typeAssetEdges = new List<Dictionary<string, object>>();
            foreach (var asset in graph.assetDependencies)
            {
                if (!nodeIds.TryGetValue(asset.path, out var assetId)) continue;
                var assetType = !string.IsNullOrEmpty(asset.assetType)
                    ? asset.assetType
                    : AssetTypeUtils.GetAssetTypeFromPath(asset.path);
                typeAssetEdges.Add(new Dictionary<string, object>
                {
                    { "src_id", $"assetType_{NormalizeId(assetType)}" }, { "dst_id", assetId },
                    { "relation_type", "includes" }, { "src_type", "assetType" }, { "dst_type", "asset" }
                });
            }

            // --- project edges ---
            var projectSceneEdges = sceneNodes.Select(s => new Dictionary<string, object>
            {
                { "src_id", "project_root" }, { "dst_id", s["id"] },
                { "relation_type", "has" }, { "src_type", "project" }, { "dst_type", "scene" }
            }).ToList();

            var projectAssetTypeEdges = assetTypeNodes.Select(a => new Dictionary<string, object>
            {
                { "src_id", "project_root" }, { "dst_id", a["id"] },
                { "relation_type", "contains" }, { "src_type", "project" }, { "dst_type", "assetType" }
            }).ToList();

            // --- Code dependency edges ---
            var codeDeps = graph.codeDependencies;

            var inheritEdges = new List<Dictionary<string, object>>();
            var implementEdges = new List<Dictionary<string, object>>();
            var declareEdges = new List<Dictionary<string, object>>();
            var usesEdges = new List<Dictionary<string, object>>();

            var assetPathToId = new Dictionary<string, string>();
            foreach (var kvp in nodeIds)
            {
                assetPathToId[kvp.Key] = kvp.Value;
            }

            // Build a class name to ID mapping for dep.to lookups (heuristic: class name = file stem)
            var classNameToId = new Dictionary<string, string>();
            foreach (var kvp in nodeIds)
            {
                var stem = Path.GetFileNameWithoutExtension(kvp.Key);
                if (!classNameToId.ContainsKey(stem))
                {
                    classNameToId[stem] = kvp.Value;
                }
            }

            foreach (var dep in codeDeps)
            {
                if (dep.type == CodeDependencyType.InheritsFrom)
                {
                    var fromId = assetPathToId.GetValueOrDefault(dep.from);
                    var toId = classNameToId.GetValueOrDefault(dep.to);
                    if (fromId != null && toId != null)
                    {
                        inheritEdges.Add(new Dictionary<string, object>
                        {
                            { "src_id", fromId }, { "dst_id", toId },
                            { "relation_type", "inheritsFrom" }, { "src_type", "asset" }, { "dst_type", "asset" }
                        });
                    }
                }
                else if (dep.type == CodeDependencyType.Implements)
                {
                    var fromId = assetPathToId.GetValueOrDefault(dep.from);
                    var toId = classNameToId.GetValueOrDefault(dep.to);
                    if (fromId != null && toId != null)
                    {
                        implementEdges.Add(new Dictionary<string, object>
                        {
                            { "src_id", fromId }, { "dst_id", toId },
                            { "relation_type", "implements" }, { "src_type", "asset" }, { "dst_type", "asset" }
                        });
                    }
                }
                else if (dep.type == CodeDependencyType.Declares)
                {
                    var fromId = assetPathToId.GetValueOrDefault(dep.from);
                    var toId = classNameToId.GetValueOrDefault(dep.to);
                    if (fromId != null && toId != null)
                    {
                        declareEdges.Add(new Dictionary<string, object>
                        {
                            { "src_id", fromId }, { "dst_id", toId },
                            { "relation_type", "declares" }, { "src_type", "asset" }, { "dst_type", "asset" }
                        });
                    }
                }

                else if (dep.type == CodeDependencyType.Uses)
                {
                    var fromId = assetPathToId.GetValueOrDefault(dep.from);
                    var toId = classNameToId.GetValueOrDefault(dep.to);
                    if (fromId != null && toId != null)
                    {
                        usesEdges.Add(new Dictionary<string, object>
                        {
                            { "src_id", fromId }, { "dst_id", toId },
                            { "relation_type", "uses" }, { "src_type", "asset" }, { "dst_type", "asset" }
                        });
                    }
                }
            }

            // --- Compute dependencies_count and dependents_count from edge lists ---
            // Outgoing (dependencies): asset as src in asset_dependsOn_asset, inheritsFrom, implements, declares, uses
            // Incoming (dependents): asset as dst in asset_dependsOn_asset, scene_dependsOn_asset, inheritsFrom, implements, declares, uses
            var outgoingCount = new Dictionary<string, int>();
            var incomingCount = new Dictionary<string, int>();
            foreach (var edge in assetAssetEdges)
            {
                var src = edge["src_id"] as string;
                var dst = edge["dst_id"] as string;
                if (src != null) outgoingCount[src] = outgoingCount.GetValueOrDefault(src, 0) + 1;
                if (dst != null) incomingCount[dst] = incomingCount.GetValueOrDefault(dst, 0) + 1;
            }
            foreach (var edge in sceneAssetEdges)
            {
                var dst = edge["dst_id"] as string;
                if (dst != null) incomingCount[dst] = incomingCount.GetValueOrDefault(dst, 0) + 1;
            }
            foreach (var edge in inheritEdges)
            {
                var src = edge["src_id"] as string;
                var dst = edge["dst_id"] as string;
                if (src != null) outgoingCount[src] = outgoingCount.GetValueOrDefault(src, 0) + 1;
                if (dst != null) incomingCount[dst] = incomingCount.GetValueOrDefault(dst, 0) + 1;
            }
            foreach (var edge in implementEdges)
            {
                var src = edge["src_id"] as string;
                var dst = edge["dst_id"] as string;
                if (src != null) outgoingCount[src] = outgoingCount.GetValueOrDefault(src, 0) + 1;
                if (dst != null) incomingCount[dst] = incomingCount.GetValueOrDefault(dst, 0) + 1;
            }
            foreach (var edge in declareEdges)
            {
                var src = edge["src_id"] as string;
                var dst = edge["dst_id"] as string;
                if (src != null) outgoingCount[src] = outgoingCount.GetValueOrDefault(src, 0) + 1;
                if (dst != null) incomingCount[dst] = incomingCount.GetValueOrDefault(dst, 0) + 1;
            }
            foreach (var edge in usesEdges)
            {
                var src = edge["src_id"] as string;
                var dst = edge["dst_id"] as string;
                if (src != null) outgoingCount[src] = outgoingCount.GetValueOrDefault(src, 0) + 1;
                if (dst != null) incomingCount[dst] = incomingCount.GetValueOrDefault(dst, 0) + 1;
            }
            foreach (var node in assetNodes)
            {
                var id = node["id"] as string;
                if (id != null)
                {
                    node["dependencies_count"] = outgoingCount.GetValueOrDefault(id, 0);
                    node["dependents_count"] = incomingCount.GetValueOrDefault(id, 0);
                }
            }

            // --- Write everything to disk ---
            WriteNodeDir(outputDir, GraphGenerationConstants.NodesSceneDir, GraphGenerationConstants.ScenesFile, sceneNodes);
            WriteNodeDir(outputDir, GraphGenerationConstants.NodesAssetDir, GraphGenerationConstants.AssetsFile, assetNodes);
            WriteNodeDir(outputDir, GraphGenerationConstants.NodesToolDir, GraphGenerationConstants.ToolsFile, new List<object>());
            WriteNodeDir(outputDir, GraphGenerationConstants.NodesAssetTypeDir, GraphGenerationConstants.AssetTypesFile, assetTypeNodes);
            WriteNodeDir(outputDir, GraphGenerationConstants.NodesToolCategoryDir, GraphGenerationConstants.ToolCategoriesFile, new List<object>());
            WriteNodeDir(outputDir, GraphGenerationConstants.NodesProjectDir, GraphGenerationConstants.ProjectFile, new List<object> { projectNode });

            WriteNodeDir(outputDir, GraphGenerationConstants.EdgesSceneDependsOnAssetDir, GraphGenerationConstants.DependenciesFileName, sceneAssetEdges);
            WriteNodeDir(outputDir, GraphGenerationConstants.EdgesAssetDependsOnAssetDir, GraphGenerationConstants.DependenciesFileName, assetAssetEdges);
            WriteNodeDir(outputDir, GraphGenerationConstants.EdgesAssetReferencedBySceneDir, GraphGenerationConstants.ReferencesFileName, assetSceneEdges);
            WriteNodeDir(outputDir, GraphGenerationConstants.EdgesAssetTypeIncludeAssetDir, GraphGenerationConstants.TypeMembershipFileName, typeAssetEdges);
            WriteNodeDir(outputDir, GraphGenerationConstants.EdgesToolCategoryIncludeToolDir, GraphGenerationConstants.CategoryMembershipFileName, new List<object>());
            WriteNodeDir(outputDir, GraphGenerationConstants.EdgesProjectCanUseToolCategoryDir, GraphGenerationConstants.CanUseFileName, new List<object>());
            WriteNodeDir(outputDir, GraphGenerationConstants.EdgesProjectHasSceneDir, GraphGenerationConstants.HasFileName, projectSceneEdges);
            WriteNodeDir(outputDir, GraphGenerationConstants.EdgesProjectContainsAssetTypeDir, GraphGenerationConstants.ContainsFileName, projectAssetTypeEdges);
            WriteNodeDir(outputDir, GraphGenerationConstants.EdgesAssetInheritsFromAssetDir, GraphGenerationConstants.InheritanceFileName, inheritEdges);
            WriteNodeDir(outputDir, GraphGenerationConstants.EdgesAssetImplementsAssetDir, GraphGenerationConstants.InterfaceImplementationFileName, implementEdges);
            WriteNodeDir(outputDir, GraphGenerationConstants.EdgesAssetDeclaresAssetDir, GraphGenerationConstants.FieldDeclarationsFileName, declareEdges);
            if (usesEdges.Count > 0)
                WriteNodeDir(outputDir, GraphGenerationConstants.EdgesAssetUsesAssetDir, GraphGenerationConstants.TypeUsageFileName, usesEdges);


            // metadata.json written last as completion signal
            var metadata = new Dictionary<string, object>
            {
                { "total_scenes", sceneNodes.Count },
                { "total_assets", assetNodes.Count },
                { "total_tools", 0 },
                { "total_asset_types", assetTypeNodes.Count },
                { "total_tool_categories", 0 },
                { "total_projects", 1 },
                { "total_scene_dependsOn_asset_edges", sceneAssetEdges.Count },
                { "total_asset_dependsOn_asset_edges", assetAssetEdges.Count },
                { "total_asset_referencedBy_scene_edges", assetSceneEdges.Count },
                { "total_assetType_includes_asset_edges", typeAssetEdges.Count },
                { "total_toolCategory_includes_tool_edges", 0 },
                { "total_project_canUse_toolCategory_edges", 0 },
                { "total_project_has_scene_edges", projectSceneEdges.Count },
                { "total_project_contains_assetType_edges", projectAssetTypeEdges.Count },
                { "total_asset_inheritsFrom_asset_edges", inheritEdges.Count },
                { "total_asset_implements_asset_edges", implementEdges.Count },
                { "total_asset_declares_asset_edges", declareEdges.Count },
                { "total_asset_uses_asset_edges", usesEdges.Count },
                { "project_name", graph.projectName ?? "" },
                { "unity_version", graph.unityVersion ?? "" }
            };
            WriteJson(Path.Combine(outputDir, "metadata.json"), metadata);
        }
    }
}
