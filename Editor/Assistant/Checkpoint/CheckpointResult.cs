namespace Unity.AI.Assistant.Editor.Checkpoint
{
    readonly struct CheckpointResult<T>
    {
        public bool Success { get; }
        public T Value { get; }
        public CheckpointErrorType ErrorType { get; }
        public string PublicMessage { get; }
        public string InternalMessage { get; }
        public bool IsRecoverable { get; }

        CheckpointResult(bool success, T value, CheckpointErrorType errorType, string publicMessage, string internalMessage, bool isRecoverable)
        {
            Success = success;
            Value = value;
            ErrorType = errorType;
            PublicMessage = publicMessage ?? string.Empty;
            InternalMessage = internalMessage ?? string.Empty;
            IsRecoverable = isRecoverable;
        }

        public static CheckpointResult<T> Ok(T value, string message = null)
            => new(true, value, CheckpointErrorType.None, message, null, false);

        public static CheckpointResult<T> Fail(CheckpointErrorType errorType, string publicMessage, string internalMessage = null, bool isRecoverable = false)
            => new(false, default, errorType, publicMessage, internalMessage ?? publicMessage, isRecoverable);

        public static implicit operator bool(CheckpointResult<T> result) => result.Success;
    }
}
