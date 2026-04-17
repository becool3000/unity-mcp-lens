using System;
using System.Collections.Generic;
using Becool.UnityMcpLens.Editor.ToolRegistry;

namespace Becool.UnityMcpLens.Editor.Tools.Parameters
{
    /// <summary>
    /// Editor state information data structure.
    /// </summary>
    public record EditorStateData
    {
        /// <summary>
        /// Gets or sets whether the editor is in play mode.
        /// </summary>
        [McpDescription("Whether the editor is in play mode")]
        public bool IsPlaying { get; set; }

        /// <summary>
        /// Gets or sets whether the game is paused.
        /// </summary>
        [McpDescription("Whether the game is paused")]
        public bool IsPaused { get; set; }

        /// <summary>
        /// Gets or sets whether the editor is compiling.
        /// </summary>
        [McpDescription("Whether the editor is compiling")]
        public bool IsCompiling { get; set; }

        /// <summary>
        /// Gets or sets whether the editor is updating.
        /// </summary>
        [McpDescription("Whether the editor is updating")]
        public bool IsUpdating { get; set; }

        /// <summary>
        /// Gets or sets whether the editor is in or transitioning into play mode.
        /// </summary>
        [McpDescription("Whether the editor is in or transitioning into play mode")]
        public bool IsPlayingOrWillChangePlaymode { get; set; }

        /// <summary>
        /// Gets or sets whether Unity is building a player.
        /// </summary>
        [McpDescription("Whether Unity is building a player")]
        public bool IsBuildingPlayer { get; set; }

        /// <summary>
        /// Gets or sets the path to Unity application.
        /// </summary>
        [McpDescription("Path to Unity application")]
        public string ApplicationPath { get; set; }

        /// <summary>
        /// Gets or sets the path to Unity application contents.
        /// </summary>
        [McpDescription("Path to Unity application contents")]
        public string ApplicationContentsPath { get; set; }

        /// <summary>
        /// Gets or sets the time since Unity startup in seconds.
        /// </summary>
        [McpDescription("Time since Unity startup")]
        public double TimeSinceStartup { get; set; }

        /// <summary>
        /// Gets or sets runtime probe data captured from the active play-mode session.
        /// </summary>
        [McpDescription("Runtime probe data captured from the active play-mode session")]
        public PlayModeRuntimeProbeData RuntimeProbe { get; set; }

        [McpDescription("Bridge status reported by the Unity MCP bridge")]
        public string BridgeStatus { get; set; }

        [McpDescription("Bridge status reason when the bridge is not in a simple ready state")]
        public string BridgeReason { get; set; }

        [McpDescription("Whether the bridge expects recovery without user action")]
        public bool BridgeExpectedRecovery { get; set; }

        [McpDescription("Tool discovery mode reported by the bridge")]
        public string ToolDiscoveryMode { get; set; }

        [McpDescription("Current known tool count reported by the bridge")]
        public int ToolCount { get; set; }

        [McpDescription("Current known tools hash reported by the bridge")]
        public string ToolsHash { get; set; }

        [McpDescription("Reason attached to the current tool discovery mode")]
        public string ToolDiscoveryReason { get; set; }

        [McpDescription("UTC timestamp when the current tool snapshot was last recorded")]
        public string ToolSnapshotUtc { get; set; }
    }

    /// <summary>
    /// Runtime probe information captured from play mode.
    /// </summary>
    public record PlayModeRuntimeProbeData
    {
        [McpDescription("Whether a runtime probe is active in play mode")]
        public bool IsAvailable { get; set; }

        [McpDescription("Whether the runtime has advanced beyond the opening frame")]
        public bool HasAdvancedFrames { get; set; }

        [McpDescription("Number of Update calls seen by the runtime probe")]
        public int UpdateCount { get; set; }

        [McpDescription("Number of FixedUpdate calls seen by the runtime probe")]
        public int FixedUpdateCount { get; set; }

        [McpDescription("Current runtime Time.time value")]
        public float RuntimeTime { get; set; }

        [McpDescription("Current runtime Time.unscaledTime value")]
        public float UnscaledTime { get; set; }

        [McpDescription("Current runtime Time.fixedTime value")]
        public float FixedTime { get; set; }

        [McpDescription("Current runtime Time.frameCount value")]
        public int FrameCount { get; set; }

