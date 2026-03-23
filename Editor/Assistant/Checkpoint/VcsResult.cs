namespace Unity.AI.Assistant.Editor.Checkpoint
{
    readonly struct VcsResult
    {
        public bool Success { get; }
        public string Output { get; }
        public string Error { get; }
        public int ExitCode { get; }

        VcsResult(bool success, string output, string error, int exitCode)
        {
            Success = success;
            Output = output;
            Error = error;
            ExitCode = exitCode;
        }

        public static VcsResult Ok(string output = "") => new(true, output, string.Empty, 0);
        public static VcsResult Fail(string error, int exitCode = 1) => new(false, string.Empty, error, exitCode);
        public static VcsResult FromCommand(bool success, string output, string error, int exitCode) => new(success, output, error, exitCode);
    }
}
