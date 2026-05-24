using System;
using Becool.UnityMcpLens.Editor.ToolRegistry;

namespace Becool.UnityMcpLens.Editor.Tools.Parameters
{
    /// <summary>
    /// Parameters for the CaptureGameView tool.
    /// </summary>
    public record CaptureGameViewParams
    {
        /// <summary>
        /// Optional name of the current scene (for confirmation/logging purposes only)
        /// </summary>
        [McpDescription("Name of the current scene (for confirmation/logging only)", Required = false)]
        public string SceneName { get; set; }

        [McpDescription("Relative output path under the Unity project (for example Temp/UiCapture/shot.png)", Required = true)]
        public string OutputPath { get; set; }

        [McpDescription("Optional fixed Game view capture width in pixels. Supply with Height for exact-resolution UI review.", Required = false)]
        public int Width { get; set; } = 0;

        [McpDescription("Optional fixed Game view capture height in pixels. Supply with Width for exact-resolution UI review.", Required = false)]
        public int Height { get; set; } = 0;

        [McpDescription("Restore the original selected Game view resolution after capture when Width/Height changed it", Required = false)]
        public bool RestoreOriginalResolution { get; set; } = true;

        [McpDescription("Optional warmup delay in milliseconds before capture", Required = false)]
        public int WarmupMs { get; set; } = 0;

        [McpDescription("Approximate rendered/runtime frames to wait or step before capture", Required = false)]
        public int WarmupFrames { get; set; } = 0;

        [McpDescription("Pause play mode before capture when Unity is already playing", Required = false)]
        public bool PausePlayMode { get; set; } = false;

        [McpDescription("Advance this many paused play-mode frames before capture", Required = false)]
        public int StepFrames { get; set; } = 0;

        [McpDescription("Restore the original pause state after capture when PausePlayMode changed it", Required = false)]
        public bool RestorePauseState { get; set; } = true;

        [McpDescription("Require Unity to be in Play Mode before capturing", Required = false)]
        public bool RequirePlaying { get; set; } = false;

        [McpDescription("Capture console error-count delta around capture", Required = false)]
        public bool CaptureConsoleDelta { get; set; } = true;

        [McpDescription("If Game view capture times out, try a camera/scene-view fallback capture to the same path", Required = false)]
        public bool FallbackSceneView { get; set; } = false;

        [McpDescription("Temporarily set UI objects active/inactive for review, then restore their original activeSelf values", Required = false)]
        public TemporaryUiActivationParams[] TemporaryActivations { get; set; } = Array.Empty<TemporaryUiActivationParams>();

        [McpDescription("Verify captured PNG dimensions against requested Width/Height when provided", Required = false)]
        public bool VerifyImageDimensions { get; set; } = true;

        [McpDescription("Timeout in milliseconds while waiting for the PNG to appear on disk", Required = false)]
        public int WaitForFileTimeoutMs { get; set; } = 4000;
    }

    public record TemporaryUiActivationParams
    {
        [McpDescription("Target UI GameObject path, name, or id.", Required = true)]
        public string Target { get; set; }

        [McpDescription("How to find the target: by_name, by_id, or by_path. Defaults to by_name.", Required = false)]
        public string SearchMethod { get; set; } = "by_name";

        [McpDescription("Include inactive UI objects while resolving the target. Defaults to true for review overlays.", Required = false)]
        public bool IncludeInactive { get; set; } = true;

        [McpDescription("Temporary activeSelf value to apply before capture. Defaults to true.", Required = false)]
        public bool Active { get; set; } = true;
    }
}
