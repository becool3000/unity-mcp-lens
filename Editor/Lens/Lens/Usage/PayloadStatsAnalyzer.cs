#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Lens.Usage
{
    sealed class PayloadStatsQuery
    {
        public int? SinceLine { get; set; }
        public DateTimeOffset? SinceUtc { get; set; }
        public int? LastRows { get; set; }
        public bool AllRows { get; set; }
        public int MaxItems { get; set; } = PayloadStatsAnalyzer.DefaultMaxItems;
        public string ExcludeConnectionId { get; set; }
        public string ExcludeRequestId { get; set; }
    }

    sealed class PayloadStatsException : Exception
    {
        public string ErrorKind { get; }

        public PayloadStatsException(string message, string errorKind) : base(message)
        {
            ErrorKind = errorKind;
        }
    }

    static class PayloadStatsFileAdapter
    {
        public static string ResolveStatsPath()
        {
            var dataPath = Application.dataPath;
            var projectRoot = !string.IsNullOrWhiteSpace(dataPath)
                ? Directory.GetParent(dataPath)?.FullName
                : Directory.GetCurrentDirectory();
            if (string.IsNullOrWhiteSpace(projectRoot))
                projectRoot = Directory.GetCurrentDirectory();

            return Path.Combine(projectRoot, "Library", "AI.Gateway.PayloadStats.jsonl");
        }
    }

    static class PayloadStatsAnalyzer
    {
        public const int DefaultLastRows = 2000;
        public const int DefaultMaxItems = 8;

        const double k_CoverageDominatedThresholdPct = 95.0d;
        const int k_MaxLastRows = 100000;
        const int k_MaxItems = 50;

        public static PayloadStatsReport LoadReport(PayloadStatsQuery query = null)
        {
            return LoadReport(PayloadStatsFileAdapter.ResolveStatsPath(), query);
        }

        public static PayloadStatsReport LoadReport(string statsPath, PayloadStatsQuery query = null)
        {
            if (string.IsNullOrWhiteSpace(statsPath))
                throw new PayloadStatsException("Stats path could not be resolved.", "stats_path_unresolved");

            if (!File.Exists(statsPath))
                throw new PayloadStatsException($"Stats file not found: {statsPath}", "stats_file_not_found");

            if (new FileInfo(statsPath).Length == 0)
                throw new PayloadStatsException($"Stats file is empty: {statsPath}", "stats_file_empty");

            query ??= new PayloadStatsQuery();
            int maxItems = Math.Clamp(query.MaxItems <= 0 ? DefaultMaxItems : query.MaxItems, 1, k_MaxItems);
            int lastRows = query.AllRows
                ? int.MaxValue
                : Math.Clamp(query.LastRows ?? DefaultLastRows, 1, k_MaxLastRows);

            var rows = ReadRows(statsPath, out int totalLineCount, out int skippedLines);
            if (rows.Count == 0)
                throw new PayloadStatsException($"No valid payload stat rows were found in {statsPath}.", "stats_file_empty");

            if (query.SinceLine.HasValue && (query.SinceLine.Value < 1 || query.SinceLine.Value > totalLineCount + 1))
                throw new PayloadStatsException($"sinceLine must be between 1 and {totalLineCount + 1}.", "invalid_since_line");

            IReadOnlyList<PayloadRow> scopedRows;
            int startLine;
            string scope;
            if (query.SinceLine.HasValue)
            {
                startLine = query.SinceLine.Value;
                scopedRows = rows.Where(row => row.LineNumber >= startLine).ToList();
                scope = $"sinceLine:{startLine}";
            }
            else if (query.SinceUtc.HasValue)
            {
                var sinceUtc = query.SinceUtc.Value;
                scopedRows = rows
                    .Where(row => row.TimestampUtc.HasValue && row.TimestampUtc.Value >= sinceUtc)
                    .ToList();
                startLine = scopedRows.Count > 0 ? scopedRows.Min(row => row.LineNumber) : totalLineCount + 1;
                scope = $"sinceUtc:{sinceUtc:O}";
            }
            else
            {
                scopedRows = rows.Skip(Math.Max(0, rows.Count - lastRows)).ToList();
                startLine = scopedRows.Count > 0 ? scopedRows[0].LineNumber : totalLineCount + 1;
                scope = query.AllRows ? "all" : $"lastRows:{lastRows}";
            }

            if (!string.IsNullOrWhiteSpace(query.ExcludeRequestId))
            {
                scopedRows = scopedRows
                    .Where(row => !ShouldExcludeRow(row, query.ExcludeConnectionId, query.ExcludeRequestId))
                    .ToList();
                scope = $"{scope},excludeRequest:{query.ExcludeRequestId}";
            }

            return BuildReport(statsPath, rows.Count, totalLineCount, skippedLines, startLine, totalLineCount + 1, scope, maxItems, scopedRows);
        }

        static bool ShouldExcludeRow(PayloadRow row, string connectionId, string requestId)
        {
            if (row == null || string.IsNullOrWhiteSpace(requestId))
                return false;

            if (!string.Equals(row.RequestId, requestId, StringComparison.Ordinal))
                return false;

            return string.IsNullOrWhiteSpace(connectionId) ||
                string.Equals(row.ConnectionId, connectionId, StringComparison.Ordinal);
        }

        static List<PayloadRow> ReadRows(string statsPath, out int totalLineCount, out int skippedLines)
        {
            var rows = new List<PayloadRow>();
            totalLineCount = 0;
            skippedLines = 0;

            foreach (var line in File.ReadLines(statsPath))
            {
                totalLineCount++;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var entry = JObject.Parse(line);
                    rows.Add(new PayloadRow
                    {
                        LineNumber = totalLineCount,
                        TimestampUtc = ParseDate(entry["timestampUtc"]),
                        EventKind = ReadString(entry["eventKind"]),
                        Stage = ReadString(entry["stage"]),
                        Name = ReadString(entry["name"]),
                        RawBytes = ReadLong(entry["rawBytes"]),
                        ShapedBytes = ReadLong(entry["shapedBytes"]),
                        Hash = ReadString(entry["hash"]),
                        NormalizedHash = ReadString(entry["normalizedHash"]),
                        RunId = ReadString(entry["runId"]),
                        ConnectionId = ReadString(entry["connectionId"], ReadString(entry.SelectToken("meta.connectionId"))),
                        RequestId = ReadString(entry["requestId"], ReadString(entry.SelectToken("meta.requestId"))),
                        Success = ReadBool(entry["success"]),
                        Unchanged = ReadBool(entry["unchanged"]),
                        ErrorKind = ReadString(entry["errorKind"]),
                        ErrorMessageShort = ReadString(entry["errorMessageShort"]),
                        CommandType = ReadString(entry["commandType"], ReadString(entry["name"])),
                        RequestBytes = ReadLong(entry["requestBytes"], ReadLong(entry.SelectToken("meta.payloadBytes"))),
                        ResponseBytes = ReadLong(entry["responseBytes"]),
                        DurationMs = ReadDouble(entry["durationMs"]),
                        RepresentationKind = ReadString(entry["representationKind"]),
                        PayloadClass = ReadString(entry["payloadClass"]),
                        CacheReuseClass = ReadString(entry["cacheReuseClass"]),
                        ToolName = ReadString(entry["toolName"]),
                        Action = ReadString(entry["action"]),
                        DiscoveryMode = ReadString(entry["discoveryMode"], ReadString(entry["toolDiscoveryMode"])),
                        SnapshotReason = ReadString(entry["snapshotReason"], ReadString(entry["toolDiscoveryReason"])),
                        SnapshotHashMinimal = ReadString(entry["snapshotHashMinimal"]),
                        SnapshotHashFull = ReadString(entry["snapshotHashFull"]),
                        BridgeSessionId = ReadString(entry["bridgeSessionId"], ReadString(entry.SelectToken("meta.bridgeSessionId"))),
                        ManifestVersion = ReadLong(entry["manifestVersion"], ReadLong(entry.SelectToken("meta.manifestVersion"))),
                        ManifestKind = ReadString(entry["manifestKind"], ReadString(entry.SelectToken("meta.manifestKind"))),
                        ManifestReason = ReadString(entry["manifestReason"], ReadString(entry.SelectToken("meta.manifestReason"))),
                        ActiveToolPacks = ReadPackList(entry["activeToolPacks"] ?? entry.SelectToken("meta.activeToolPacks")),
                        ResponseStatus = ReadString(entry["responseStatus"], ReadString(entry.SelectToken("meta.status")))
                    });
                }
                catch
                {
                    skippedLines++;
                }
            }

            return rows;
        }

        static PayloadStatsReport BuildReport(
            string statsPath,
            int validLineCount,
            int totalLineCount,
            int skippedLines,
            int startLine,
            int nextLine,
            string scope,
            int maxItems,
            IReadOnlyList<PayloadRow> rows)
        {
            var scopedRows = rows?.ToList() ?? new List<PayloadRow>();
            var coverageRows = scopedRows.Where(IsCoverageRow).ToList();
            var payloadRows = scopedRows.Where(row => !IsCoverageRow(row)).ToList();
            var orderedRows = scopedRows.OrderBy(row => row.TimestampUtc ?? DateTimeOffset.MinValue).ToList();
            long rawBytes = payloadRows.Sum(row => row.RawBytes);
            long shapedBytes = payloadRows.Sum(row => row.ShapedBytes);

            var exactDuplicateGroups = payloadRows
                .Where(row => !string.IsNullOrWhiteSpace(row.Hash))
                .GroupBy(row => $"{row.Stage}|{row.Name}|{row.Hash}")
                .Where(group => group.Count() > 1)
                .ToList();
            var normalizedDuplicateGroups = payloadRows
                .Where(row => !string.IsNullOrWhiteSpace(row.NormalizedHash))
                .GroupBy(row => $"{row.Stage}|{row.Name}|{row.NormalizedHash}")
                .Where(group => group.Count() > 1)
                .ToList();

            var exactRepeatedRawBytes = exactDuplicateGroups.Sum(group => group.OrderBy(row => row.TimestampUtc ?? DateTimeOffset.MinValue).Skip(1).Sum(row => row.RawBytes));
            var normalizedRepeatedRawBytes = normalizedDuplicateGroups.Sum(group => group.OrderBy(row => row.TimestampUtc ?? DateTimeOffset.MinValue).Skip(1).Sum(row => row.RawBytes));

            var bridgeRequestRows = coverageRows.Where(row => string.Equals(row.Stage, "coverage_bridge_command_request", StringComparison.Ordinal)).ToList();
            var bridgeResponseRows = coverageRows.Where(row => string.Equals(row.Stage, "coverage_bridge_command_response", StringComparison.Ordinal)).ToList();
            var bridgeTopCommands = bridgeRequestRows
                .GroupBy(row => string.IsNullOrWhiteSpace(row.CommandType) ? "(unknown)" : row.CommandType)
                .Select(group => new BridgeCommandRow
                {
                    Label = group.Key,
                    Count = group.Count(),
                    RequestBytes = group.Sum(row => row.RequestBytes)
                })
                .OrderByDescending(row => row.Count)
                .ThenByDescending(row => row.RequestBytes)
                .Take(maxItems)
                .ToList();

            var responseKeys = new HashSet<string>(
                bridgeResponseRows.Select(BuildBridgeRowKey).Where(key => !string.IsNullOrWhiteSpace(key)),
                StringComparer.Ordinal);
            var unmatchedRequests = bridgeRequestRows
                .Where(row => !string.IsNullOrWhiteSpace(BuildBridgeRowKey(row)) && !responseKeys.Contains(BuildBridgeRowKey(row)))
                .OrderBy(row => row.TimestampUtc ?? DateTimeOffset.MinValue)
                .Select(row => new UnmatchedRequestRow
                {
                    TimestampUtc = row.TimestampUtc,
                    ConnectionId = row.ConnectionId,
                    RequestId = row.RequestId,
                    CommandType = row.CommandType,
                    RequestBytes = row.RequestBytes,
                    Classification = ClassifyUnmatchedBridgeRequest(row, orderedRows)
                })
                .ToList();
            var connectionSummaries = scopedRows
                .Where(row => !string.IsNullOrWhiteSpace(row.ConnectionId))
                .GroupBy(row => row.ConnectionId)
                .Select(group =>
                {
                    var connectionRows = group.OrderBy(row => row.TimestampUtc ?? DateTimeOffset.MinValue).ToList();
                    var requests = connectionRows.Where(row => string.Equals(row.Stage, "coverage_bridge_command_request", StringComparison.Ordinal)).ToList();
                    var responses = connectionRows.Where(row => string.Equals(row.Stage, "coverage_bridge_command_response", StringComparison.Ordinal)).ToList();
                    return new ConnectionSummaryRow
                    {
                        ConnectionId = group.Key,
                        FirstUtc = connectionRows.FirstOrDefault()?.TimestampUtc,
                        LastUtc = connectionRows.LastOrDefault()?.TimestampUtc,
                        RequestCount = requests.Count,
                        ResponseCount = responses.Count,
                        UnmatchedRequestCount = unmatchedRequests.Count(request => string.Equals(request.ConnectionId, group.Key, StringComparison.Ordinal)),
                        TopCommand = requests
                            .GroupBy(row => string.IsNullOrWhiteSpace(row.CommandType) ? "(unknown)" : row.CommandType)
                            .OrderByDescending(commandGroup => commandGroup.Count())
                            .Select(commandGroup => commandGroup.Key)
                            .FirstOrDefault() ?? "(none)"
                    };
                })
                .OrderBy(row => row.FirstUtc ?? DateTimeOffset.MinValue)
                .ToList();
            var packSetTransitions = bridgeResponseRows
                .Where(row => string.Equals(row.CommandType, "set_tool_packs", StringComparison.Ordinal))
                .OrderBy(row => row.TimestampUtc ?? DateTimeOffset.MinValue)
                .Select(row => new PackSetTransitionRow
                {
                    TimestampUtc = row.TimestampUtc,
                    ConnectionId = row.ConnectionId,
                    RequestId = row.RequestId,
                    ActiveToolPacks = row.ActiveToolPacks,
                    ManifestKind = row.ManifestKind,
                    ManifestReason = row.ManifestReason,
                    Unchanged = row.Unchanged == true,
                    ResponseBytes = row.ResponseBytes
                })
                .ToList();

            var toolSnapshotRows = scopedRows.Where(row =>
                string.Equals(row.EventKind, "tool_snapshot", StringComparison.Ordinal) ||
                string.Equals(row.Stage, "tool_snapshot", StringComparison.Ordinal)).OrderBy(row => row.TimestampUtc ?? DateTimeOffset.MinValue).ToList();

            var minimalTransitions = 0;
            var fullTransitions = 0;
            var falseStableMinimalTransitions = 0;
            for (var i = 1; i < toolSnapshotRows.Count; i++)
            {
                var previous = toolSnapshotRows[i - 1];
                var current = toolSnapshotRows[i];
                if (!string.Equals(current.SnapshotHashMinimal, previous.SnapshotHashMinimal, StringComparison.Ordinal))
                    minimalTransitions++;
                if (!string.Equals(current.SnapshotHashFull, previous.SnapshotHashFull, StringComparison.Ordinal))
                    fullTransitions++;
                if (string.Equals(current.SnapshotHashMinimal, previous.SnapshotHashMinimal, StringComparison.Ordinal) &&
                    !string.Equals(current.SnapshotHashFull, previous.SnapshotHashFull, StringComparison.Ordinal))
                {
                    falseStableMinimalTransitions++;
                }
            }

            var latencyRows = scopedRows.Where(row => row.DurationMs > 0d).ToList();
            var report = new PayloadStatsReport
            {
                StatsPath = statsPath,
                Scope = scope,
                TotalLineCount = totalLineCount,
                ValidLineCount = validLineCount,
                SkippedLineCount = skippedLines,
                StartLine = startLine,
                NextLine = nextLine,
                EntryCount = scopedRows.Count,
                PayloadEntryCount = payloadRows.Count,
                CoverageEntryCount = coverageRows.Count,
                CoveragePct = Percent(coverageRows.Count, scopedRows.Count),
                FirstTimestampUtc = orderedRows.FirstOrDefault()?.TimestampUtc,
                LastTimestampUtc = orderedRows.LastOrDefault()?.TimestampUtc,
                RawBytes = rawBytes,
                ShapedBytes = shapedBytes,
                RawTokens = payloadRows.Sum(row => EstimateTokensFromBytes(row.RawBytes)),
                ShapedTokens = payloadRows.Sum(row => EstimateTokensFromBytes(row.ShapedBytes)),
                ShapingApplicability = CreateShapingApplicability(payloadRows.Count, coverageRows.Count, rawBytes, shapedBytes, payloadRows.Count(row => row.RawBytes > row.ShapedBytes)),
                ExactRepeatedRawBytes = exactRepeatedRawBytes,
                ExactRepeatedRawPct = Percent(exactRepeatedRawBytes, rawBytes),
                NormalizedRepeatedRawBytes = normalizedRepeatedRawBytes,
                NormalizedRepeatedRawPct = Percent(normalizedRepeatedRawBytes, rawBytes),
                BridgeRequestCount = bridgeRequestRows.Count,
                BridgeResponseCount = bridgeResponseRows.Count,
                BridgeRequestBytes = bridgeRequestRows.Sum(row => row.RequestBytes),
                BridgeResponseBytes = bridgeResponseRows.Sum(row => row.ResponseBytes),
                BridgeTopCommands = bridgeTopCommands,
                BridgeConnectionCount = connectionSummaries.Count,
                ConnectionSummaries = connectionSummaries,
                SetupCycleCount = CountSetupCycles(bridgeRequestRows),
                PackSetTransitions = packSetTransitions,
                UnmatchedRequests = unmatchedRequests,
                ToolSnapshotCount = toolSnapshotRows.Count,
                MinimalHashTransitions = minimalTransitions,
                FullHashTransitions = fullTransitions,
                FalseStableMinimalTransitions = falseStableMinimalTransitions,
                TopStages = payloadRows
                    .GroupBy(row => string.IsNullOrWhiteSpace(row.Stage) ? "(none)" : row.Stage)
                    .Select(CreateSummaryRow)
                    .OrderByDescending(row => row.RawBytes)
                    .Take(maxItems)
                    .ToList(),
                TopNames = payloadRows
                    .GroupBy(row => string.IsNullOrWhiteSpace(row.Name) ? "(none)" : row.Name)
                    .Select(CreateSummaryRow)
                    .OrderByDescending(row => row.RawBytes)
                    .Take(maxItems)
                    .ToList(),
                RunSummaries = scopedRows
                    .GroupBy(row => string.IsNullOrWhiteSpace(row.RunId) ? "(none)" : row.RunId)
                    .Select(group => new RunSummaryRow
                    {
                        Label = group.Key,
                        Count = group.Count(),
                        RawBytes = group.Sum(row => row.RawBytes),
                        FailureCount = group.Count(row => row.Success == false || !string.IsNullOrWhiteSpace(row.ErrorKind))
                    })
                    .OrderByDescending(row => row.Count)
                    .Take(maxItems)
                    .ToList(),
                Latency = new LatencyReport
                {
                    Overall = CreateLatencySummary(latencyRows),
                    SlowOperations = latencyRows
                        .GroupBy(row => $"{(string.IsNullOrWhiteSpace(row.Stage) ? "(none)" : row.Stage)}|{(string.IsNullOrWhiteSpace(row.Name) ? "(none)" : row.Name)}")
                        .Select(group => CreateSlowOperationRow(group))
                        .OrderByDescending(row => row.MeanMs)
                        .Take(maxItems)
                        .ToList()
                },
                TsamCoverage = CreateTsamCoverage(scopedRows, maxItems),
                FailureClasses = CreateFailureClasses(scopedRows, maxItems),
                LargestEntries = payloadRows
                    .OrderByDescending(row => row.RawBytes)
                    .Take(maxItems)
                    .Select(row => new PayloadEntrySummaryRow
                    {
                        TimestampUtc = row.TimestampUtc,
                        Stage = row.Stage,
                        Name = row.Name,
                        RawBytes = row.RawBytes,
                        ShapedBytes = row.ShapedBytes,
                        RepresentationKind = row.RepresentationKind,
                        PayloadClass = row.PayloadClass
                    })
                    .ToList()
            };

            report.Findings = CreateFindings(report);
            return report;
        }

        static PayloadSummaryRow CreateSummaryRow(IGrouping<string, PayloadRow> group)
        {
            var rawBytes = group.Sum(row => row.RawBytes);
            var shapedBytes = group.Sum(row => row.ShapedBytes);
            var savedBytes = Math.Max(0L, rawBytes - shapedBytes);
            return new PayloadSummaryRow
            {
                Label = group.Key,
                Count = group.Count(),
                RawBytes = rawBytes,
                ShapedBytes = shapedBytes,
                SavedBytes = savedBytes,
                SavingsPct = Percent(savedBytes, rawBytes)
            };
        }

        static List<TsamCoverageRow> CreateTsamCoverage(IReadOnlyList<PayloadRow> rows, int maxItems)
        {
            return rows
                .Where(IsTsamStageRow)
                .GroupBy(row => $"{ResolveTsamToolName(row)}|{ResolveTsamAction(row)}")
                .Select(group =>
                {
                    var groupedRows = group.ToList();
                    int CountStage(string stage) => groupedRows.Count(row => string.Equals(row.Stage, stage, StringComparison.Ordinal));
                    return new TsamCoverageRow
                    {
                        ToolName = ResolveTsamToolName(groupedRows[0]),
                        Action = ResolveTsamAction(groupedRows[0]),
                        RowCount = groupedRows.Count,
                        OperationCount = new[] { CountStage("normalization"), CountStage("service"), CountStage("adapter"), CountStage("result_shaping") }.DefaultIfEmpty(0).Max(),
                        NormalizationRows = CountStage("normalization"),
                        ServiceRows = CountStage("service"),
                        AdapterRows = CountStage("adapter"),
                        ResultShapingRows = CountStage("result_shaping"),
                        FailedRows = groupedRows.Count(row => row.Success == false || !string.IsNullOrWhiteSpace(row.ErrorKind)),
                        ErrorKinds = groupedRows.Select(row => row.ErrorKind).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                        RequestBytes = groupedRows.Max(row => row.RequestBytes),
                        ResponseBytes = groupedRows.Max(row => row.ResponseBytes),
                        TotalDurationMs = Math.Round(groupedRows.Sum(row => row.DurationMs), 2)
                    };
                })
                .OrderByDescending(row => row.RowCount)
                .ThenBy(row => row.ToolName, StringComparer.Ordinal)
                .ThenBy(row => row.Action, StringComparer.Ordinal)
                .Take(maxItems)
                .ToList();
        }

        static List<FailureClassRow> CreateFailureClasses(IReadOnlyList<PayloadRow> rows, int maxItems)
        {
            return rows
                .Where(row => row.Success == false || !string.IsNullOrWhiteSpace(row.ErrorKind))
                .GroupBy(row => $"{(string.IsNullOrWhiteSpace(row.Stage) ? "(none)" : row.Stage)}|{(string.IsNullOrWhiteSpace(row.Name) ? "(none)" : row.Name)}|{(string.IsNullOrWhiteSpace(row.ErrorKind) ? "(unknown)" : row.ErrorKind)}")
                .Select(group =>
                {
                    var first = group.First();
                    return new FailureClassRow
                    {
                        Stage = string.IsNullOrWhiteSpace(first.Stage) ? "(none)" : first.Stage,
                        Name = string.IsNullOrWhiteSpace(first.Name) ? "(none)" : first.Name,
                        ErrorKind = string.IsNullOrWhiteSpace(first.ErrorKind) ? "(unknown)" : first.ErrorKind,
                        Count = group.Count()
                    };
                })
                .OrderByDescending(row => row.Count)
                .Take(maxItems)
                .ToList();
        }

        static List<UsageFindingRow> CreateFindings(PayloadStatsReport report)
        {
            var findings = new List<UsageFindingRow>();
            if (report.NormalizedRepeatedRawPct >= 10d)
                findings.Add(new UsageFindingRow("repeated_context", "warning", $"Normalized repeated payload estimate is {report.NormalizedRepeatedRawPct:F2}% of raw payload bytes."));

            int schemaRequests = report.BridgeTopCommands?.FirstOrDefault(row => string.Equals(row.Label, "get_tool_schema", StringComparison.Ordinal))?.Count ?? 0;
            if (schemaRequests > 5)
                findings.Add(new UsageFindingRow("schema_churn", "info", $"get_tool_schema was requested {schemaRequests} times in scope."));

            if (report.BridgeConnectionCount > 3 || report.SetupCycleCount > 1)
                findings.Add(new UsageFindingRow("session_churn", "info", $"Scope contains {report.BridgeConnectionCount} connections and {report.SetupCycleCount} setup cycles."));

            if ((report.PackSetTransitions?.Count ?? 0) > 2)
                findings.Add(new UsageFindingRow("pack_churn", "info", $"Scope contains {report.PackSetTransitions.Count} pack-set transitions."));

            if ((report.UnmatchedRequests?.Count ?? 0) > 0)
                findings.Add(new UsageFindingRow("unmatched_requests", "warning", $"Scope contains {report.UnmatchedRequests.Count} unmatched bridge request(s)."));

            if (report.FalseStableMinimalTransitions > 0)
                findings.Add(new UsageFindingRow("tool_snapshot_noise", "warning", $"Tool snapshot minimal hash stayed stable while full hash changed {report.FalseStableMinimalTransitions} time(s)."));

            if ((report.FailureClasses?.Count ?? 0) > 0)
                findings.Add(new UsageFindingRow("failures_recorded", "info", $"Scope includes {report.FailureClasses.Sum(row => row.Count)} failed or error-classified row(s)."));

            var incompleteTsam = report.TsamCoverage?.FirstOrDefault(row =>
                row.NormalizationRows != row.OperationCount ||
                row.ServiceRows != row.OperationCount ||
                row.AdapterRows != row.OperationCount ||
                row.ResultShapingRows != row.OperationCount);
            if (incompleteTsam != null)
                findings.Add(new UsageFindingRow("tsam_stage_gap", "warning", $"{incompleteTsam.ToolName}/{incompleteTsam.Action} has incomplete TSAM stage coverage."));

            var largest = report.LargestEntries?.FirstOrDefault();
            if (largest != null && largest.RawBytes >= 20000)
                findings.Add(new UsageFindingRow("large_payload", "info", $"Largest payload is {FormatBytes(largest.RawBytes)} from {largest.Name}."));

            return findings;
        }

        static LatencySummaryRow CreateLatencySummary(IReadOnlyList<PayloadRow> rows)
        {
            var values = rows.Select(row => row.DurationMs).Where(value => value > 0d).OrderBy(value => value).ToArray();
            if (values.Length == 0)
            {
                return new LatencySummaryRow();
            }

            return new LatencySummaryRow
            {
                Count = values.Length,
                MeanMs = Math.Round(values.Average(), 2),
                P50Ms = Percentile(values, 50d),
                P95Ms = Percentile(values, 95d),
                P99Ms = Percentile(values, 99d),
                MaxMs = Math.Round(values.Max(), 2)
            };
        }

        static SlowOperationRow CreateSlowOperationRow(IGrouping<string, PayloadRow> group)
        {
            var values = group.Select(row => row.DurationMs).Where(value => value > 0d).OrderBy(value => value).ToArray();
            return new SlowOperationRow
            {
                Label = group.Key,
                Count = values.Length,
                MeanMs = values.Length == 0 ? 0d : Math.Round(values.Average(), 2),
                P95Ms = Percentile(values, 95d),
                MaxMs = values.Length == 0 ? 0d : Math.Round(values.Max(), 2)
            };
        }

        static double Percentile(double[] sortedValues, double percentile)
        {
            if (sortedValues == null || sortedValues.Length == 0)
                return 0d;
            if (sortedValues.Length == 1)
                return Math.Round(sortedValues[0], 2);

            var rank = (percentile / 100d) * (sortedValues.Length - 1);
            var lower = (int)Math.Floor(rank);
            var upper = (int)Math.Ceiling(rank);
            if (lower == upper)
                return Math.Round(sortedValues[lower], 2);

            var weight = rank - lower;
            return Math.Round(sortedValues[lower] * (1d - weight) + sortedValues[upper] * weight, 2);
        }

        static bool IsCoverageRow(PayloadRow row)
        {
            if (row == null)
                return false;

            if (!string.IsNullOrWhiteSpace(row.Stage) && row.Stage.StartsWith("coverage_", StringComparison.Ordinal))
                return true;

            return string.Equals(row.EventKind, "coverage", StringComparison.Ordinal) ||
                string.Equals(row.EventKind, "bridge_coverage", StringComparison.Ordinal);
        }

        static bool IsTsamStageRow(PayloadRow row)
        {
            if (row == null)
                return false;

            return string.Equals(row.EventKind, "tool_tsam_stage", StringComparison.Ordinal) ||
                string.Equals(row.PayloadClass, "tool_timing", StringComparison.Ordinal);
        }

        static string ResolveTsamToolName(PayloadRow row)
        {
            if (!string.IsNullOrWhiteSpace(row?.ToolName))
                return row.ToolName;

            var parts = (row?.Name ?? string.Empty).Split('.');
            return parts.Length >= 2 ? string.Join(".", parts.Take(parts.Length - 2)) : "(unknown)";
        }

        static string ResolveTsamAction(PayloadRow row)
        {
            if (!string.IsNullOrWhiteSpace(row?.Action))
                return row.Action;

            var parts = (row?.Name ?? string.Empty).Split('.');
            return parts.Length >= 2 ? parts[parts.Length - 2] : "(unknown)";
        }

        static ShapingApplicabilityRow CreateShapingApplicability(int payloadRowsEligible, int coverageRowsNotApplicable, long rawBytes, long shapedBytes, int payloadRowsWithSavings)
        {
            string summary;
            if (payloadRowsEligible == 0)
                summary = "No payload rows recorded; coverage rows are shaping n/a.";
            else if (rawBytes > 0 && rawBytes == shapedBytes)
                summary = "No shaping recorded for eligible payload rows; coverage rows are shaping n/a.";
            else
                summary = "Payload rows are shaping eligible; coverage rows are shaping n/a.";

            return new ShapingApplicabilityRow
            {
                PayloadRowsEligible = payloadRowsEligible,
                CoverageRowsNotApplicable = coverageRowsNotApplicable,
                PayloadRowsWithSavings = payloadRowsWithSavings,
                NoShapingRecorded = rawBytes > 0 && rawBytes == shapedBytes,
                Summary = summary
            };
        }

        static string BuildBridgeRowKey(PayloadRow row)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.RequestId))
                return string.Empty;

            return $"{row.ConnectionId}|{row.RequestId}";
        }

        static string ClassifyUnmatchedBridgeRequest(PayloadRow request, IReadOnlyList<PayloadRow> orderedRows)
        {
            if (request?.TimestampUtc == null || orderedRows == null)
                return "unmatched_request";

            var requestTime = request.TimestampUtc.Value;
            var nearbyRows = orderedRows
                .Where(row => row.TimestampUtc.HasValue &&
                    row.TimestampUtc.Value > requestTime &&
                    (row.TimestampUtc.Value - requestTime).TotalSeconds <= 45d)
                .ToList();
            bool hasNewConnection = nearbyRows.Any(row =>
                string.Equals(row.Stage, "coverage_bridge_command_request", StringComparison.Ordinal) &&
                string.Equals(row.CommandType, "register_client", StringComparison.Ordinal) &&
                !string.Equals(row.ConnectionId, request.ConnectionId, StringComparison.Ordinal));
            bool hasSnapshotRefresh = nearbyRows.Any(row =>
                string.Equals(row.EventKind, "tool_snapshot", StringComparison.Ordinal) ||
                string.Equals(row.Stage, "tool_snapshot", StringComparison.Ordinal));
            bool hasReloadHint = nearbyRows.Any(row =>
                !string.IsNullOrWhiteSpace(row.SnapshotReason) &&
                (row.SnapshotReason.IndexOf("reload", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    row.SnapshotReason.IndexOf("compile", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    row.SnapshotReason.IndexOf("domain", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    row.SnapshotReason.IndexOf("bridge_started", StringComparison.OrdinalIgnoreCase) >= 0));

            if (hasNewConnection && (hasSnapshotRefresh || hasReloadHint))
                return "domain_reload_transport_close";

            if (hasNewConnection)
                return "transport_reconnect_after_request";

            if (hasSnapshotRefresh)
                return "snapshot_refresh_after_request";

            return "unmatched_request";
        }

        static int CountSetupCycles(IReadOnlyList<PayloadRow> bridgeRequestRows)
        {
            var count = 0;
            foreach (var connectionGroup in bridgeRequestRows
                .OrderBy(row => row.TimestampUtc ?? DateTimeOffset.MinValue)
                .GroupBy(row => row.ConnectionId))
            {
                var requests = connectionGroup.OrderBy(row => row.TimestampUtc ?? DateTimeOffset.MinValue).ToList();
                foreach (var registerRow in requests.Where(row => string.Equals(row.CommandType, "register_client", StringComparison.Ordinal)))
                {
                    if (!registerRow.TimestampUtc.HasValue)
                        continue;

                    var manifestRow = requests.FirstOrDefault(row =>
                        row.TimestampUtc.HasValue &&
                        row.TimestampUtc.Value > registerRow.TimestampUtc.Value &&
                        string.Equals(row.CommandType, "get_manifest", StringComparison.Ordinal));
                    if (manifestRow?.TimestampUtc == null)
                        continue;

                    var schemaRow = requests.FirstOrDefault(row =>
                        row.TimestampUtc.HasValue &&
                        row.TimestampUtc.Value > manifestRow.TimestampUtc.Value &&
                        string.Equals(row.CommandType, "get_tool_schema", StringComparison.Ordinal));
                    if (schemaRow != null)
                        count++;
                }
            }

            return count;
        }

        static DateTimeOffset? ParseDate(JToken token)
        {
            var value = ReadString(token);
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                return parsed;

            return null;
        }

        static string ReadString(JToken token, string fallback = "")
        {
            if (token == null || token.Type == JTokenType.Null)
                return fallback;

            var value = token.ToString();
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        static long ReadLong(JToken token, long fallback = 0)
        {
            if (token == null || token.Type == JTokenType.Null)
                return fallback;

            if (token.Type == JTokenType.Integer)
                return token.Value<long>();

            if (token.Type == JTokenType.Float)
                return Convert.ToInt64(Math.Round(token.Value<double>()));

            return long.TryParse(token.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
        }

        static double ReadDouble(JToken token, double fallback = 0d)
        {
            if (token == null || token.Type == JTokenType.Null)
                return fallback;

            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
                return token.Value<double>();

            return double.TryParse(token.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
        }

        static bool? ReadBool(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            if (token.Type == JTokenType.Boolean)
                return token.Value<bool>();

            if (bool.TryParse(token.ToString(), out var parsed))
                return parsed;

            return null;
        }

        static string ReadPackList(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return string.Empty;

            if (token.Type == JTokenType.Array)
            {
                return string.Join(",",
                    token.Values<string>()
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value.Trim()));
            }

            return ReadString(token);
        }

        static long EstimateTokensFromBytes(long bytes)
        {
            if (bytes <= 0)
                return 0;

            return (long)Math.Ceiling(bytes / 4.0d);
        }

        static double Percent(long part, long whole)
        {
            if (whole <= 0)
                return 0d;

            return Math.Round((part / (double)whole) * 100d, 2);
        }

        static string FormatBytes(long bytes)
        {
            var value = (double)bytes;
            var units = new[] { "B", "KB", "MB", "GB" };
            var unitIndex = 0;
            while (value >= 1024d && unitIndex < units.Length - 1)
            {
                value /= 1024d;
                unitIndex++;
            }

            return $"{value:N2} {units[unitIndex]}";
        }
    }

    static class PayloadStatsReportFormatter
    {
        const int k_TopCount = 8;

        public static object CreateCompactData(PayloadStatsReport report, object detailRef)
        {
            return new
            {
                statsPath = report.StatsPath,
                scope = report.Scope,
                marker = new
                {
                    startLine = report.StartLine,
                    nextLine = report.NextLine,
                    lastTimestampUtc = report.LastTimestampUtc?.ToString("O")
                },
                totals = new
                {
                    entries = report.EntryCount,
                    payloadRows = report.PayloadEntryCount,
                    coverageRows = report.CoverageEntryCount,
                    skippedLines = report.SkippedLineCount,
                    rawBytes = report.RawBytes,
                    shapedBytes = report.ShapedBytes,
                    savedBytes = Math.Max(0L, report.RawBytes - report.ShapedBytes),
                    rawTokens = report.RawTokens,
                    shapedTokens = report.ShapedTokens,
                    savedTokens = report.SavedTokens,
                    savingsPct = report.SavingsPct,
                    shaping = report.ShapingApplicability
                },
                repeatedContext = new
                {
                    exactRawBytes = report.ExactRepeatedRawBytes,
                    exactRawPct = report.ExactRepeatedRawPct,
                    normalizedRawBytes = report.NormalizedRepeatedRawBytes,
                    normalizedRawPct = report.NormalizedRepeatedRawPct
                },
                bridge = new
                {
                    requests = report.BridgeRequestCount,
                    responses = report.BridgeResponseCount,
                    connections = report.BridgeConnectionCount,
                    setupCycles = report.SetupCycleCount,
                    requestBytes = report.BridgeRequestBytes,
                    responseBytes = report.BridgeResponseBytes,
                    topCommands = report.BridgeTopCommands,
                    packSetTransitions = report.PackSetTransitions,
                    unmatchedRequests = report.UnmatchedRequests
                },
                toolSnapshotChurn = new
                {
                    rows = report.ToolSnapshotCount,
                    minimalHashTransitions = report.MinimalHashTransitions,
                    fullHashTransitions = report.FullHashTransitions,
                    falseStableMinimalTransitions = report.FalseStableMinimalTransitions
                },
                tsamCoverage = report.TsamCoverage,
                failureClasses = report.FailureClasses,
                latency = report.Latency,
                largePayloads = report.LargestEntries,
                topStages = report.TopStages,
                topNames = report.TopNames,
                findings = report.Findings,
                detailRef
            };
        }

        public static string BuildClipboardReport(PayloadStatsReport report)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Unity MCP Lens Usage Report");
            builder.AppendLine($"Stats path: {report.StatsPath}");
            builder.AppendLine("Scope: current Unity project");
            builder.AppendLine($"Entries: {FormatNumber(report.EntryCount)} total, {FormatNumber(report.PayloadEntryCount)} payload, {FormatNumber(report.CoverageEntryCount)} coverage");

            if (report.FirstTimestampUtc.HasValue && report.LastTimestampUtc.HasValue)
                builder.AppendLine($"Date range: {report.FirstTimestampUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss} -> {report.LastTimestampUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}");

            if (report.CoverageDominated)
                builder.AppendLine($"Coverage-dominated log warning: {report.CoveragePct:F2}% of rows are coverage events.");

            builder.AppendLine();
            builder.AppendLine($"Payload stats: {FormatNumber(report.RawTokens)} raw tokens -> {FormatNumber(report.ShapedTokens)} shaped tokens, saved {FormatNumber(report.SavedTokens)} tokens ({report.SavingsPct:F2}% savings).");
            builder.AppendLine($"Shaping applicability: {report.ShapingApplicability?.Summary ?? "Payload rows are shaping eligible; coverage rows are shaping n/a."}");
            builder.AppendLine($"Repeated context estimate: exact {report.ExactRepeatedRawPct:F2}% raw, normalized {report.NormalizedRepeatedRawPct:F2}% raw.");
            builder.AppendLine($"Bridge requests seen: {FormatNumber(report.BridgeRequestCount)}. Bridge responses seen: {FormatNumber(report.BridgeResponseCount)}.");
            builder.AppendLine($"Session churn: {FormatNumber(report.BridgeConnectionCount)} connections, {FormatNumber(report.SetupCycleCount)} setup cycles, {FormatNumber(report.PackSetTransitions?.Count ?? 0)} pack-set transitions, {FormatNumber(report.UnmatchedRequests?.Count ?? 0)} unmatched requests.");
            builder.AppendLine($"Tool snapshot churn: {FormatNumber(report.ToolSnapshotCount)} rows, {FormatNumber(report.MinimalHashTransitions)} minimal transitions, {FormatNumber(report.FullHashTransitions)} full transitions, {FormatNumber(report.FalseStableMinimalTransitions)} false-stable minimal transitions.");

            AppendTsamCoverage(builder, report.TsamCoverage);
            AppendFailures(builder, report.FailureClasses);
            AppendFindings(builder, report.Findings);
            AppendBridgeCommands(builder, report.BridgeTopCommands);
            AppendConnectionSummaries(builder, report.ConnectionSummaries);
            AppendPackSetTransitions(builder, report.PackSetTransitions);
            AppendUnmatchedRequests(builder, report.UnmatchedRequests);
            AppendLargePayloads(builder, report.LargestEntries);
            AppendSummaryRows(builder, "Top payload stages", report.TopStages);
            AppendSummaryRows(builder, "Top payload names", report.TopNames);
            AppendRunSummaries(builder, report.RunSummaries);

            return builder.ToString().TrimEnd();
        }

        static void AppendTsamCoverage(StringBuilder builder, IReadOnlyList<TsamCoverageRow> rows)
        {
            if (rows == null || rows.Count == 0)
                return;

            builder.AppendLine();
            builder.AppendLine("TSAM stage coverage:");
            foreach (var row in rows.Take(k_TopCount))
            {
                builder.AppendLine($"- {row.ToolName}/{row.Action}: ops {row.OperationCount}, rows {row.RowCount}, stages n/s/a/r {row.NormalizationRows}/{row.ServiceRows}/{row.AdapterRows}/{row.ResultShapingRows}, failures {row.FailedRows}");
            }
        }

        static void AppendFailures(StringBuilder builder, IReadOnlyList<FailureClassRow> rows)
        {
            if (rows == null || rows.Count == 0)
                return;

            builder.AppendLine();
            builder.AppendLine("Failure classes:");
            foreach (var row in rows.Take(k_TopCount))
                builder.AppendLine($"- {row.Name}: {row.ErrorKind}, count {row.Count}");
        }

        static void AppendFindings(StringBuilder builder, IReadOnlyList<UsageFindingRow> rows)
        {
            if (rows == null || rows.Count == 0)
                return;

            builder.AppendLine();
            builder.AppendLine("Findings:");
            foreach (var row in rows.Take(k_TopCount))
                builder.AppendLine($"- {row.Severity}/{row.Kind}: {row.Message}");
        }

        static void AppendSummaryRows(StringBuilder builder, string heading, IReadOnlyList<PayloadSummaryRow> rows)
        {
            if (rows == null || rows.Count == 0)
                return;

            builder.AppendLine();
            builder.AppendLine($"{heading}:");
            foreach (var row in rows)
            {
                builder.AppendLine($"- {row.Label}: {FormatBytes(row.RawBytes)} raw -> {FormatBytes(row.ShapedBytes)} shaped, saved {FormatBytes(row.SavedBytes)} ({row.SavingsPct:F2}%), count {row.Count}");
            }
        }

        static void AppendRunSummaries(StringBuilder builder, IReadOnlyList<RunSummaryRow> rows)
        {
            if (rows == null || rows.Count == 0)
                return;

            builder.AppendLine();
            builder.AppendLine("Top runs:");
            foreach (var row in rows)
            {
                builder.AppendLine($"- {row.Label}: rows {row.Count}, raw {FormatBytes(row.RawBytes)}, failures {row.FailureCount}");
            }
        }

        static void AppendConnectionSummaries(StringBuilder builder, IReadOnlyList<ConnectionSummaryRow> rows)
        {
            if (rows == null || rows.Count == 0)
                return;

            builder.AppendLine();
            builder.AppendLine("Bridge connections:");
            foreach (var row in rows.Take(k_TopCount))
            {
                builder.AppendLine($"- {row.ConnectionId}: requests {row.RequestCount}, responses {row.ResponseCount}, unmatched {row.UnmatchedRequestCount}, top {row.TopCommand}");
            }
        }

        static void AppendPackSetTransitions(StringBuilder builder, IReadOnlyList<PackSetTransitionRow> rows)
        {
            if (rows == null || rows.Count == 0)
                return;

            builder.AppendLine();
            builder.AppendLine("Pack-set transitions:");
            foreach (var row in rows.Take(k_TopCount))
            {
                builder.AppendLine($"- {row.ConnectionId}: {row.ActiveToolPacks} -> {row.ManifestKind}, unchanged {row.Unchanged}, response {FormatBytes(row.ResponseBytes)}");
            }
        }

        static void AppendUnmatchedRequests(StringBuilder builder, IReadOnlyList<UnmatchedRequestRow> rows)
        {
            if (rows == null || rows.Count == 0)
                return;

            builder.AppendLine();
            builder.AppendLine("Unmatched bridge requests:");
            foreach (var row in rows.Take(k_TopCount))
            {
                builder.AppendLine($"- {row.CommandType}: {row.ConnectionId}, {ShortId(row.RequestId)}, {row.Classification}");
            }
        }

        static void AppendBridgeCommands(StringBuilder builder, IReadOnlyList<BridgeCommandRow> rows)
        {
            if (rows == null || rows.Count == 0)
                return;

            builder.AppendLine();
            builder.AppendLine("Top bridge commands:");
            foreach (var row in rows)
            {
                builder.AppendLine($"- {row.Label}: count {row.Count}, request bytes {FormatBytes(row.RequestBytes)}");
            }
        }

        static void AppendLargePayloads(StringBuilder builder, IReadOnlyList<PayloadEntrySummaryRow> rows)
        {
            if (rows == null || rows.Count == 0)
                return;

            builder.AppendLine();
            builder.AppendLine("Largest payload rows:");
            foreach (var row in rows.Take(k_TopCount))
            {
                builder.AppendLine($"- {row.Name}: {row.Stage}, {FormatBytes(row.RawBytes)} raw -> {FormatBytes(row.ShapedBytes)} shaped");
            }
        }

        public static string FormatBytes(long bytes)
        {
            var value = (double)bytes;
            var units = new[] { "B", "KB", "MB", "GB" };
            var unitIndex = 0;
            while (value >= 1024d && unitIndex < units.Length - 1)
            {
                value /= 1024d;
                unitIndex++;
            }

            return $"{value:N2} {units[unitIndex]}";
        }

        public static string FormatNumber(long value)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }

        static string ShortId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length <= 10)
                return value ?? string.Empty;

            return value.Substring(0, 10);
        }
    }

    sealed class PayloadStatsReport
    {
        const double k_CoverageDominatedThresholdPct = 95.0d;

        public string StatsPath { get; set; }
        public string Scope { get; set; }
        public int TotalLineCount { get; set; }
        public int ValidLineCount { get; set; }
        public int SkippedLineCount { get; set; }
        public int StartLine { get; set; }
        public int NextLine { get; set; }
        public int EntryCount { get; set; }
        public int PayloadEntryCount { get; set; }
        public int CoverageEntryCount { get; set; }
        public double CoveragePct { get; set; }
        public bool CoverageDominated => CoveragePct >= k_CoverageDominatedThresholdPct;
        public DateTimeOffset? FirstTimestampUtc { get; set; }
        public DateTimeOffset? LastTimestampUtc { get; set; }
        public long RawBytes { get; set; }
        public long ShapedBytes { get; set; }
        public long RawTokens { get; set; }
        public long ShapedTokens { get; set; }
        public long SavedTokens => Math.Max(0L, RawTokens - ShapedTokens);
        public double SavingsPct => RawBytes <= 0 ? 0d : Math.Round((Math.Max(0L, RawBytes - ShapedBytes) / (double)RawBytes) * 100d, 2);
        public ShapingApplicabilityRow ShapingApplicability { get; set; }
        public long ExactRepeatedRawBytes { get; set; }
        public double ExactRepeatedRawPct { get; set; }
        public long NormalizedRepeatedRawBytes { get; set; }
        public double NormalizedRepeatedRawPct { get; set; }
        public int BridgeRequestCount { get; set; }
        public int BridgeResponseCount { get; set; }
        public long BridgeRequestBytes { get; set; }
        public long BridgeResponseBytes { get; set; }
        public List<BridgeCommandRow> BridgeTopCommands { get; set; }
        public int BridgeConnectionCount { get; set; }
        public List<ConnectionSummaryRow> ConnectionSummaries { get; set; }
        public int SetupCycleCount { get; set; }
        public List<PackSetTransitionRow> PackSetTransitions { get; set; }
        public List<UnmatchedRequestRow> UnmatchedRequests { get; set; }
        public int ToolSnapshotCount { get; set; }
        public int MinimalHashTransitions { get; set; }
        public int FullHashTransitions { get; set; }
        public int FalseStableMinimalTransitions { get; set; }
        public List<PayloadSummaryRow> TopStages { get; set; }
        public List<PayloadSummaryRow> TopNames { get; set; }
        public List<RunSummaryRow> RunSummaries { get; set; }
        public LatencyReport Latency { get; set; }
        public List<TsamCoverageRow> TsamCoverage { get; set; }
        public List<FailureClassRow> FailureClasses { get; set; }
        public List<PayloadEntrySummaryRow> LargestEntries { get; set; }
        public List<UsageFindingRow> Findings { get; set; }
    }

    sealed class ShapingApplicabilityRow
    {
        public int PayloadRowsEligible { get; set; }
        public int CoverageRowsNotApplicable { get; set; }
        public int PayloadRowsWithSavings { get; set; }
        public bool NoShapingRecorded { get; set; }
        public string Summary { get; set; }
    }

    sealed class PayloadSummaryRow
    {
        public string Label { get; set; }
        public int Count { get; set; }
        public long RawBytes { get; set; }
        public long ShapedBytes { get; set; }
        public long SavedBytes { get; set; }
        public double SavingsPct { get; set; }
    }

    sealed class RunSummaryRow
    {
        public string Label { get; set; }
        public int Count { get; set; }
        public long RawBytes { get; set; }
        public int FailureCount { get; set; }
    }

    sealed class BridgeCommandRow
    {
        public string Label { get; set; }
        public int Count { get; set; }
        public long RequestBytes { get; set; }
    }

    sealed class ConnectionSummaryRow
    {
        public string ConnectionId { get; set; }
        public DateTimeOffset? FirstUtc { get; set; }
        public DateTimeOffset? LastUtc { get; set; }
        public int RequestCount { get; set; }
        public int ResponseCount { get; set; }
        public int UnmatchedRequestCount { get; set; }
        public string TopCommand { get; set; }
    }

    sealed class PackSetTransitionRow
    {
        public DateTimeOffset? TimestampUtc { get; set; }
        public string ConnectionId { get; set; }
        public string RequestId { get; set; }
        public string ActiveToolPacks { get; set; }
        public string ManifestKind { get; set; }
        public string ManifestReason { get; set; }
        public bool Unchanged { get; set; }
        public long ResponseBytes { get; set; }
    }

    sealed class UnmatchedRequestRow
    {
        public DateTimeOffset? TimestampUtc { get; set; }
        public string ConnectionId { get; set; }
        public string RequestId { get; set; }
        public string CommandType { get; set; }
        public long RequestBytes { get; set; }
        public string Classification { get; set; }
    }

    sealed class LatencyReport
    {
        public LatencySummaryRow Overall { get; set; }
        public List<SlowOperationRow> SlowOperations { get; set; }
    }

    sealed class LatencySummaryRow
    {
        public int Count { get; set; }
        public double MeanMs { get; set; }
        public double P50Ms { get; set; }
        public double P95Ms { get; set; }
        public double P99Ms { get; set; }
        public double MaxMs { get; set; }
    }

    sealed class SlowOperationRow
    {
        public string Label { get; set; }
        public int Count { get; set; }
        public double MeanMs { get; set; }
        public double P95Ms { get; set; }
        public double MaxMs { get; set; }
    }

    sealed class TsamCoverageRow
    {
        public string ToolName { get; set; }
        public string Action { get; set; }
        public int OperationCount { get; set; }
        public int RowCount { get; set; }
        public int NormalizationRows { get; set; }
        public int ServiceRows { get; set; }
        public int AdapterRows { get; set; }
        public int ResultShapingRows { get; set; }
        public int FailedRows { get; set; }
        public string[] ErrorKinds { get; set; }
        public long RequestBytes { get; set; }
        public long ResponseBytes { get; set; }
        public double TotalDurationMs { get; set; }
    }

    sealed class FailureClassRow
    {
        public string Stage { get; set; }
        public string Name { get; set; }
        public string ErrorKind { get; set; }
        public int Count { get; set; }
    }

    sealed class PayloadEntrySummaryRow
    {
        public DateTimeOffset? TimestampUtc { get; set; }
        public string Stage { get; set; }
        public string Name { get; set; }
        public long RawBytes { get; set; }
        public long ShapedBytes { get; set; }
        public string RepresentationKind { get; set; }
        public string PayloadClass { get; set; }
    }

    sealed class UsageFindingRow
    {
        public string Kind { get; set; }
        public string Severity { get; set; }
        public string Message { get; set; }

        public UsageFindingRow(string kind, string severity, string message)
        {
            Kind = kind;
            Severity = severity;
            Message = message;
        }
    }

    sealed class PayloadRow
    {
        public int LineNumber { get; set; }
        public DateTimeOffset? TimestampUtc { get; set; }
        public string EventKind { get; set; }
        public string Stage { get; set; }
        public string Name { get; set; }
        public long RawBytes { get; set; }
        public long ShapedBytes { get; set; }
        public string Hash { get; set; }
        public string NormalizedHash { get; set; }
        public string RunId { get; set; }
        public string ConnectionId { get; set; }
        public string RequestId { get; set; }
        public bool? Success { get; set; }
        public bool? Unchanged { get; set; }
        public string ErrorKind { get; set; }
        public string ErrorMessageShort { get; set; }
        public string CommandType { get; set; }
        public long RequestBytes { get; set; }
        public long ResponseBytes { get; set; }
        public double DurationMs { get; set; }
        public string RepresentationKind { get; set; }
        public string PayloadClass { get; set; }
        public string CacheReuseClass { get; set; }
        public string ToolName { get; set; }
        public string Action { get; set; }
        public string DiscoveryMode { get; set; }
        public string SnapshotReason { get; set; }
        public string SnapshotHashMinimal { get; set; }
        public string SnapshotHashFull { get; set; }
        public string BridgeSessionId { get; set; }
        public long ManifestVersion { get; set; }
        public string ManifestKind { get; set; }
        public string ManifestReason { get; set; }
        public string ActiveToolPacks { get; set; }
        public string ResponseStatus { get; set; }
    }
}
