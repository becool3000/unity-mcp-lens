using System;
using Unity.AI.MCP.Editor.ToolRegistry;

namespace Unity.AI.MCP.Editor.Tools.Parameters
{
    /// <summary>
    /// Actions that can be performed by the Unity.ManageScene tool.
    /// </summary>
    public enum SceneAction
    {
        /// <summary>
        /// Create a new scene.
        /// </summary>
        Create,

        /// <summary>
        /// Load an existing scene.
        /// </summary>
        Load,

        /// <summary>
        /// Save the current scene.
        /// </summary>
        Save,

        /// <summary>
        /// Get the hierarchy of GameObjects in the scene.
        /// </summary>
        GetHierarchy,

        /// <summary>
        /// Get information about the active scene.
        /// </summary>
        GetActive,

        /// <summary>
        /// Get the build settings for scenes.
        /// </summary>
        GetBuildSettings
    }

    /// <summary>
    /// Parameters for the Unity.ManageScene tool.
    /// </summary>
    public record ManageSceneParams
    {
        /// <summary>
        /// Gets or sets the operation to perform.
        /// </summary>
        [McpDescription("Operation to perform", Required = true, Default = SceneAction.GetActive)]
        public SceneAction Action { get; set; } = SceneAction.GetActive;

        /// <summary>
        /// Gets or sets the scene name.
        /// </summary>
        [McpDescription("Scene name", Required = false)]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the relative path under Assets/.
        /// </summary>
        [McpDescription("Relative path under Assets/", Required = false)]
        public string Path { get; set; }

        /// <summary>
        /// Gets or sets the build index for the scene.
        /// </summary>
        [McpDescription("Build index for scene", Required = false)]
        public int? BuildIndex { get; set; }

        /// <summary>
        /// Gets or sets the hierarchy depth limit (-1 for full hierarchy, 0 for root objects only, 1+ for limited depth).
        /// </summary>
        [McpDescription("Hierarchy depth limit (-1 for full hierarchy, 0 for root objects only, 1+ for limited depth)", Required = false, Default = -1)]
        public int? Depth { get; set; } = -1;
    }
}
