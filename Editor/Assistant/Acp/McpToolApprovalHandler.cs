using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.Relay.Editor;
using Unity.Relay.Editor.Acp;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Acp
{
    /// <summary>
    /// Handles MCP tool approval requests for agents that don't send native request_permission (e.g., Codex).
    /// This is a workaround for Codex which doesn't use the standard ACP permission flow.
    ///
    /// The flow:
    /// 1. MCP server calls mcp/request_tool_approval via Bridge
    /// 2. Bridge calls McpToolApprovalHandler.RequestApprovalAsync
    /// 3. This handler shows permission UI via AcpSession.FireMcpPermissionRequest
    /// 4. User responds, AcpProvider calls Complete
    /// 5. Response is returned to MCP server
    /// </summary>
    static class McpToolApprovalHandler
    {
        // Pending approval requests waiting for user response
        static readonly ConcurrentDictionary<string, TaskCompletionSource<McpToolApprovalResponse>> s_PendingApprovals = new();

        /// <summary>
        /// Request approval for an MCP tool call.
        /// Routes to the appropriate AcpSession for permission UI, or auto-approves if no session found.
        /// </summary>
        public static async Task<McpToolApprovalResponse> RequestApprovalAsync(
            McpToolApprovalRequest request)
        {
            // Find the AcpSession by sessionId
            var session = FindSessionByChannelId(request.SessionId);
            if (session == null)
            {
                return new McpToolApprovalResponse(true, "No active session (auto-approved)");
            }

            // Auto-approve in full-auto mode
            var mode = session.CurrentModeId;
            if (mode == "full-auto")
            {
                return new McpToolApprovalResponse(true, "Auto-approved (full-auto mode)");
            }

            // For other modes, show permission UI
            var tcs = new TaskCompletionSource<McpToolApprovalResponse>();
            s_PendingApprovals[request.ToolCallId] = tcs;

            try
            {
                // Parse tool arguments
                var args = JObject.Parse(request.ToolArgs ?? "{}");

                // Calculate cost for tools that support it (e.g., asset generation)
                var cost = await AcpToolCostCalculator.TryGetCostAsync(request.ToolName, args);

                // Create an ACP permission request for the MCP tool
                var permissionRequest = new AcpPermissionRequest
                {
                    RequestId = request.ToolCallId,
                    ToolCall = new AcpToolCall
                    {
                        ToolCallId = request.ToolCallId,
                        ToolName = request.ToolName,
                        Title = $"MCP Tool: {request.ToolName}",
                        RawInput = args,
                        Cost = cost
                    },
                    Options = new[]
                    {
                        new AcpPermissionOption { OptionId = AcpPermissionMapping.AllowOnceKind, Name = "Allow", Kind = AcpPermissionMapping.AllowOnceKind },
                        new AcpPermissionOption { OptionId = AcpPermissionMapping.AllowAlwaysKind, Name = "Always Allow", Kind = AcpPermissionMapping.AllowAlwaysKind },
                        new AcpPermissionOption { OptionId = AcpPermissionMapping.RejectOnceKind, Name = "Reject", Kind = AcpPermissionMapping.RejectOnceKind }
                    }
                };

                // Fire the permission request event on the session
                session.FireMcpPermissionRequest(permissionRequest);

                // Wait for user response with timeout
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5));
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    Debug.LogWarning($"[McpToolApprovalHandler] Timeout waiting for approval: {request.ToolName}");
                    return new McpToolApprovalResponse(false, "Approval timed out");
                }

                return await tcs.Task;
            }
            finally
            {
                s_PendingApprovals.TryRemove(request.ToolCallId, out _);
            }
        }

        /// <summary>
        /// Complete a pending MCP tool approval request.
        /// Called by AcpProvider when user responds to permission request.
        /// </summary>
        public static void Complete(string toolCallId, bool approved, string reason = null, bool alwaysAllow = false)
        {
            if (s_PendingApprovals.TryRemove(toolCallId, out var tcs))
            {
                tcs.TrySetResult(new McpToolApprovalResponse(approved, reason, alwaysAllow));
            }
        }

        /// <summary>
        /// Check if a tool call ID is a pending MCP approval.
        /// </summary>
        public static bool IsPending(string toolCallId)
        {
            return s_PendingApprovals.ContainsKey(toolCallId);
        }

        /// <summary>
        /// Find an AcpSession by its channel ID (sessionId from the relay).
        /// </summary>
        static AcpSession FindSessionByChannelId(string channelId)
        {
            if (string.IsNullOrEmpty(channelId)) return null;

            foreach (var sessionId in AcpSessionRegistry.ActiveSessionIds)
            {
                if (sessionId.Value == channelId)
                {
                    return AcpSessionRegistry.Get(sessionId);
                }
            }

            return null;
        }
    }
}
