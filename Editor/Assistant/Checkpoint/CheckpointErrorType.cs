namespace Unity.AI.Assistant.Editor.Checkpoint
{
    enum CheckpointErrorType
    {
        None = 0,
        VcsNotFound,
        VcsExtensionMissing,
        RepositoryCorrupted,
        LockConflict,
        RepositoryMissing,
        PermissionDenied,
        Timeout,
        Cancelled,
        VcsCommandFailed
    }
}