        [McpDescription("Current runtime realtimeSinceStartup value")]
        public double LastRealtimeSinceStartup { get; set; }

        [McpDescription("Active scene name seen by the runtime probe")]
        public string ActiveSceneName { get; set; }
    }

    public record EditorTransitionData
    {
        [McpDescription("State of the requested play transition")]
        public string TransitionState { get; set; }

        [McpDescription("Whether reconnect/disconnect is expected during this transition")]
        public bool ReconnectExpected { get; set; }

        [McpDescription("Whether the editor was already playing before the request")]
        public bool WasAlreadyPlaying { get; set; }

        [McpDescription("Whether a completion wait timed out")]
        public bool WaitTimedOut { get; set; }

        [McpDescription("Current editor state snapshot")]
        public EditorStateData EditorState { get; set; }
    }

    public record EditorStabilityAttemptData
    {
        [McpDescription("UTC timestamp for this sample")]
        public string Timestamp { get; set; }

        [McpDescription("Whether the editor was stable at this sample")]
        public bool IsStable { get; set; }

        [McpDescription("Blocking reasons preventing a stable editor state")]
        public List<string> BlockingReasons { get; set; }

        [McpDescription("Editor state captured for this sample")]
        public EditorStateData EditorState { get; set; }
    }

    public record EditorStabilityResultData
    {
        [McpDescription("Whether the editor reached a stable state")]
        public bool IsStable { get; set; }

        [McpDescription("Whether the wait timed out before stability was observed")]
        public bool TimedOut { get; set; }

        [McpDescription("Milliseconds spent waiting")]
        public int WaitedMilliseconds { get; set; }

        [McpDescription("Number of stable polls reached by the wait")]
        public int StablePollCountReached { get; set; }

        [McpDescription("Blocking reasons from the last sampled state")]
        public List<string> BlockingReasons { get; set; }

        [McpDescription("Sample history captured while waiting")]
        public List<EditorStabilityAttemptData> Attempts { get; set; }

        [McpDescription("Editor state captured at completion")]
        public EditorStateData EditorState { get; set; }
    }


    /// <summary>
    /// Editor window information data structure.
    /// </summary>
    public record EditorWindowInfo
    {
        /// <summary>
        /// Gets or sets the window title.
        /// </summary>
        [McpDescription("Window title")]
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the full type name of the window.
        /// </summary>
        [McpDescription("Full type name of the window")]
        public string TypeName { get; set; }

        /// <summary>
        /// Gets or sets whether the window is currently focused.
        /// </summary>
        [McpDescription("Whether the window is currently focused")]
        public bool IsFocused { get; set; }

        /// <summary>
        /// Gets or sets the window position and size.
        /// </summary>
        [McpDescription("Window position and size")]
        public WindowPosition Position { get; set; }

        /// <summary>
        /// Gets or sets the Unity instance ID of the window.
        /// </summary>
        [McpDescription("Unity instance ID of the window")]
        public int InstanceID { get; set; }
    }

    /// <summary>
    /// Window position and size data structure.
    /// </summary>
    public record WindowPosition
    {
        /// <summary>
        /// Gets or sets the X coordinate of the window.
        /// </summary>
        [McpDescription("X coordinate")]
        public float X { get; set; }

        /// <summary>
        /// Gets or sets the Y coordinate of the window.
        /// </summary>
        [McpDescription("Y coordinate")]
        public float Y { get; set; }

        /// <summary>
        /// Gets or sets the width of the window.
        /// </summary>
        [McpDescription("Width")]
        public float Width { get; set; }

        /// <summary>
        /// Gets or sets the height of the window.
        /// </summary>
        [McpDescription("Height")]
        public float Height { get; set; }
    }

    /// <summary>
    /// Active tool information data structure.
    /// </summary>
    public record ActiveToolData
    {
        /// <summary>
        /// Gets or sets the name of the active tool.
        /// </summary>
        [McpDescription("Name of the active tool")]
        public string ActiveTool { get; set; }

        /// <summary>
        /// Gets or sets whether a custom tool is active.
        /// </summary>
        [McpDescription("Whether a custom tool is active")]
        public bool IsCustom { get; set; }

        /// <summary>
        /// Gets or sets the pivot mode setting.
        /// </summary>
        [McpDescription("Pivot mode setting")]
        public string PivotMode { get; set; }

