namespace Unity.AI.Assistant.Editor.Acp
{
    /// <summary>
    /// Request for MCP tool approval.
    /// </summary>
    record McpToolApprovalRequest(
        string SessionId,
        string Provider,
        string ToolName,
        string ToolArgs,
        string ToolCallId);

    /// <summary>
    /// Response for MCP tool approval.
    /// </summary>
    record McpToolApprovalResponse(
        bool Approved,
        string Reason = null,
        bool AlwaysAllow = false);
}
