#nullable disable
using System;
using System.Globalization;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Lens.Usage;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class LensUsageTools
    {
        const string ToolName = "Unity.GetLensUsageReport";
        const string Description = "Returns a compact read-only report from the current project's Lens PayloadStats.jsonl, including payload totals, bridge churn, TSAM stage coverage, failures, latency, and cost findings.";

        [McpTool(ToolName, Description, "Get Lens Usage Report", Groups = new[] { "debug", "diagnostics" }, EnabledByDefault = true)]
        public static object GetUsageReport(JObject parameters)
        {
            parameters ??= new JObject();
            int sinceLine = ReadInt(parameters, "sinceLine");
            string sinceUtcText = ReadString(parameters, "sinceUtc");
            int lastRows = ReadInt(parameters, "lastRows");
            int maxItems = ReadInt(parameters, "maxItems");
            bool includeDetails = ReadBool(parameters, "includeDetails");

            if (sinceLine < 0)
                return Error("sinceLine must be greater than or equal to 1 when supplied, or omitted.", "invalid_since_line");
            if (lastRows < 0)
                return Error("lastRows must be greater than or equal to 1 when supplied, or omitted.", "invalid_last_rows");
            if (maxItems < 0)
                return Error("maxItems must be greater than or equal to 1 when supplied, or omitted.", "invalid_max_items");

            var query = new PayloadStatsQuery
            {
                LastRows = lastRows > 0 ? lastRows : PayloadStatsAnalyzer.DefaultLastRows,
                MaxItems = maxItems > 0 ? maxItems : PayloadStatsAnalyzer.DefaultMaxItems
            };

            var execution = McpToolExecutionScope.Current;
            if (!string.IsNullOrWhiteSpace(execution?.RequestId))
            {
                query.ExcludeConnectionId = execution.ConnectionId;
                query.ExcludeRequestId = execution.RequestId;
            }

            if (sinceLine > 0)
            {
                query.SinceLine = sinceLine;
            }
            else if (!string.IsNullOrWhiteSpace(sinceUtcText))
            {
                if (!DateTimeOffset.TryParse(sinceUtcText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var sinceUtc))
                    return Error("sinceUtc must be a valid ISO-8601 timestamp.", "invalid_since_utc");

                query.SinceUtc = sinceUtc.ToUniversalTime();
            }

            try
            {
                var report = PayloadStatsAnalyzer.LoadReport(query);
                object detailRef = null;
                if (includeDetails)
                {
                    var serialized = JsonConvert.SerializeObject(report, Formatting.None);
                    detailRef = ToolResultCompactor.CreateStoredDetailRef(
                        ToolName,
                        report,
                        PayloadBudgeting.GetUtf8ByteCount(serialized),
                        new
                        {
                            report.StatsPath,
                            report.Scope,
                            report.EntryCount,
                            report.NextLine
                        });
                }

                return Response.Success(
                    $"Retrieved Lens usage report for {report.EntryCount} row(s).",
                    PayloadStatsReportFormatter.CreateCompactData(report, detailRef));
            }
            catch (PayloadStatsException ex)
            {
                return Error(ex.Message, ex.ErrorKind);
            }
            catch (Exception ex)
            {
                return Error($"Failed to build Lens usage report: {ex.Message}", ex.GetType().Name);
            }
        }

        static object Error(string message, string errorKind)
        {
            return Response.Error(message, new
            {
                errorKind,
                code = errorKind
            });
        }

        [McpSchema(ToolName)]
        public static object GetUsageReportSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    sinceLine = new
                    {
                        type = "integer",
                        description = "Analyze rows starting at this 1-based physical JSONL line number. Takes precedence over sinceUtc and lastRows."
                    },
                    sinceUtc = new
                    {
                        type = "string",
                        description = "Analyze rows at or after this UTC ISO-8601 timestamp. Ignored when sinceLine is supplied."
                    },
                    lastRows = new
                    {
                        type = "integer",
                        description = "Analyze only the last N valid rows when sinceLine and sinceUtc are omitted. Defaults to 2000.",
                        @default = PayloadStatsAnalyzer.DefaultLastRows
                    },
                    maxItems = new
                    {
                        type = "integer",
                        description = "Maximum rows to return in each top-list section. Defaults to 8.",
                        @default = PayloadStatsAnalyzer.DefaultMaxItems
                    },
                    includeDetails = new
                    {
                        type = "boolean",
                        description = "When true, stores the fuller structured report behind a Unity.ReadDetailRef-compatible detail ref when the active bridge supports detail refs.",
                        @default = false
                    }
                }
            };
        }

        static int ReadInt(JObject parameters, string name)
        {
            if (!parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token) || token == null || token.Type == JTokenType.Null)
                return 0;

            if (token.Type == JTokenType.Integer)
                return token.Value<int>();

            return int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;
        }

        static string ReadString(JObject parameters, string name)
        {
            if (!parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token) || token == null || token.Type == JTokenType.Null)
                return string.Empty;

            return token.ToString();
        }

        static bool ReadBool(JObject parameters, string name)
        {
            if (!parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token) || token == null || token.Type == JTokenType.Null)
                return false;

            if (token.Type == JTokenType.Boolean)
                return token.Value<bool>();

            return bool.TryParse(token.ToString(), out var value) && value;
        }
    }
}
