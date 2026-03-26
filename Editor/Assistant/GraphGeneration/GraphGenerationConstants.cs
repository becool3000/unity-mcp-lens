using System.IO;

namespace Unity.AI.Assistant.Editor.GraphGeneration
{
    /// <summary>
    /// Shared directory and file names for the AI.CoreGraph layout.
    /// Single source of truth for GraphQueryEngine, GraphRefreshManager, and GraphRestructurer.
    /// </summary>
    internal static class GraphGenerationConstants
    {
        // Node directories
        public const string NodesProjectDir = "nodes-project";
        public const string NodesSceneDir = "nodes-scene";
        public const string NodesAssetDir = "nodes-asset";
        public const string NodesToolDir = "nodes-tool";
        public const string NodesAssetTypeDir = "nodes-assetType";
        public const string NodesToolCategoryDir = "nodes-toolCategory";

        // Node files
        public const string ProjectFile = "project.json";
        public const string ScenesFile = "scenes.json";
        public const string AssetsFile = "assets.json";
        public const string ToolsFile = "tools.json";
        public const string AssetTypesFile = "assetTypes.json";
        public const string ToolCategoriesFile = "toolCategories.json";

        // Directory globs for Directory.GetDirectories
        public const string EdgeDirPattern = "edges-*";
        public const string NodesDirPattern = "nodes-*";

        // Common paths (dir + file)
        public static string AssetsPath => Path.Combine(NodesAssetDir, AssetsFile);
        public static string ScenesPath => Path.Combine(NodesSceneDir, ScenesFile);
        public static string ProjectPath => Path.Combine(NodesProjectDir, ProjectFile);
        public static string AssetTypesPath => Path.Combine(NodesAssetTypeDir, AssetTypesFile);

        // Edge dir + file names (for path building)
        public const string EdgesSceneDependsOnAssetDir = "edges-scene_dependsOn_asset";
        public const string EdgesAssetDependsOnAssetDir = "edges-asset_dependsOn_asset";
        public const string EdgesAssetReferencedBySceneDir = "edges-asset_referencedBy_scene";
        public const string EdgesAssetTypeIncludeAssetDir = "edges-assetType_include_asset";
        public const string EdgesToolCategoryIncludeToolDir = "edges-toolCategory_include_tool";
        public const string EdgesProjectCanUseToolCategoryDir = "edges-project_canUse_toolCategory";
        public const string EdgesProjectHasSceneDir = "edges-project_has_scene";
        public const string EdgesProjectContainsAssetTypeDir = "edges-project_contains_assetType";
        public const string EdgesAssetInheritsFromAssetDir = "edges-asset_inheritsFrom_asset";
        public const string EdgesAssetImplementsAssetDir = "edges-asset_implements_asset";
        public const string EdgesAssetDeclaresAssetDir = "edges-asset_declares_asset";
        public const string EdgesAssetUsesAssetDir = "edges-asset_uses_asset";

        public const string DependenciesFileName = "dependencies.json";
        public const string ReferencesFileName = "references.json";
        public const string TypeMembershipFileName = "type_membership.json";
        public const string HasFileName = "has.json";
        public const string ContainsFileName = "contains.json";
        public const string InheritanceFileName = "inheritance.json";
        public const string InterfaceImplementationFileName = "interface_implementation.json";
        public const string FieldDeclarationsFileName = "field_declarations.json";
        public const string TypeUsageFileName = "type_usage.json";
        public const string CategoryMembershipFileName = "category_membership.json";
        public const string CanUseFileName = "canUse.json";

        public const string MetadataFile = "metadata.json";

        // Pending changes and refresh timestamps (shared with GraphQueryEngine and GraphRefreshPostprocessor)
        public const string PendingChangesFile = ".pending_changes.json";
        public const string PendingChangesInProgressFile = ".pending_changes.json.inprogress";
        public const string LastRefreshTimestampFile = ".last_refresh_timestamp";

        // Node ID prefixes for path → nodeId resolution (single source of truth)
        public static readonly string[] NodeIdPrefixesForPathResolution = { "scene_", "asset_", "tool_" };
        public const string DefaultNodeIdPrefix = "asset_";

        public static readonly string[] RequiredNodeDirs = { NodesAssetDir, NodesSceneDir, NodesProjectDir };
    }
}
