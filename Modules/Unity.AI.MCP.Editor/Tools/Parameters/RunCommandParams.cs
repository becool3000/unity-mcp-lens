using Unity.AI.MCP.Editor.ToolRegistry;

namespace Unity.AI.MCP.Editor.Tools.Parameters
{
    /// <summary>
    /// Parameters for the Unity.RunCommand tool.
    /// </summary>
    public record RunCommandParams
    {
        /// <summary>
        /// Gets or sets whether the command should only be validated or compiled and executed.
        /// </summary>
        [McpDescription("Execution mode: 'execute' compiles and runs the command; 'validate' compiles only and does not execute.", Required = false)]
        public string Mode { get; set; } = "execute";

        /// <summary>
        /// Gets or sets the C# script code to compile and execute.
        /// </summary>
        [McpDescription("The C# script code to compile and execute. Should implement IRunCommand interface or be a valid C# script.", Required = true)]
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets an optional title for the execution command.
        /// </summary>
        [McpDescription("Optional title for the execution command", Required = false)]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets whether play mode should be paused before execution when Unity is currently playing.
        /// </summary>
        [McpDescription("Pause play mode before executing the command when Unity is currently playing.", Required = false)]
        public bool PausePlayMode { get; set; }

        /// <summary>
        /// Gets or sets the number of paused play-mode frames to advance before execution.
        /// </summary>
        [McpDescription("Advance this many paused play-mode frames before executing the command.", Required = false)]
        public int StepFrames { get; set; }

        /// <summary>
        /// Gets or sets whether the prior play-mode pause state should be restored after execution.
        /// </summary>
        [McpDescription("Restore the prior play-mode pause state after executing the command.", Required = false)]
        public bool RestorePauseState { get; set; } = true;
    }
}
