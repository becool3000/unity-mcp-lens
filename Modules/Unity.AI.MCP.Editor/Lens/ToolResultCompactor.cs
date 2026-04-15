using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.MCP.Editor.Lens
{
    static class ToolResultCompactor
    {
        public static BudgetedToolResult ShapeTextPayload(
            string toolName,
            string summary,
            string text,
            object detailRefMeta = null,
            int maxPreviewLines = PayloadBudgetPolicy.MaxPreviewFileLines,
            int maxPreviewBytes = PayloadBudgetPolicy.MaxToolResultBytes,
            bool requireDetailRefForCompaction = false)
        {
            text ??= string.Empty;

            var rawBytes = PayloadBudgeting.GetUtf8ByteCount(text);
            var detailRef = rawBytes > maxPreviewBytes
                ? CreateStoredDetailRef(toolName, text, rawBytes, detailRefMeta)
                : null;

            if (rawBytes > maxPreviewBytes && requireDetailRefForCompaction && detailRef == null)
            {
                var fullResult = PayloadBudgeting.CreateTextResult(
                    summary,
                    new { rawBytes },
                    text,
                    detailRef: null,
                    maxPreviewLines: int.MaxValue,
                    maxPreviewBytes: int.MaxValue);

                PayloadStats.Record(
                    "tool_result",
                    toolName,
                    rawBytes,
                    rawBytes,
                    PayloadBudgeting.EstimateTokensFromBytes(rawBytes),
                    fullResult.Sha256);
                return fullResult;
            }

            var budgeted = PayloadBudgeting.CreateTextResult(
                summary,
                new { rawBytes },
                text,
                detailRef,
                maxPreviewLines: maxPreviewLines,
                maxPreviewBytes: maxPreviewBytes);

            var previewBytes = PayloadBudgeting.GetUtf8ByteCount(budgeted.Preview);
            PayloadStats.Record(
                "tool_result",
                toolName,
                rawBytes,
                previewBytes,
                PayloadBudgeting.EstimateTokensFromBytes(previewBytes),
                budgeted.Sha256);
            return budgeted;
        }

        public static object ShapeJsonPayload(
            string toolName,
            string summary,
            object data,
            object legacyDetailRef = null,
            int maxPreviewLines = 40,
            int maxPreviewBytes = PayloadBudgetPolicy.MaxToolResultBytes)
        {
            var serialized = Newtonsoft.Json.JsonConvert.SerializeObject(data, Newtonsoft.Json.Formatting.None);
            var rawBytes = PayloadBudgeting.GetUtf8ByteCount(serialized);
            if (rawBytes <= PayloadBudgetPolicy.MaxToolResultBytes)
            {
                PayloadStats.Record(
                    "tool_result",
                    toolName,
                    rawBytes,
                    rawBytes,
                    PayloadBudgeting.EstimateTokensFromBytes(rawBytes),
                    PayloadBudgeting.ComputeSha256(serialized));
                return data;
            }

            var detailRef = CreateDetailRef(toolName, data, rawBytes, legacyDetailRef);
            var budgeted = PayloadBudgeting.CreateTextResult(
                summary,
                new { rawBytes },
                serialized,
                detailRef,
                maxPreviewLines: maxPreviewLines,
                maxPreviewBytes: maxPreviewBytes);

            var previewBytes = PayloadBudgeting.GetUtf8ByteCount(budgeted.Preview);
            PayloadStats.Record(
                "tool_result",
                toolName,
                rawBytes,
                previewBytes,
                PayloadBudgeting.EstimateTokensFromBytes(previewBytes),
                budgeted.Sha256);
            return budgeted;
        }

        static object CreateDetailRef(string toolName, object data, int rawBytes, object legacyDetailRef)
        {
            var storedDetailRef = CreateStoredDetailRef(toolName, data, rawBytes, legacyDetailRef);
            return storedDetailRef ?? legacyDetailRef;
        }

        public static object CreateStoredDetailRef(string toolName, object data, int rawBytes, object meta = null)
        {
            var execution = ExternalToolExecutionScope.Current;
            if (string.IsNullOrWhiteSpace(execution?.ConnectionId) ||
                !BridgeLensSessionRegistry.TryGetConnectionState(execution.ConnectionId, out var state) ||
                state?.Capabilities?.SupportsToolSyncLens != true)
            {
                return null;
            }

            string refId = ToolDetailRefStore.Store(
                execution.ConnectionId,
                new
                {
                    tool = toolName,
                    bytes = rawBytes,
                    data
                },
                meta: meta);

            return new
            {
                refId,
                tool = toolName,
                bytes = rawBytes
            };
        }
    }
}
