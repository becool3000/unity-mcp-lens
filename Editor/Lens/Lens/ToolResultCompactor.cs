using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils;

namespace Becool.UnityMcpLens.Editor.Lens
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

        public static object ShapeStructuredPayload(
            string toolName,
            object rawData,
            object compactData,
            object detailRefMeta = null,
            string payloadClass = "structured_tool_result")
        {
            var rawJson = JsonConvert.SerializeObject(rawData, Formatting.None);
            var rawBytes = PayloadBudgeting.GetUtf8ByteCount(rawJson);
            var detailRef = CreateStoredDetailRef(toolName, rawData, rawBytes, detailRefMeta);
            var shaped = ToObject(compactData);
            shaped["detailAvailable"] = detailRef != null;
            if (detailRef != null)
                shaped["detailRef"] = JToken.FromObject(detailRef);
            shaped["rawBytes"] = rawBytes;
            shaped["sha256"] = PayloadBudgeting.ComputeSha256(rawJson);

            string shapedJson = null;
            int shapedBytes = 0;
            for (int i = 0; i < 3; i++)
            {
                shaped["shapedBytes"] = shapedBytes;
                shapedJson = shaped.ToString(Formatting.None);
                int nextBytes = PayloadBudgeting.GetUtf8ByteCount(shapedJson);
                if (nextBytes == shapedBytes)
                    break;
                shapedBytes = nextBytes;
            }

            shaped["shapedBytes"] = shapedBytes;
            shapedJson = shaped.ToString(Formatting.None);
            shapedBytes = PayloadBudgeting.GetUtf8ByteCount(shapedJson);

            PayloadStats.RecordText(
                "tool_result",
                toolName,
                rawJson,
                shapedJson,
                meta: new
                {
                    rawBytes,
                    shapedBytes,
                    compacted = shapedBytes < rawBytes
                },
                options: new PayloadStatOptions
                {
                    EventKind = "tool_result",
                    RepresentationKind = "compact",
                    PayloadClass = payloadClass,
                    Success = true,
                    DetailAvailable = detailRef != null
                });

            return shaped;
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
            var execution = McpToolExecutionScope.Current;
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

        static JObject ToObject(object data)
        {
            if (data == null)
                return new JObject();

            JToken token = data is JToken jToken
                ? jToken.DeepClone()
                : JToken.FromObject(data);

            return token as JObject ?? new JObject { ["value"] = token };
        }
    }
}
