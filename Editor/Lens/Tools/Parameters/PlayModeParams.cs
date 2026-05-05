using Becool.UnityMcpLens.Editor.ToolRegistry;

namespace Becool.UnityMcpLens.Editor.Tools.Parameters
{
    public record ExitPlayModeParams
    {
        [McpDescription("Wait until the editor leaves play mode and reaches a stable state.", Required = false)]
        public bool WaitForStableEditor { get; set; } = true;

        [McpDescription("Timeout in milliseconds while waiting for play-mode exit and editor stability.", Required = false)]
        public int TimeoutMs { get; set; } = 30000;

        [McpDescription("Polling interval in milliseconds for wait-based exit checks.", Required = false)]
        public int PollIntervalMs { get; set; } = 250;

        [McpDescription("Consecutive stable polls required before reporting a stable editor.", Required = false)]
        public int StablePollCount { get; set; } = 2;

        [McpDescription("Additional settle delay in milliseconds after stable polls are reached.", Required = false)]
        public int PostStableDelayMs { get; set; } = 250;

        [McpDescription("Clear EditorApplication.isPaused before requesting play-mode exit.", Required = false)]
        public bool UnpauseBeforeExit { get; set; } = true;
    }
}
