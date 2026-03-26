using System;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Editor.Acp;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Data.MessageBlocks
{
    /// <summary>
    /// Message block model for ACP (Agent Client Protocol) tool calls.
    /// Holds the tool call information and can be updated as the tool call progresses.
    /// </summary>
    class AcpToolCallBlockModel : IMessageBlockModel, IEquatable<AcpToolCallBlockModel>
    {
        static readonly JTokenEqualityComparer s_JTokenComparer = new();

        /// <summary>
        /// The tool call info, updated on each tool_call event for this tool call ID.
        /// </summary>
        public AcpToolCallInfo CallInfo;

        /// <summary>
        /// The most recent tool_call_update for this tool call, if any.
        /// Contains status changes, tool responses, and result content.
        /// </summary>
        public AcpToolCallUpdate LatestUpdate;

        /// <summary>
        /// Pending permission request for this tool call, if any.
        /// Set when the ACP agent requests permission to execute this tool.
        /// </summary>
        public AcpPermissionRequest PendingPermission;

        /// <summary>
        /// The user's response to the permission request.
        /// Null until the user responds to the permission prompt.
        /// </summary>
        public AcpPermissionOutcome PermissionResponse;

        /// <summary>
        /// Whether this tool call was created during the reasoning phase.
        /// </summary>
        public bool IsReasoning;

        /// <summary>
        /// The full rawInput JObject, preserved for auto-approved tool calls that need to display file content.
        /// </summary>
        public JObject RawInput;

        public string ToolCallId => CallInfo?.ToolCallId;

        /// <summary>
        /// Whether this tool call has a pending permission request awaiting user response.
        /// </summary>
        public bool HasPendingPermission => PendingPermission != null && PermissionResponse == null;

        public bool Equals(AcpToolCallBlockModel other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(CallInfo, other.CallInfo)
                && Equals(LatestUpdate, other.LatestUpdate)
                && Equals(PendingPermission, other.PendingPermission)
                && Equals(PermissionResponse, other.PermissionResponse)
                && IsReasoning == other.IsReasoning
                && JToken.DeepEquals(RawInput, other.RawInput);
        }

        public override bool Equals(object obj) => obj is AcpToolCallBlockModel other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(CallInfo, LatestUpdate, PendingPermission, PermissionResponse, IsReasoning,
            RawInput != null ? s_JTokenComparer.GetHashCode(RawInput) : 0);
    }
}
