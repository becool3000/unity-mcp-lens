namespace Unity.AI.Assistant.Editor.Checkpoint
{
    readonly struct VcsCommitInfo
    {
        public string Hash { get; }
        public string Message { get; }
        public long TimestampUnixSeconds { get; }

        public VcsCommitInfo(string hash, string message, long timestampUnixSeconds)
        {
            Hash = hash;
            Message = message;
            TimestampUnixSeconds = timestampUnixSeconds;
        }
    }
}
