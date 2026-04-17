using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Unity.AI.MCP.Editor.Utils
{
    static class PayloadBudgetPolicy
    {
        public const int MaxToolResultBytes = 8192;
        public const int MaxIssuesBytes = 8192;
        public const int MaxSceneQueryBytes = 4096;
        public const int MaxChangedFilesBytes = 12288;
        public const int MaxPreviewFileBytes = 8192;
        public const int MaxPreviewFileLines = 80;
        public const int MaxInlineSnippetLines = 40;
        public const int MaxProjectDataChars = 32000;
        public const int MaxProjectDataItems = 300;
        public const int MaxUiLayoutEntries = 50;
        public const int MaxDiagnosticFindings = 20;
        public const int MaxPersistedBlockBytes = 16384;
        public const int MaxPersistedPreviewBytes = 8192;
        public const double ConversationSaveDebounceSeconds = 0.35d;
        public const double HeartbeatWriteIntervalSeconds = 5d;
    }

    [Serializable]
    class BudgetedToolResult
    {
        [JsonProperty("summary")] public string Summary { get; set; }
        [JsonProperty("meta")] public object Meta { get; set; }
        [JsonProperty("preview")] public string Preview { get; set; }
        [JsonProperty("detailAvailable")] public bool DetailAvailable { get; set; }
        [JsonProperty("detailRef")] public object DetailRef { get; set; }
        [JsonProperty("truncated")] public bool Truncated { get; set; }
        [JsonProperty("sha256")] public string Sha256 { get; set; }
        [JsonProperty("bytes")] public int Bytes { get; set; }
    }

    sealed class PayloadStatScope
    {
        public string RunId { get; }
        public string TaskId { get; }
        public string OperationId { get; }
        public string ConversationId { get; }
        public string ConnectionId { get; }
        public string RequestId { get; }
        public string ProviderId { get; }
        public string WorkflowKind { get; }
        public string Origin { get; }

        public PayloadStatScope(
            string runId = null,
            string taskId = null,
            string operationId = null,
            string conversationId = null,
            string connectionId = null,
            string requestId = null,
            string providerId = null,
            string workflowKind = null,
            string origin = null)
        {
            RunId = runId;
            TaskId = taskId;
            OperationId = operationId;
            ConversationId = conversationId;
            ConnectionId = connectionId;
            RequestId = requestId;
            ProviderId = providerId;
            WorkflowKind = workflowKind;
            Origin = origin;
        }

        public PayloadStatScope Merge(PayloadStatScope child)
        {
            if (child == null)
                return this;

            return new PayloadStatScope(
                string.IsNullOrWhiteSpace(child.RunId) ? RunId : child.RunId,
                string.IsNullOrWhiteSpace(child.TaskId) ? TaskId : child.TaskId,
                string.IsNullOrWhiteSpace(child.OperationId) ? OperationId : child.OperationId,
                string.IsNullOrWhiteSpace(child.ConversationId) ? ConversationId : child.ConversationId,
                string.IsNullOrWhiteSpace(child.ConnectionId) ? ConnectionId : child.ConnectionId,
                string.IsNullOrWhiteSpace(child.RequestId) ? RequestId : child.RequestId,
                string.IsNullOrWhiteSpace(child.ProviderId) ? ProviderId : child.ProviderId,
                string.IsNullOrWhiteSpace(child.WorkflowKind) ? WorkflowKind : child.WorkflowKind,
                string.IsNullOrWhiteSpace(child.Origin) ? Origin : child.Origin);
        }
    }

    sealed class PayloadStatOptions
    {
        public string SchemaVersion { get; set; }
        public string EventKind { get; set; }
        public string RunId { get; set; }
        public string TaskId { get; set; }
        public string OperationId { get; set; }
        public string ConversationId { get; set; }
        public string ConnectionId { get; set; }
        public string RequestId { get; set; }
        public string ProviderId { get; set; }
        public string WorkflowKind { get; set; }
        public string Origin { get; set; }
        public int? RawChars { get; set; }
        public int? ShapedChars { get; set; }
        public int? DurationMs { get; set; }
        public bool? Success { get; set; }
        public string ErrorKind { get; set; }
        public string ErrorMessageShort { get; set; }
        public string RepresentationKind { get; set; }
        public string StableKey { get; set; }
        public string NormalizedHash { get; set; }
        public string DuplicateClass { get; set; }
        public string PayloadClass { get; set; }
        public string DynamicFieldFlags { get; set; }
        public bool? DetailAvailable { get; set; }
        public bool? Cached { get; set; }
        public bool? Unchanged { get; set; }
        public string CacheReuseClass { get; set; }
        public object ExtraFields { get; set; }
    }

    static class PayloadBudgeting
    {
        static readonly UTF8Encoding k_Utf8 = new(false);
        static readonly Regex k_GuidRegex = new(@"(?i)\b[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        static readonly Regex k_IsoTimestampRegex = new(@"\b\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        const int k_MaxStructuredNormalizationBytes = 262144;

        public static int GetUtf8ByteCount(string text) => string.IsNullOrEmpty(text) ? 0 : k_Utf8.GetByteCount(text);
        public static int EstimateTokensFromBytes(int byteCount) => byteCount <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(byteCount / 4.0d));
        public static string CreateCorrelationId() => Guid.NewGuid().ToString("N");

        public static string ComputeSha256(string text) => ComputeSha256(string.IsNullOrEmpty(text) ? Array.Empty<byte>() : k_Utf8.GetBytes(text));

        public static string ComputeSha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes ?? Array.Empty<byte>())).Replace("-", "").ToLowerInvariant();
        }

        public static string ComputeNormalizedSha256(string text, out string dynamicFieldFlags)
        {
            return ComputeSha256(NormalizeTextForComparison(text, out dynamicFieldFlags));
        }

        public static string NormalizeTextForComparison(string text, out string dynamicFieldFlags)
        {
            var flags = new SortedSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text))
            {
                dynamicFieldFlags = string.Empty;
                return string.Empty;
            }

            var trimmed = text.TrimStart();
            string normalized;
            if (GetUtf8ByteCount(text) <= k_MaxStructuredNormalizationBytes &&
                trimmed.Length > 0 &&
                (trimmed[0] == '{' || trimmed[0] == '[') &&
                TryNormalizeJson(text, flags, out var normalizedJson))
            {
                normalized = normalizedJson;
            }
            else
            {
                normalized = NormalizeScalarString(text, flags);
            }

            dynamicFieldFlags = string.Join(",", flags);
            return normalized;
        }

        public static string CreateTextPreview(string text, int maxLines, int maxBytes, out bool truncated)
        {
            truncated = false;
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var normalized = text.Replace("\r\n", "\n");
            var lines = normalized.Split('\n');
            var preview = normalized;
            if (maxLines > 0 && lines.Length > maxLines)
            {
                preview = string.Join("\n", lines, 0, maxLines);
                truncated = true;
            }

            if (maxBytes > 0)
                preview = TruncateToUtf8Bytes(preview, maxBytes, ref truncated);

            return truncated ? preview.TrimEnd() + "\n... [truncated]" : preview;
        }

        public static string TruncateForStorage(string text, int maxBytes, out bool truncated)
        {
            truncated = false;
            return string.IsNullOrEmpty(text) ? string.Empty : TruncateToUtf8Bytes(text, maxBytes, ref truncated);
        }

        public static BudgetedToolResult CreateTextResult(
            string summary,
            object meta,
            string text,
            object detailRef,
            int maxPreviewLines = PayloadBudgetPolicy.MaxPreviewFileLines,
            int maxPreviewBytes = PayloadBudgetPolicy.MaxToolResultBytes)
        {
            text ??= string.Empty;
            var preview = CreateTextPreview(text, maxPreviewLines, maxPreviewBytes, out var truncated);
            return new BudgetedToolResult
            {
                Summary = summary,
                Meta = meta,
                Preview = preview,
                DetailAvailable = detailRef != null,
                DetailRef = detailRef,
                Truncated = truncated,
                Sha256 = ComputeSha256(text),
                Bytes = GetUtf8ByteCount(text)
            };
        }

        static string TruncateToUtf8Bytes(string text, int maxBytes, ref bool truncated)
        {
            if (string.IsNullOrEmpty(text) || maxBytes <= 0 || k_Utf8.GetByteCount(text) <= maxBytes)
                return text ?? string.Empty;

            truncated = true;
            int low = 0;
            int high = text.Length;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (k_Utf8.GetByteCount(text.Substring(0, mid)) <= maxBytes)
                    low = mid;
                else
                    high = mid - 1;
            }

            return text.Substring(0, Math.Max(0, low));
        }

        static bool TryNormalizeJson(string text, ISet<string> flags, out string normalized)
        {
            try
            {
                normalized = NormalizeJsonToken(JToken.Parse(text), flags).ToString(Formatting.None);
                return true;
            }
            catch
            {
                normalized = null;
                return false;
            }
        }

        static JToken NormalizeJsonToken(JToken token, ISet<string> flags, string propertyName = null)
        {
            switch (token?.Type)
            {
                case JTokenType.Object:
                    var properties = new List<JProperty>();
                    foreach (var property in ((JObject)token).Properties())
                        properties.Add(new JProperty(property.Name, NormalizeJsonToken(property.Value, flags, property.Name)));
                    properties.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
                    return new JObject(properties);
                case JTokenType.Array:
                    var array = new JArray();
                    foreach (var child in (JArray)token)
                        array.Add(NormalizeJsonToken(child, flags, propertyName));
                    return array;
                case JTokenType.String:
                    if (TryGetVolatileFieldMarker(propertyName, out var marker))
                    {
                        flags.Add(marker);
                        return new JValue($"<{marker}>");
                    }
                    return new JValue(NormalizeScalarString(token.Value<string>() ?? string.Empty, flags));
                case JTokenType.Integer:
                case JTokenType.Float:
                case JTokenType.Date:
                    if (TryGetVolatileFieldMarker(propertyName, out var numericMarker))
                    {
                        flags.Add(numericMarker);
                        return new JValue(0);
                    }
                    return token.DeepClone();
                default:
                    return token?.DeepClone() ?? JValue.CreateNull();
            }
        }

        static string NormalizeScalarString(string value, ISet<string> flags)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var normalized = k_IsoTimestampRegex.Replace(value, _ =>
            {
                flags.Add("timestamp");
                return "<timestamp>";
            });

            return k_GuidRegex.Replace(normalized, _ =>
            {
                flags.Add("guid");
                return "<guid>";
            });
        }

        static bool TryGetVolatileFieldMarker(string propertyName, out string marker)
        {
            switch (propertyName?.ToLowerInvariant())
            {
                case "timestamputc":
                case "created_date":
                case "createddate":
                case "last_heartbeat":
                case "tool_snapshot_utc":
                case "last_command_success_utc":
                case "last_command_failure_utc":
                    marker = "timestamp";
                    return true;
                case "requestid":
                case "request_id":
                case "correlationid":
                case "correlation_id":
                case "toolcallid":
                case "tool_call_id":
                    marker = "request_id";
                    return true;
                case "valueindex":
                case "value_index":
                    marker = "value_index";
                    return true;
                default:
                    marker = null;
                    return false;
            }
        }
    }

    static class PayloadStats
    {
        const string k_SchemaVersion = "unity-mcp-lens.payload-stats.v1";
        static readonly object s_Lock = new();
        static readonly AsyncLocal<PayloadStatScope> s_CurrentScope = new();
        static string s_FilePath;

        sealed class ScopeHandle : IDisposable
        {
            readonly PayloadStatScope m_Previous;
            bool m_Disposed;

            public ScopeHandle(PayloadStatScope previous) => m_Previous = previous;

            public void Dispose()
            {
                if (m_Disposed)
                    return;

                m_Disposed = true;
                s_CurrentScope.Value = m_Previous;
            }
        }

        public static IDisposable BeginScope(PayloadStatScope scope)
        {
            var previous = s_CurrentScope.Value;
            s_CurrentScope.Value = previous == null ? scope : previous.Merge(scope);
            return new ScopeHandle(previous);
        }

        public static void RecordCoverage(string stage, string name, object meta = null, string hash = null, PayloadStatOptions options = null)
        {
            Record(stage, name, 0, 0, 0, hash, meta, options);
        }

        public static void RecordText(string stage, string name, string rawText, string shapedText = null, object meta = null, PayloadStatOptions options = null)
        {
            rawText ??= string.Empty;
            shapedText ??= rawText;
            options ??= new PayloadStatOptions();
            options.RawChars ??= rawText.Length;
            options.ShapedChars ??= shapedText.Length;

            var normalizedHash = PayloadBudgeting.ComputeNormalizedSha256(shapedText, out var flags);
            options.NormalizedHash ??= normalizedHash;
            options.DynamicFieldFlags ??= flags;

            Record(
                stage,
                name,
                PayloadBudgeting.GetUtf8ByteCount(rawText),
                PayloadBudgeting.GetUtf8ByteCount(shapedText),
                PayloadBudgeting.EstimateTokensFromBytes(PayloadBudgeting.GetUtf8ByteCount(shapedText)),
                PayloadBudgeting.ComputeSha256(shapedText),
                meta,
                options);
        }

        public static void Record(string stage, string name, int rawBytes, int shapedBytes, int tokenEstimate, string hash, object meta = null, PayloadStatOptions options = null)
        {
            try
            {
                var payload = new JObject
                {
                    ["schemaVersion"] = string.IsNullOrWhiteSpace(options?.SchemaVersion) ? k_SchemaVersion : options.SchemaVersion,
                    ["eventKind"] = ResolveEventKind(stage, options?.EventKind),
                    ["timestampUtc"] = DateTime.UtcNow.ToString("O"),
                    ["stage"] = stage,
                    ["name"] = name,
                    ["rawBytes"] = rawBytes,
                    ["shapedBytes"] = shapedBytes,
                    ["tokenEstimate"] = tokenEstimate,
                    ["hash"] = hash
                };

                ApplyScope(payload, s_CurrentScope.Value);
                ApplyOptions(payload, options);

                if (meta != null)
                    payload["meta"] = JToken.FromObject(meta);

                var filePath = GetFilePath();
                if (string.IsNullOrEmpty(filePath))
                    return;

                lock (s_Lock)
                {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(filePath) ?? System.IO.Directory.GetCurrentDirectory());
                    System.IO.File.AppendAllText(filePath, payload.ToString(Formatting.None) + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Best-effort telemetry only.
            }
        }

        static string GetFilePath()
        {
            if (!string.IsNullOrEmpty(s_FilePath))
                return s_FilePath;

            try
            {
                var projectRoot = !string.IsNullOrEmpty(Application.dataPath)
                    ? System.IO.Directory.GetParent(Application.dataPath)?.FullName
                    : System.IO.Directory.GetCurrentDirectory();
                s_FilePath = System.IO.Path.Combine(projectRoot ?? System.IO.Directory.GetCurrentDirectory(), "Library", "AI.Gateway.PayloadStats.jsonl");
                return s_FilePath;
            }
            catch
            {
                return null;
            }
        }

        static string ResolveEventKind(string stage, string eventKind)
        {
            if (!string.IsNullOrWhiteSpace(eventKind))
                return eventKind;

            if (!string.IsNullOrWhiteSpace(stage) && stage.StartsWith("coverage_bridge_", StringComparison.Ordinal))
                return "bridge_coverage";

            return !string.IsNullOrWhiteSpace(stage) && stage.StartsWith("coverage_", StringComparison.Ordinal)
                ? "coverage"
                : "payload";
        }

        static void ApplyScope(JObject payload, PayloadStatScope scope)
        {
            if (scope == null)
                return;

            AddString(payload, "runId", scope.RunId);
            AddString(payload, "taskId", scope.TaskId);
            AddString(payload, "operationId", scope.OperationId);
            AddString(payload, "conversationId", scope.ConversationId);
            AddString(payload, "connectionId", scope.ConnectionId);
            AddString(payload, "requestId", scope.RequestId);
            AddString(payload, "providerId", scope.ProviderId);
            AddString(payload, "workflowKind", scope.WorkflowKind);
            AddString(payload, "origin", scope.Origin);
        }

        static void ApplyOptions(JObject payload, PayloadStatOptions options)
        {
            if (options == null)
                return;

            AddString(payload, "runId", options.RunId);
            AddString(payload, "taskId", options.TaskId);
            AddString(payload, "operationId", options.OperationId);
            AddString(payload, "conversationId", options.ConversationId);
            AddString(payload, "connectionId", options.ConnectionId);
            AddString(payload, "requestId", options.RequestId);
            AddString(payload, "providerId", options.ProviderId);
            AddString(payload, "workflowKind", options.WorkflowKind);
            AddString(payload, "origin", options.Origin);
            AddInteger(payload, "rawChars", options.RawChars);
            AddInteger(payload, "shapedChars", options.ShapedChars);
            AddInteger(payload, "durationMs", options.DurationMs);
            AddBoolean(payload, "success", options.Success);
            AddString(payload, "errorKind", options.ErrorKind);
            AddString(payload, "errorMessageShort", TrimToLength(options.ErrorMessageShort, 240));
            AddString(payload, "representationKind", options.RepresentationKind);
            AddString(payload, "stableKey", options.StableKey);
            AddString(payload, "normalizedHash", options.NormalizedHash);
            AddString(payload, "duplicateClass", options.DuplicateClass);
            AddString(payload, "payloadClass", options.PayloadClass);
            AddString(payload, "dynamicFieldFlags", options.DynamicFieldFlags);
            AddBoolean(payload, "detailAvailable", options.DetailAvailable);
            AddBoolean(payload, "cached", options.Cached);
            AddBoolean(payload, "unchanged", options.Unchanged);
            AddString(payload, "cacheReuseClass", options.CacheReuseClass);

            if (options.ExtraFields == null)
                return;

            foreach (var property in JObject.FromObject(options.ExtraFields).Properties())
                payload[property.Name] = property.Value;
        }

        static void AddString(JObject payload, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                payload[key] = value;
        }

        static void AddInteger(JObject payload, string key, int? value)
        {
            if (value.HasValue)
                payload[key] = value.Value;
        }

        static void AddBoolean(JObject payload, string key, bool? value)
        {
            if (value.HasValue)
                payload[key] = value.Value;
        }

        static string TrimToLength(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || maxLength <= 0 || text.Length <= maxLength)
                return text;

            return text.Substring(0, maxLength);
        }
    }
}
