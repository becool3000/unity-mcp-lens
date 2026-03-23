namespace Unity.AI.Assistant.Editor.Acp
{
    /// <summary>
    /// Keys used to store Unity-specific ACP tool call metadata inside persisted tool call JSON.
    /// </summary>
    static class AcpToolCallStorageKeys
    {
        public const string UnityDataKey = "_unity";
        public const string UnityCallInfoKey = "callInfo";
        public const string UnityLatestUpdateKey = "latestUpdate";
        public const string UnityPendingPermissionKey = "pendingPermission";
        public const string UnityPermissionResponseKey = "permissionResponse";
        public const string UnityIsReasoningKey = "isReasoning";
    }
}