        /// <summary>
        /// Gets or sets the pivot rotation setting.
        /// </summary>
        [McpDescription("Pivot rotation setting")]
        public string PivotRotation { get; set; }

        /// <summary>
        /// Gets or sets the handle rotation as euler angles.
        /// </summary>
        [McpDescription("Handle rotation as euler angles")]
        public float[] HandleRotation { get; set; }

        /// <summary>
        /// Gets or sets the handle position.
        /// </summary>
        [McpDescription("Handle position")]
        public float[] HandlePosition { get; set; }
    }

    /// <summary>
    /// Selection object information data structure.
    /// </summary>
    public record SelectionObjectInfo
    {
        /// <summary>
        /// Gets or sets the object name.
        /// </summary>
        [McpDescription("Object name")]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the full type name.
        /// </summary>
        [McpDescription("Full type name")]
        public string Type { get; set; }

        /// <summary>
        /// Gets or sets the Unity instance ID.
        /// </summary>
        [McpDescription("Unity instance ID")]
        public int? InstanceID { get; set; }
    }

    /// <summary>
    /// GameObject selection information data structure.
    /// </summary>
    public record GameObjectSelectionInfo
    {
        /// <summary>
        /// Gets or sets the GameObject name.
        /// </summary>
        [McpDescription("GameObject name")]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the Unity instance ID.
        /// </summary>
        [McpDescription("Unity instance ID")]
        public int? InstanceID { get; set; }
    }

    /// <summary>
    /// Selection information data structure.
    /// </summary>
    public record SelectionData
    {
        /// <summary>
        /// Gets or sets the name of active selected object.
        /// </summary>
        [McpDescription("Name of active selected object")]
        public string ActiveObject { get; set; }

        /// <summary>
        /// Gets or sets the name of active selected GameObject.
        /// </summary>
        [McpDescription("Name of active selected GameObject")]
        public string ActiveGameObject { get; set; }

        /// <summary>
        /// Gets or sets the name of active selected Transform.
        /// </summary>
        [McpDescription("Name of active selected Transform")]
        public string ActiveTransform { get; set; }

        /// <summary>
        /// Gets or sets the instance ID of active selection.
        /// </summary>
        [McpDescription("Instance ID of active selection")]
        public int ActiveInstanceID { get; set; }

        /// <summary>
        /// Gets or sets the total count of selected objects.
        /// </summary>
        [McpDescription("Total count of selected objects")]
        public int Count { get; set; }

        /// <summary>
        /// Gets or sets the list of all selected objects.
        /// </summary>
        [McpDescription("List of all selected objects")]
        public List<SelectionObjectInfo> Objects { get; set; }

        /// <summary>
        /// Gets or sets the list of all selected GameObjects.
        /// </summary>
        [McpDescription("List of all selected GameObjects")]
        public List<GameObjectSelectionInfo> GameObjects { get; set; }

        /// <summary>
        /// Gets or sets the asset GUIDs of selected assets in Project view.
        /// </summary>
        [McpDescription("Asset GUIDs of selected assets in Project view")]
        public string[] AssetGUIDs { get; set; }
    }

    /// <summary>
    /// Prefab stage information data structure.
    /// </summary>
    public record PrefabStageData
    {
        /// <summary>
        /// Gets or sets whether prefab stage is currently open.
        /// </summary>
        [McpDescription("Whether prefab stage is currently open")]
        public bool IsOpen { get; set; }

        /// <summary>
        /// Gets or sets the asset path of the prefab being edited.
        /// </summary>
        [McpDescription("Asset path of the prefab being edited")]
        public string AssetPath { get; set; }

        /// <summary>
        /// Gets or sets the name of the prefab root GameObject.
        /// </summary>
        [McpDescription("Name of the prefab root GameObject")]
        public string PrefabRootName { get; set; }

        /// <summary>
        /// Gets or sets the prefab stage mode (InContext or InIsolation).
        /// </summary>
        [McpDescription("Prefab stage mode (InContext or InIsolation)")]
        public string Mode { get; set; }

        /// <summary>
        /// Gets or sets whether the prefab has unsaved changes.
        /// </summary>
        [McpDescription("Whether the prefab has unsaved changes")]
        public bool IsDirty { get; set; }
    }

}
