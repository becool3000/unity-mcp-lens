namespace Unity.Relay.Editor.Acp
{
    /// <summary>
    /// Constants for ACP (Agent Client Protocol) providers and protocol methods.
    /// </summary>
    static class AcpConstants
    {
        // Provider IDs
        public const string ProviderId_Unity = "unity";
        public const string ProviderId_ClaudeCode = "claude-code";

        // JSON-RPC Methods (session protocol)
        public const string Method_SessionInitialized = "session/initialized";
        public const string Method_SessionUpdate = "session/update";
        public const string Method_SessionPrompt = "session/prompt";
        public const string Method_SessionCancel = "session/cancel";
        public const string Method_SessionSetMode = "session/set_mode";
        public const string Method_SessionSetModel = "session/set_model";
        public const string Method_RequestPermission = "session/request_permission";

        // Session Update Types
        public const string UpdateType_AgentMessageChunk = "agent_message_chunk";
        public const string UpdateType_AgentThoughtChunk = "agent_thought_chunk";
        public const string UpdateType_CurrentModeUpdate = "current_mode_update";
        public const string UpdateType_ToolCall = "tool_call";
        public const string UpdateType_ToolCallUpdate = "tool_call_update";
        public const string UpdateType_AvailableCommandsUpdate = "available_commands_update";
        public const string UpdateType_ToolResult = "tool_result";
        public const string UpdateType_Plan = "plan";
        public const string UpdateType_AgentMessageStart = "agent_message_start";
        public const string UpdateType_AgentMessageEnd = "agent_message_end";
        public const string UpdateType_FileDiff = "file_diff";

        // Tool Call Status Values
        public const string Status_Pending = "pending";
        public const string Status_Completed = "completed";
        public const string Status_Failed = "failed";

        // Permission Outcome Values
        public const string Outcome_Selected = "selected";
        public const string Outcome_Cancelled = "cancelled";

        // Default provider (fallback when none specified)
        public const string DefaultProviderId = ProviderId_ClaudeCode;

        // Error Codes (from relay credential failures)
        public const string ErrorCode_CredentialAccessFailed = "credential_access_failed";
        public const string ErrorCode_CredentialNotFound = "credential_not_found";
    }
}
