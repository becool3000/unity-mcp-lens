using System;
using Becool.UnityMcpLens.Editor.ToolRegistry;

namespace Becool.UnityMcpLens.Editor.Tools.Parameters
{
    /// <summary>
    /// Actions that can be performed by the Unity.ManageEditor tool.
    /// </summary>
    public enum EditorAction
    {
        /// <summary>
        /// Enter play mode.
        /// </summary>
        Play,

        /// <summary>
        /// Pause play mode.
        /// </summary>
        Pause,

        /// <summary>
        /// Exit play mode.
        /// </summary>
        Stop,

        /// <summary>
        /// Get the current editor state.
        /// </summary>
        GetState,

        /// <summary>
        /// Get a compact editor state summary for frequent readiness checks.
        /// </summary>
        GetCompactState,

        /// <summary>
        /// Wait until the Unity editor reaches a stable idle state.
        /// </summary>
        WaitForStableEditor,

        /// <summary>
        /// Get the project root directory path.
        /// </summary>
        GetProjectRoot,

        /// <summary>
        /// Get information about open editor windows.
        /// </summary>
        GetWindows,

        /// <summary>
        /// Get the currently active editor tool.
        /// </summary>
        GetActiveTool,

        /// <summary>
        /// Get the current selection in the editor.
        /// </summary>
        GetSelection,

        /// <summary>
        /// Get prefab stage information if a prefab is open for editing.
        /// </summary>
        GetPrefabStage,

        /// <summary>
        /// Set the active editor tool.
        /// </summary>
        SetActiveTool,

        /// <summary>
        /// Add a new tag to the project.
        /// </summary>
        AddTag,

        /// <summary>
        /// Remove a tag from the project.
        /// </summary>
        RemoveTag,

        /// <summary>
        /// Get all tags in the project.
        /// </summary>
        GetTags,

        /// <summary>
        /// Add a new layer to the project.
        /// </summary>
        AddLayer,

        /// <summary>
        /// Remove a layer from the project.
        /// </summary>
        RemoveLayer,

        /// <summary>
        /// Get all layers in the project.
        /// </summary>
        GetLayers
    }

    /// <summary>
    /// Parameters for the Unity.ManageEditor tool.
    /// </summary>
    public record ManageEditorParams
    {
        /// <summary>
        /// Gets or sets the operation to perform.
        /// </summary>
        [McpDescription("Operation to perform", Required = true, Default = EditorAction.GetState)]
        public EditorAction Action { get; set; } = EditorAction.GetState;

        /// <summary>
        /// Gets or sets whether to wait for certain actions to complete.
        /// </summary>
        [McpDescription("If true, waits for certain actions", Required = false)]
        public bool? WaitForCompletion { get; set; }

        /// <summary>
        /// Gets or sets the timeout for wait-based actions in milliseconds.
        /// </summary>
        [McpDescription("Timeout in milliseconds for wait-based actions", Required = false)]
        public int? TimeoutMs { get; set; }

        /// <summary>
        /// Gets or sets the polling interval for wait-based actions in milliseconds.
        /// </summary>
        [McpDescription("Polling interval in milliseconds for wait-based actions", Required = false)]
        public int? PollIntervalMs { get; set; }

        /// <summary>
        /// Gets or sets the number of consecutive stable polls required for WaitForStableEditor.
        /// </summary>
        [McpDescription("Consecutive stable polls required for WaitForStableEditor", Required = false)]
        public int? StablePollCount { get; set; }

        /// <summary>
        /// Gets or sets the post-stability settle delay for WaitForStableEditor.
        /// </summary>
        [McpDescription("Additional delay in milliseconds after stable polls are reached", Required = false)]
        public int? PostStableDelayMs { get; set; }

        /// <summary>
        /// Gets or sets the tool name for the set_active_tool action.
        /// </summary>
        [McpDescription("Tool name for set_active_tool action", Required = false)]
        public string ToolName { get; set; }

        /// <summary>
        /// Gets or sets the tag name for add_tag/remove_tag actions.
        /// </summary>
        [McpDescription("Tag name for add_tag/remove_tag actions", Required = false)]
        public string TagName { get; set; }

        /// <summary>
        /// Gets or sets the layer name for add_layer/remove_layer actions.
        /// </summary>
        [McpDescription("Layer name for add_layer/remove_layer actions", Required = false)]
        public string LayerName { get; set; }
    }
}
