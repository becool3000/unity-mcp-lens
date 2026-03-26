using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor.GraphGeneration;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.Tools.Editor
{
    class DependencyTools
    {
        const string k_GetDependencyFunctionId = "Unity.GetDependency";

        // Store as object to avoid loading GraphRefreshManager (and PendingChangesTypes) when
        // DependencyTools is loaded for tool discovery (TypeCache). Loading that type at
        // discovery time can fail after refactors and cause GetDependency to be skipped.
        static object s_refreshManager;
        static string s_refreshManagerGraphRoot;
        static readonly object s_refreshManagerLock = new object();

        static GraphRefreshManager GetRefreshManager()
        {
            var root = AssistantGraphGenerator.GraphRoot;
            lock (s_refreshManagerLock)
            {
                if (s_refreshManager == null || s_refreshManagerGraphRoot != root)
                {
                    s_refreshManager = new GraphRefreshManager(root);
                    s_refreshManagerGraphRoot = root;
                }
                return (GraphRefreshManager)s_refreshManager;
            }
        }

        [AgentTool(
            @"Query the Unity project's pre-indexed dependency graph to map relationships between scripts, assets, scenes, prefabs, etc.

### Operational Guidelines
Before reading the content of any asset or making any changes, you MUST call Unity.GetDependency for that asset to perform impact analysis. If you do not know the path, find it first, then immediately call GetDependency before any other operation.
Use it to discover related scripts and assets instead of relying on broad name-based searches.
This tool provides a more direct path for context searching.
It is the most efficient way to find related scripts (via uses, declares, or referencedBy edges) compared to broad keyword searches.

### EFFICIENCY NOTE:
Use Unity.GetDependency when you need to understand relationships.
Unity.GetDependency is pre-indexed and significantly faster than scanning files for string matches,
and it captures non-obvious relationships like Scene-to-Asset dependencies that text searches might miss.
For example, when you modify a script, you can use this tool
to check what other scripts depend on it.
For any asset, you can find what references it, or explore its relationships.
Use get_edges multiple times to explore multi-hop relations.
Always check the default limit of max_results and increase it if needed.

### QUICK START:
- ""What will break if I delete this?"" → query_type: ""get_edges"", direction: ""incoming"", file_path: ""Assets/Path/To/Script.cs""
- ""What does this script rely on?"" → query_type: ""get_edges"", direction: ""outgoing"", file_path: ""Assets/Path/To/Script.cs""
- ""Which scenes use this prefab?"" → query_type: ""get_edges"", direction: ""incoming"", file_path: ""Assets/Prefabs/Player.prefab""

### KEY CAPABILITIES:
- Impact Analysis: Instantly map out what will break if a file is removed by querying its incoming edges (referencedBy, inheritsFrom, implements, declares).
- Dependency Tracing: Discover asset dependencies and references
- Scene-Asset Mapping: Find which scenes actually use a specific script or prefab.
- Script Structure Understanding:
   - Find all implementations of an interface
   - Trace class inheritance hierarchies
   - Identify field/property declarations
   - Type Usage Tracking: Identify which scripts use a class as a return type or parameter (uses).
- Refactoring Safety: Before deleting or renaming a class, map out its outgoing edges to understand what other systems it relies on.

### DEPENDENCY GRAPH REFERENCE

**I. STRUCTURE OVERVIEW**

Nodes Types: project, scene, asset, assetType, tool, toolCategory

Edge Types (Relationships):
- edges-asset_dependsOn_asset (asset uses another asset)
- edges-scene_dependsOn_asset (scene uses an asset)
- edges-asset_referencedBy_asset (asset is used by another asset)
- edges-asset_referencedBy_scene (asset is used by a scene)
- edges-asset_inheritsFrom_asset (class inheritance between scripts)
- edges-asset_implements_asset (interface implementation in scripts)
- edges-asset_declares_asset (field/property declarations in scripts)
- edges-asset_uses_asset (type usage: return types and parameters in scripts)
- edges-asset_include_asset (package/module inclusions)
- edges-assetType_has_asset (asset type contains specific assets)
- edges-toolCategory_canUse_tool (tool category provides tools)
- edges-project_contains_scene (project contains scenes)
- edges-project_contains_asset (project contains assets)

**II. NODE FEATURES**

Common FEATURES: id, name, path, type

Asset-Specific FEATURES: asset_type, dependencies_count, dependents_count
Scene-Specific FEATURES: dependencies_count
Tool-Specific FEATURES: function_id, function_name, function_namespace, tags

**II. HIGH-LEVEL NODE SUMMARY**
All 53 Asset Types:
AnimationClip, AnimatorController, AudioClip,
AudioMixerController, AvatarMask, BoolEvent,
CinemachineBlenderSettings, Cubemap, FloatEvent, Font,
GameEvent, GameObject, InputActionAsset, IntPairEvent,
LightingDataAsset, LightingSettings, Material, Mesh,
MonoScript, NetworkPrefabsList, NotificationEvent,
PanelSettings, PhysicsMaterial, PlayerStateEvent, Prefab,
ProbeVolumeBakingSet, QuickJoinSettings,
RespawnStatusEvent, SceneAsset, Script, SessionSettings,
Shader, ShaderInclude, SoundDef, StatChangeEvent,
StatDefinition, StatDepletedEvent, StatsConfig,
StyleSheet, SubGraphAsset, TMP_FontAsset, TextAsset,
Texture, Texture2D, ThemeStyleSheet, TimelineAsset,
Vector2Event, VisualEffectAsset, VisualTreeAsset,
VolumeProfile, WeaponData, WeaponLoadout, WeaponSwapEvent

All 9 Tool Categories:
code-correction, code-edit, code-execution, game-object,
play-mode, project-overview, smart-context,
static-context, ui

**III. NODE ID NAMING CONVENTIONS**

Project Nodes: project_root

Asset Nodes: asset_ + path (replace / and . with _)
Example: Assets/Scripts/Player.cs → asset_Assets_Scripts_Player_cs

Scene Nodes: scene_ + path (replace / and . with _)
Example: Assets/Scenes/Main.unity → scene_Assets_Scenes_Main_unity

AssetType Nodes: assetType_ + name
Example: assetType_Prefab, assetType_MonoScript

ToolCategory Nodes: toolCategory_ + name
Example: toolCategory_code_execution",
            k_GetDependencyFunctionId,
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_SmartContextTag)]
        public static async Task<string> GetDependency(
            ToolExecutionContext context,
            [Parameter(
                "Type of query. Must be one of:\n" +
                " - get_node: Fetch a single node by ID (asset, scene, project, assetType, toolCategory). Use when you need details for one node (e.g. type, path, metadata).\n" +
                " - get_edges: Fetch dependency/reference edges for a node. Use when you need 'what depends on this' (incoming) or 'what this depends on' (outgoing). Requires nodeId (or filePath).\n" +
                " - get_metadata: Fetch graph overview (root path, node counts, ID conventions). Use first to understand the graph or to learn node_id naming rules; no nodeId needed.")]
            string queryType,
            [Parameter("Node ID (e.g. 'asset_Assets_Scripts_Foo_cs')")]
            string nodeId = null,
            [Parameter("Unity file path (e.g. 'Assets/Scripts/Foo.cs') - auto-converted to node_id")]
            string filePath = null,
            [Parameter(
                "Edge direction for get_edges only. Ignored for get_node and get_metadata.\n" +
                " - incoming: Edges where this node is the destination (things that depend on or point to this node).\n" +
                " - outgoing: Edges where this node is the source (things this node depends on or points to).\n" +
                " - both: Return both incoming and outgoing edges (default).")]
            string direction = "both",
            [Parameter("Max results for get_edges (default: 100, max: 1000)")]
            int maxResults = 100)
        {
            try
            {
                var graphRoot = AssistantGraphGenerator.GraphRoot;

                if (!AssistantGraphGenerator.GraphExists())
                {
                    AssistantGraphGenerator.GenerateGraphAsync();
                    return JsonConvert.SerializeObject(new
                    {
                        message = "The dependency graph is still being generated in the background. Please try your query again in a moment.",
                        graph_root = graphRoot
                    }, Formatting.Indented);
                }

                if (!string.IsNullOrEmpty(filePath) && string.IsNullOrEmpty(nodeId))
                    nodeId = GraphQueryEngine.ResolveFilePathToNodeId(graphRoot, filePath);

                maxResults = Math.Clamp(maxResults, 1, 1000);

                var refreshManager = GetRefreshManager();
                if (refreshManager.HasPendingChanges())
                {
                    var refreshResult = await refreshManager.ProcessPendingChangesAsync();
                    if (refreshResult.Status == "error" && refreshResult.NeedsFullRegeneration)
                    {
                        // Optional: could trigger full regeneration here; plan says skip
                    }
                }

                var qt = (queryType ?? "").Trim().ToLowerInvariant();
                if (qt == "get_node")
                    return GraphQueryEngine.GetNode(graphRoot, nodeId);
                if (qt == "get_edges")
                    return GraphQueryEngine.GetEdges(graphRoot, nodeId, direction ?? "both", maxResults);
                if (qt == "get_metadata")
                    return GraphQueryEngine.GetMetadata(graphRoot);

                throw new ArgumentException(
                    $"Unknown query_type: {queryType}. Valid types: get_node, get_edges, get_metadata.");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"GetDependency failed: {ex.Message}", ex);
            }
        }
    }
}
