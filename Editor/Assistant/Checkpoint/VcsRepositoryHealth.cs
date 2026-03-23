namespace Unity.AI.Assistant.Editor.Checkpoint
{
    readonly struct VcsRepositoryHealth
    {
        public VcsRepositoryHealthStatus Status { get; }
        public string Message { get; }
        public string LockFilePath { get; }

        public bool IsHealthy => Status == VcsRepositoryHealthStatus.Healthy;

        VcsRepositoryHealth(VcsRepositoryHealthStatus status, string message, string lockFilePath = null)
        {
            Status = status;
            Message = message ?? string.Empty;
            LockFilePath = lockFilePath;
        }

        public static VcsRepositoryHealth Healthy()
            => new(VcsRepositoryHealthStatus.Healthy, "Repository is healthy");

        public static VcsRepositoryHealth Missing()
            => new(VcsRepositoryHealthStatus.Missing, "Repository does not exist");

        public static VcsRepositoryHealth Corrupted(string message = null)
            => new(VcsRepositoryHealthStatus.Corrupted, message ?? "Repository is corrupted");

        public static VcsRepositoryHealth Locked(string lockPath)
            => new(VcsRepositoryHealthStatus.Locked, "Repository has a stale lock file", lockPath);
    }
}
