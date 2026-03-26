using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Converters;
using Unity.AI.Assistant.Utils;
using Unity.AI.Toolkit;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.GraphGeneration
{
    #region Data Models

    [Serializable]
    class DependencyGraph
    {
        public string projectName;
        public string unityVersion;
        public string timestamp;
        public List<AssetDependencyInfo> assetDependencies = new List<AssetDependencyInfo>();
        public List<SceneInfo> scenes = new List<SceneInfo>();
        public List<CodeDependencyInfo> codeDependencies = new List<CodeDependencyInfo>();
    }

    [Serializable]
    class SceneInfo
    {
        public string path;
        public string name;
        public List<string> dependencies = new List<string>();
    }

    [Serializable]
    class AssetDependencyInfo
    {
        public string path;
        public string name;
        public string assetType;
        public List<string> dependencies = new List<string>();
        public HashSet<string> dependents = new HashSet<string>();
    }

    enum CodeDependencyType
    {
        InheritsFrom,
        Implements,
        Declares,
        Uses
    }

    [Serializable]
    class CodeDependencyInfo
    {
        [Newtonsoft.Json.JsonConverter(typeof(StringEnumConverter))]
        public CodeDependencyType type;
        public string from;
        public string to;
        public List<CodeReference> references = new List<CodeReference>();
    }

    [Serializable]
    class CodeReference
    {
        public string sourceFile;
        public int lineNumber;
    }

    #endregion

    static class AssistantGraphGenerator
    {
        const string GraphFolder = "AI.CoreGraph";
        const int BatchSize = 100;
        static int s_GenerationInProgress;
        internal static bool s_SuppressGeneration;

        /// <summary>
        /// Path to the graph under the project Library folder so the backend can resolve it as unity_project_path/Library/AI.CoreGraph.
        /// </summary>
        public static string GraphRoot => Path.Combine(
            Path.GetDirectoryName(Application.dataPath), "Library", GraphFolder);

        public static bool GraphExists()
        {
            return Directory.Exists(GraphRoot)
                && File.Exists(Path.Combine(GraphRoot, "metadata.json"));
        }

        /// <summary>
        /// Clears the in-progress flag so a subsequent open can trigger generation (e.g. when the Assistant window was closed mid-generation).
        /// </summary>
        internal static void ResetGenerationInProgress()
        {
            Interlocked.Exchange(ref s_GenerationInProgress, 0);
        }

        public static void GenerateGraphAsync()
        {
            if (s_SuppressGeneration || GraphExists()) return;
            if (Interlocked.CompareExchange(ref s_GenerationInProgress, 1, 0) != 0)
            {
                if (!GraphExists())
                    Interlocked.Exchange(ref s_GenerationInProgress, 0);
                return;
            }
            _ = GenerateGraphTask();
        }

        static async Task GenerateGraphTask()
        {
            try
            {
                InternalLog.Log("[AssistantGraphGenerator] Starting incremental graph generation...");

                var generator = new GraphGenerator();
                var graph = new DependencyGraph
                {
                    projectName = Application.productName,
                    unityVersion = Application.unityVersion,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    assetDependencies = new List<AssetDependencyInfo>(),
                    scenes = new List<SceneInfo>(),
                    codeDependencies = new List<CodeDependencyInfo>()
                };

                InternalLog.Log($"[AssistantGraphGenerator] Starting graph generation for project: {Application.productName}");
                InternalLog.Log($"[AssistantGraphGenerator] Unity version: {Application.unityVersion}");

                AssetDatabase.Refresh();
                await EditorTask.Yield();

                var assetDict = new Dictionary<string, AssetDependencyInfo>();

                string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");
                InternalLog.Log($"[AssistantGraphGenerator] Found {sceneGuids.Length} scene(s)");

                foreach (string guid in sceneGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(path))
                    {
                        generator.ProcessScene(path, graph, assetDict);
                    }
                }

                await EditorTask.Yield();

                string[] allGuids = AssetDatabase.FindAssets("t:Object");
                InternalLog.Log($"[AssistantGraphGenerator] Processing {allGuids.Length} asset(s)...");

                for (int i = 0; i < allGuids.Length; i++)
                {
                    string guid = allGuids[i];
                    string path = AssetDatabase.GUIDToAssetPath(guid);

                    string[] dependencies = AssetDatabase.GetDependencies(path, true);

                    if (assetDict.TryGetValue(path, out var existing))
                    {
                        existing.dependencies.Clear();
                        foreach (string dep in dependencies)
                        {
                            if (dep == path) continue;
                            existing.dependencies.Add(dep);

                            if (!assetDict.ContainsKey(dep))
                            {
                                assetDict[dep] = new AssetDependencyInfo
                                {
                                    path = dep,
                                    name = Path.GetFileName(dep),
                                    assetType = generator.GetAssetType(dep),
                                    dependencies = new List<string>(),
                                    dependents = new HashSet<string>()
                                };
                            }

                            if (!assetDict[dep].dependents.Contains(path))
                                assetDict[dep].dependents.Add(path);
                        }
                    }
                    else
                    {
                        var assetInfo = new AssetDependencyInfo
                        {
                            path = path,
                            name = Path.GetFileName(path),
                            assetType = generator.GetAssetType(path),
                            dependencies = new List<string>(),
                            dependents = new HashSet<string>()
                        };

                        foreach (string dep in dependencies)
                        {
                            if (dep == path) continue;

                            assetInfo.dependencies.Add(dep);

                            if (!assetDict.ContainsKey(dep))
                            {
                                assetDict[dep] = new AssetDependencyInfo
                                {
                                    path = dep,
                                    name = Path.GetFileName(dep),
                                    assetType = generator.GetAssetType(dep),
                                    dependencies = new List<string>(),
                                    dependents = new HashSet<string>()
                                };
                            }

                            if (!assetDict[dep].dependents.Contains(path))
                                assetDict[dep].dependents.Add(path);
                        }

                        assetDict[path] = assetInfo;
                    }

                    if ((i + 1) % BatchSize == 0)
                        await EditorTask.Yield();
                }

                graph.assetDependencies = assetDict.Values.ToList();
                InternalLog.Log($"[AssistantGraphGenerator] Final graph: {graph.scenes.Count} scene(s), {graph.assetDependencies.Count} asset(s)");

                var projectRoot = Path.GetDirectoryName(Application.dataPath);
                var graphRoot = GraphRoot;

                // Fast path: discover C# files with Directory.GetFiles on background thread and write graph immediately.
                // Include both Assets and Packages so the graph covers project and package scripts (matches AssetDatabase scope).
                // In parallel, build Unity's canonical list via AssetDatabase for validation (addresses symlinks / Unity view).
                var task = Task.Run(() =>
                {
                    var assetsPath = Path.Combine(projectRoot, "Assets");
                    var pathsFromAssets = Directory.GetFiles(assetsPath, "*.cs", SearchOption.AllDirectories);
                    var packagesPath = Path.Combine(projectRoot, "Packages");
                    var pathsFromPackages = Directory.Exists(packagesPath)
                        ? Directory.GetFiles(packagesPath, "*.cs", SearchOption.AllDirectories)
                        : Array.Empty<string>();
                    var pathsFromGetFiles = pathsFromAssets.Concat(pathsFromPackages).ToList();
                    var analyzer = new CodeDependencyAnalyzer();
                    graph.codeDependencies = analyzer.AnalyzeCodeDependencies(pathsFromGetFiles);
                    GraphRestructurer.RestructureGraph(graph, graphRoot);
                    return pathsFromGetFiles;
                });

                var csFilePathsFromAssetDb = new List<string>();
                foreach (var guid in AssetDatabase.FindAssets("t:MonoScript"))
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (assetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    {
                        var combinedPath = Path.Combine(projectRoot, assetPath);
                        if (File.Exists(combinedPath))
                            csFilePathsFromAssetDb.Add(combinedPath);
                    }
                }

                var pathsFromGetFiles = await task;

                if (!PathSetsEqual(pathsFromGetFiles, csFilePathsFromAssetDb))
                {
                    InternalLog.Log("[AssistantGraphGenerator] C# file list differed from AssetDatabase (e.g. symlinks); regenerating graph with Unity asset list.");
                    await Task.Run(() =>
                    {
                        var analyzer = new CodeDependencyAnalyzer();
                        graph.codeDependencies = analyzer.AnalyzeCodeDependencies(csFilePathsFromAssetDb);
                        GraphRestructurer.RestructureGraph(graph, graphRoot);
                    });
                }

                InternalLog.Log($"[AssistantGraphGenerator] Graph generation complete: {graphRoot}");
            }
            catch (Exception ex)
            {
                InternalLog.LogWarning($"[AssistantGraphGenerator] Background generation failed: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                Interlocked.Exchange(ref s_GenerationInProgress, 0);
            }
        }

        static string NormalizePath(string path)
        {
            return path?.Replace('\\', '/').TrimEnd('/') ?? "";
        }

        static bool PathSetsEqual(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            if (a.Count != b.Count) return false;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in a)
                set.Add(NormalizePath(p));
            foreach (var p in b)
                if (!set.Contains(NormalizePath(p))) return false;
            return true;
        }

    #region GraphGenerator - Phase 1, main thread (from full_graph_generation.cs)

    class GraphGenerator
    {
        public void ProcessScene(string scenePath, DependencyGraph graph, Dictionary<string, AssetDependencyInfo> assetDict)
        {
            var sceneInfo = new SceneInfo
            {
                path = scenePath,
                name = Path.GetFileNameWithoutExtension(scenePath),
                dependencies = new List<string>()
            };

            string[] dependencies = AssetDatabase.GetDependencies(scenePath, true);

            foreach (string dep in dependencies)
            {
                if (dep == scenePath) continue;

                sceneInfo.dependencies.Add(dep);

                if (!assetDict.ContainsKey(dep))
                {
                    assetDict[dep] = new AssetDependencyInfo
                    {
                        path = dep,
                        name = Path.GetFileName(dep),
                        assetType = GetAssetType(dep),
                        dependencies = new List<string>(),
                        dependents = new HashSet<string>()
                    };
                }

                if (!assetDict[dep].dependents.Contains(scenePath))
                    assetDict[dep].dependents.Add(scenePath);
            }

            graph.scenes.Add(sceneInfo);
        }

        /// <summary>
        /// Returns the asset type using AssetDatabase.GetMainAssetTypeAtPath (no load). When the type is not
        /// available (e.g. not yet imported), falls back to an extension-based mapping aligned with Unity's
        /// common importers and FindAssets filters. See Unity Scripting Reference: AssetDatabase.FindAssets.
        /// </summary>
        public string GetAssetType(string path)
        {
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (type != null) return type.Name;

            return AssetTypeUtils.GetAssetTypeFromPath(path);
        }
    }

    #endregion
    }
}
