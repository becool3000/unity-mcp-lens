using System;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Data;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Acp
{
    /// <summary>
    /// Message block for ACP tool calls.
    /// Used by AcpProvider to track tool calls in the conversation.
    /// </summary>
    class AcpToolCallBlock : IAssistantMessageBlock
    {
        /// <summary>
        /// The tool call info from the initial tool_call event.
        /// </summary>
        public AcpToolCallInfo CallInfo;

        /// <summary>
        /// The most recent tool_call_update for this tool call, if any.
        /// </summary>
        public AcpToolCallUpdate LatestUpdate;

        /// <summary>
        /// Pending permission request for this tool call, if any.
        /// </summary>
        public AcpPermissionRequest PendingPermission;

        /// <summary>
        /// The user's response to the permission request.
        /// </summary>
        public AcpPermissionOutcome PermissionResponse;

        /// <summary>
        /// Whether this tool call was created during the reasoning phase.
        /// </summary>
        public bool IsReasoning;

        public string ToolCallId => CallInfo?.ToolCallId;

        /// <summary>
        /// Whether this tool call has a pending permission request awaiting user response.
        /// </summary>
        public bool HasPendingPermission => PendingPermission != null && PermissionResponse == null;

        /// <summary>
        /// Converts an AcpToolCallStorageBlock to an AcpToolCallBlock.
        /// This is a shared helper to avoid duplicating conversion logic.
        /// </summary>
        public static AcpToolCallBlock FromStorageBlock(AcpToolCallStorageBlock storageBlock)
        {
            if (storageBlock == null)
                throw new ArgumentNullException(nameof(storageBlock));

            try
            {
                var toolCallJson = storageBlock.ToolCallData;
                if (toolCallJson == null)
                    throw new ArgumentException("ToolCallData is null", nameof(storageBlock));

                var unityData = toolCallJson[AcpToolCallStorageKeys.UnityDataKey] as JObject;

                AcpToolCallInfo callInfo = null;
                AcpToolCallUpdate update = null;
                AcpPermissionRequest pendingPermission = null;
                AcpPermissionOutcome permissionResponse = null;
                bool isReasoning = false;

                if (unityData != null)
                {
                    callInfo = unityData[AcpToolCallStorageKeys.UnityCallInfoKey]?.ToObject<AcpToolCallInfo>();
                    update = unityData[AcpToolCallStorageKeys.UnityLatestUpdateKey]?.ToObject<AcpToolCallUpdate>();
                    pendingPermission = unityData[AcpToolCallStorageKeys.UnityPendingPermissionKey]?.ToObject<AcpPermissionRequest>();
                    permissionResponse = unityData[AcpToolCallStorageKeys.UnityPermissionResponseKey]?.ToObject<AcpPermissionOutcome>();
                    isReasoning = unityData[AcpToolCallStorageKeys.UnityIsReasoningKey]?.Value<bool>() ?? false;
                }

                // Fallback to parsing raw tool call update format (legacy storage).
                callInfo ??= AcpToolCallInfo.FromUpdate(toolCallJson);
                update ??= AcpToolCallUpdate.FromUpdate(toolCallJson);

                if (!isReasoning)
                {
                    var kind = toolCallJson["kind"]?.ToString();
                    isReasoning = kind == "think";
                }

                if (pendingPermission?.RequestId is JValue jValue)
                    pendingPermission.RequestId = jValue.Value;
                else if (pendingPermission?.RequestId is JToken jToken)
                    pendingPermission.RequestId = jToken.ToObject<object>();

                return new AcpToolCallBlock
                {
                    CallInfo = callInfo,
                    LatestUpdate = update,
                    PendingPermission = pendingPermission,
                    PermissionResponse = permissionResponse,
                    IsReasoning = isReasoning
                };
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"ACP: Failed to convert tool call storage block: {ex.Message}");
                throw;
            }
        }
    }
}
