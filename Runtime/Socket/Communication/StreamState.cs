namespace Unity.AI.Assistant.Socket.Communication
{
    enum StreamState
    {
        /// <summary>
        /// This means the stream handler has been created but the stream has not started
        /// </summary>
        Waiting,
        /// <summary>
        /// This means that the stream is currently in progress
        /// </summary>
        InProgress,
        /// <summary>
        /// This means that the stream has finished streaming or an error has occurred
        /// </summary>
        Completed
    }
}
