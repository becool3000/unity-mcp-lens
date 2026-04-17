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

        [McpDescription("Optional warmup delay in milliseconds before capture", Required = false)]
        public int WarmupMs { get; set; } = 0;

        [McpDescription("Pause play mode before capture when Unity is already playing", Required = false)]
        public bool PausePlayMode { get; set; } = false;

        [McpDescription("Advance this many paused play-mode frames before capture", Required = false)]
        public int StepFrames { get; set; } = 0;

        [McpDescription("Timeout in milliseconds while waiting for the PNG to appear on disk", Required = false)]
        public int WaitForFileTimeoutMs { get; set; } = 4000;
    }
}
