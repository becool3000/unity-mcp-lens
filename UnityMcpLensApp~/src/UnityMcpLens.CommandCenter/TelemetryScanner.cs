using System.Globalization;
using System.IO;
using System.Text.Json;

namespace UnityMcpLens.CommandCenter;

public sealed class TelemetryScanner
{
    const int DefaultLastRows = 2000;
    const int TopItemCount = 8;

    readonly string m_ProjectRoot;

    public TelemetryScanner(string projectRoot)
    {
        m_ProjectRoot = string.IsNullOrWhiteSpace(projectRoot)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(projectRoot);
    }

    public string StatsPath => Path.Combine(m_ProjectRoot, "Library", "AI.Gateway.PayloadStats.jsonl");

    public TelemetrySnapshot Scan(int lastRows = DefaultLastRows)
    {
        string statsPath = StatsPath;
        if (!File.Exists(statsPath))
            return TelemetrySnapshot.Missing(statsPath);

        FileInfo info = new(statsPath);
        if (info.Length == 0)
            return TelemetrySnapshot.Empty(statsPath, info);

        lastRows = Math.Clamp(lastRows <= 0 ? DefaultLastRows : lastRows, 1, 100000);
        List<TelemetryLine> lines = ReadLatestLines(statsPath, lastRows, out int totalLineCount);
        List<TelemetryRow> rows = [];
        int skippedLines = 0;

        foreach (TelemetryLine line in lines)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(line.Text);
                rows.Add(TelemetryRow.FromJson(line.LineNumber, document.RootElement));
            }
            catch
            {
                skippedLines++;
            }
        }

        if (rows.Count == 0)
            return TelemetrySnapshot.NoValidRows(statsPath, info, totalLineCount, skippedLines, lines.Count > 0 ? lines[0].LineNumber : totalLineCount + 1);

        return BuildSnapshot(statsPath, info, totalLineCount, skippedLines, lines[0].LineNumber, rows);
    }

    static List<TelemetryLine> ReadLatestLines(string statsPath, int lastRows, out int totalLineCount)
    {
        Queue<TelemetryLine> queue = new(lastRows + 1);
        totalLineCount = 0;

        foreach (string line in File.ReadLines(statsPath))
        {
            totalLineCount++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            queue.Enqueue(new TelemetryLine(totalLineCount, line));
            if (queue.Count > lastRows)
                queue.Dequeue();
        }

        return queue.ToList();
    }

    static TelemetrySnapshot BuildSnapshot(string statsPath, FileInfo info, int totalLineCount, int skippedLines, int startLine, IReadOnlyList<TelemetryRow> rows)
    {
        List<TelemetryRow> scopedRows = rows.OrderBy(row => row.TimestampUtc ?? DateTimeOffset.MinValue).ToList();
        List<TelemetryRow> coverageRows = scopedRows.Where(IsCoverageRow).ToList();
        List<TelemetryRow> payloadRows = scopedRows.Where(row => !IsCoverageRow(row)).ToList();
        List<TelemetryRow> bridgeRequestRows = coverageRows
            .Where(row => string.Equals(row.Stage, "coverage_bridge_command_request", StringComparison.Ordinal))
            .ToList();
        List<TelemetryRow> bridgeResponseRows = coverageRows
            .Where(row => string.Equals(row.Stage, "coverage_bridge_command_response", StringComparison.Ordinal))
            .ToList();
        List<TelemetryRow> toolSnapshotRows = scopedRows
            .Where(row => string.Equals(row.EventKind, "tool_snapshot", StringComparison.Ordinal) ||
                string.Equals(row.Stage, "tool_snapshot", StringComparison.Ordinal))
            .ToList();

        long rawBytes = payloadRows.Sum(row => row.RawBytes);
        long shapedBytes = payloadRows.Sum(row => row.ShapedBytes);
        int payloadRowsWithSavings = payloadRows.Count(row => row.RawBytes > row.ShapedBytes);
        HashSet<string> responseKeys = new(
            bridgeResponseRows.Select(BuildBridgeRowKey).Where(key => !string.IsNullOrWhiteSpace(key)),
            StringComparer.Ordinal);
        List<TelemetryUnmatchedRequestRow> allUnmatchedRequests = bridgeRequestRows
            .Where(row => !string.IsNullOrWhiteSpace(BuildBridgeRowKey(row)) && !responseKeys.Contains(BuildBridgeRowKey(row)))
            .Select(row => new TelemetryUnmatchedRequestRow
            {
                TimestampUtc = FormatTimestamp(row.TimestampUtc),
                Command = string.IsNullOrWhiteSpace(row.CommandType) ? "(unknown)" : row.CommandType,
                ConnectionId = string.IsNullOrWhiteSpace(row.ConnectionId) ? "(none)" : row.ConnectionId,
                RequestId = string.IsNullOrWhiteSpace(row.RequestId) ? "(none)" : row.RequestId,
                RequestBytes = row.RequestBytes
            })
            .ToList();
        List<TelemetryUnmatchedRequestRow> unmatchedRequests = allUnmatchedRequests
            .Take(TopItemCount)
            .ToList();

        int minimalTransitions = 0;
        int fullTransitions = 0;
        int falseStableMinimalTransitions = 0;
        for (int i = 1; i < toolSnapshotRows.Count; i++)
        {
            TelemetryRow previous = toolSnapshotRows[i - 1];
            TelemetryRow current = toolSnapshotRows[i];
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

        List<TelemetryRow> latencyRows = scopedRows.Where(row => row.DurationMs > 0d).ToList();
        return new TelemetrySnapshot
        {
            StatsPath = statsPath,
            Exists = true,
            IsEmpty = false,
            FileSizeBytes = info.Length,
            LastWriteUtc = info.LastWriteTimeUtc,
            Scope = $"lastRows:{rows.Count}",
            TotalLineCount = totalLineCount,
            StartLine = startLine,
            NextLine = totalLineCount + 1,
            EntryCount = scopedRows.Count,
            PayloadEntryCount = payloadRows.Count,
            CoverageEntryCount = coverageRows.Count,
            SkippedLineCount = skippedLines,
            RawBytes = rawBytes,
            ShapedBytes = shapedBytes,
            RawTokens = EstimateTokens(rawBytes),
            ShapedTokens = EstimateTokens(shapedBytes),
            PayloadRowsWithSavings = payloadRowsWithSavings,
            BridgeRequestCount = bridgeRequestRows.Count,
            BridgeResponseCount = bridgeResponseRows.Count,
            BridgeConnectionCount = scopedRows
                .Where(row => !string.IsNullOrWhiteSpace(row.ConnectionId))
                .Select(row => row.ConnectionId)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            SetupCycleCount = CountSetupCycles(bridgeRequestRows),
            UnmatchedRequestCount = allUnmatchedRequests.Count,
            PackSetTransitionCount = bridgeResponseRows.Count(row => string.Equals(row.CommandType, "set_tool_packs", StringComparison.Ordinal)),
            ToolSnapshotCount = toolSnapshotRows.Count,
            MinimalHashTransitions = minimalTransitions,
            FullHashTransitions = fullTransitions,
            FalseStableMinimalTransitions = falseStableMinimalTransitions,
            FirstTimestampUtc = scopedRows.FirstOrDefault()?.TimestampUtc,
            LastTimestampUtc = scopedRows.LastOrDefault()?.TimestampUtc,
            TopSavings = CreateSummaryRows(payloadRows.Where(row => row.RawBytes > row.ShapedBytes), row => $"{EmptyLabel(row.Stage)}:{EmptyLabel(row.Name)}", bySavings: true),
            TopStages = CreateSummaryRows(payloadRows, row => EmptyLabel(row.Stage), bySavings: false),
            TopNames = CreateSummaryRows(payloadRows, row => EmptyLabel(row.Name), bySavings: false),
            SlowOperations = latencyRows
                .GroupBy(row => $"{EmptyLabel(row.Stage)}:{EmptyLabel(row.Name)}")
                .Select(group => CreateSlowOperationRow(group))
                .OrderByDescending(row => row.P95Ms)
                .ThenByDescending(row => row.MaxMs)
                .Take(TopItemCount)
                .ToList(),
            FailureClasses = scopedRows
                .Where(row => row.Success == false || !string.IsNullOrWhiteSpace(row.ErrorKind))
                .GroupBy(row => $"{EmptyLabel(row.Stage)}|{EmptyLabel(row.Name)}|{EmptyLabel(row.ErrorKind)}")
                .Select(group =>
                {
                    TelemetryRow first = group.First();
                    return new TelemetryFailureClassRow
                    {
                        Stage = EmptyLabel(first.Stage),
                        Name = EmptyLabel(first.Name),
                        ErrorKind = EmptyLabel(first.ErrorKind),
                        Count = group.Count()
                    };
                })
                .OrderByDescending(row => row.Count)
                .Take(TopItemCount)
                .ToList(),
            UnmatchedRequests = unmatchedRequests,
            StatusMessage = "Telemetry loaded."
        };
    }

    static List<TelemetrySummaryRow> CreateSummaryRows(IEnumerable<TelemetryRow> rows, Func<TelemetryRow, string> keySelector, bool bySavings)
    {
        return rows
            .GroupBy(keySelector)
            .Select(group => new TelemetrySummaryRow
            {
                Label = group.Key,
                Count = group.Count(),
                RawBytes = group.Sum(row => row.RawBytes),
                ShapedBytes = group.Sum(row => row.ShapedBytes)
            })
            .OrderByDescending(row => bySavings ? row.SavedBytes : row.RawBytes)
            .ThenByDescending(row => row.Count)
            .Take(TopItemCount)
            .ToList();
    }

    static TelemetrySlowOperationRow CreateSlowOperationRow(IGrouping<string, TelemetryRow> group)
    {
        List<double> values = group.Select(row => row.DurationMs).Where(value => value > 0d).OrderBy(value => value).ToList();
        return new TelemetrySlowOperationRow
        {
            Label = group.Key,
            Count = values.Count,
            MeanMs = values.Count == 0 ? 0d : Math.Round(values.Average(), 2),
            P95Ms = Percentile(values, 0.95d),
            MaxMs = values.Count == 0 ? 0d : Math.Round(values[^1], 2)
        };
    }

    static bool IsCoverageRow(TelemetryRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.Stage) && row.Stage.StartsWith("coverage_", StringComparison.Ordinal))
            return true;

        return string.Equals(row.EventKind, "coverage", StringComparison.Ordinal) ||
            string.Equals(row.EventKind, "bridge_coverage", StringComparison.Ordinal);
    }

    static int CountSetupCycles(IReadOnlyList<TelemetryRow> bridgeRequestRows)
    {
        int count = 0;
        foreach (IGrouping<string, TelemetryRow> connectionGroup in bridgeRequestRows
            .OrderBy(row => row.TimestampUtc ?? DateTimeOffset.MinValue)
            .GroupBy(row => row.ConnectionId ?? string.Empty))
        {
            List<TelemetryRow> requests = connectionGroup.OrderBy(row => row.TimestampUtc ?? DateTimeOffset.MinValue).ToList();
            foreach (TelemetryRow registerRow in requests.Where(row => string.Equals(row.CommandType, "register_client", StringComparison.Ordinal)))
            {
                if (!registerRow.TimestampUtc.HasValue)
                    continue;

                TelemetryRow? manifestRow = requests.FirstOrDefault(row =>
                    row.TimestampUtc.HasValue &&
                    row.TimestampUtc.Value > registerRow.TimestampUtc.Value &&
                    string.Equals(row.CommandType, "get_manifest", StringComparison.Ordinal));
                if (manifestRow?.TimestampUtc == null)
                    continue;

                if (requests.Any(row =>
                    row.TimestampUtc.HasValue &&
                    row.TimestampUtc.Value > manifestRow.TimestampUtc.Value &&
                    string.Equals(row.CommandType, "get_tool_schema", StringComparison.Ordinal)))
                {
                    count++;
                }
            }
        }

        return count;
    }

    static string BuildBridgeRowKey(TelemetryRow row)
    {
        if (string.IsNullOrWhiteSpace(row.RequestId))
            return string.Empty;

        return $"{row.ConnectionId}|{row.RequestId}";
    }

    static long EstimateTokens(long bytes) => Math.Max(0L, (long)Math.Ceiling(bytes / 4.0d));

    static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
            return 0d;

        int index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        index = Math.Clamp(index, 0, sortedValues.Count - 1);
        return Math.Round(sortedValues[index], 2);
    }

    static string EmptyLabel(string? value) => string.IsNullOrWhiteSpace(value) ? "(none)" : value;

    static string FormatTimestamp(DateTimeOffset? timestamp) => timestamp.HasValue ? timestamp.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "unknown";

    readonly record struct TelemetryLine(int LineNumber, string Text);
}

public sealed class TelemetrySnapshot
{
    public string StatsPath { get; init; } = string.Empty;
    public bool Exists { get; init; }
    public bool IsEmpty { get; init; }
    public long FileSizeBytes { get; init; }
    public DateTime LastWriteUtc { get; init; }
    public string Scope { get; init; } = string.Empty;
    public int TotalLineCount { get; init; }
    public int StartLine { get; init; }
    public int NextLine { get; init; }
    public int EntryCount { get; init; }
    public int PayloadEntryCount { get; init; }
    public int CoverageEntryCount { get; init; }
    public int SkippedLineCount { get; init; }
    public long RawBytes { get; init; }
    public long ShapedBytes { get; init; }
    public long SavedBytes => Math.Max(0L, RawBytes - ShapedBytes);
    public long RawTokens { get; init; }
    public long ShapedTokens { get; init; }
    public long SavedTokens => Math.Max(0L, RawTokens - ShapedTokens);
    public int PayloadRowsWithSavings { get; init; }
    public double SavingsPct => RawBytes <= 0 ? 0d : Math.Round((SavedBytes / (double)RawBytes) * 100d, 2);
    public int BridgeRequestCount { get; init; }
    public int BridgeResponseCount { get; init; }
    public int BridgeConnectionCount { get; init; }
    public int SetupCycleCount { get; init; }
    public int UnmatchedRequestCount { get; init; }
    public int PackSetTransitionCount { get; init; }
    public int ToolSnapshotCount { get; init; }
    public int MinimalHashTransitions { get; init; }
    public int FullHashTransitions { get; init; }
    public int FalseStableMinimalTransitions { get; init; }
    public DateTimeOffset? FirstTimestampUtc { get; init; }
    public DateTimeOffset? LastTimestampUtc { get; init; }
    public List<TelemetrySummaryRow> TopSavings { get; init; } = [];
    public List<TelemetrySummaryRow> TopStages { get; init; } = [];
    public List<TelemetrySummaryRow> TopNames { get; init; } = [];
    public List<TelemetrySlowOperationRow> SlowOperations { get; init; } = [];
    public List<TelemetryFailureClassRow> FailureClasses { get; init; } = [];
    public List<TelemetryUnmatchedRequestRow> UnmatchedRequests { get; init; } = [];
    public string StatusMessage { get; init; } = string.Empty;

    public string FileSizeDisplay => Exists ? FormatBytes(FileSizeBytes) : "missing";
    public string LastWriteDisplay => Exists && LastWriteUtc != DateTime.MinValue ? LastWriteUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "unknown";
    public string RowSummaryDisplay => Exists ? $"{EntryCount:N0} scoped rows, {PayloadEntryCount:N0} payload, {CoverageEntryCount:N0} coverage, {SkippedLineCount:N0} skipped" : "No telemetry file found.";
    public string PayloadSummaryDisplay => $"{FormatBytes(RawBytes)} raw -> {FormatBytes(ShapedBytes)} shaped, saved {FormatBytes(SavedBytes)} / {SavedTokens:N0} tokens ({SavingsPct:0.00}%)";
    public string BridgeSummaryDisplay => $"{BridgeRequestCount:N0} requests, {BridgeResponseCount:N0} responses, {BridgeConnectionCount:N0} connections, {SetupCycleCount:N0} setup cycles, {UnmatchedRequestCount:N0} unmatched, {PackSetTransitionCount:N0} pack transitions";
    public string SnapshotSummaryDisplay => $"{ToolSnapshotCount:N0} snapshot rows, {MinimalHashTransitions:N0} minimal transitions, {FullHashTransitions:N0} full transitions, {FalseStableMinimalTransitions:N0} false-stable minimal transitions";
    public string DateRangeDisplay => FirstTimestampUtc.HasValue && LastTimestampUtc.HasValue
        ? $"{FirstTimestampUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss} -> {LastTimestampUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
        : "unknown";

    public static TelemetrySnapshot Missing(string statsPath) => new()
    {
        StatsPath = statsPath,
        Exists = false,
        StatusMessage = "Telemetry file not found. Trigger Lens activity in this project to collect usage data."
    };

    public static TelemetrySnapshot Empty(string statsPath, FileInfo info) => new()
    {
        StatsPath = statsPath,
        Exists = true,
        IsEmpty = true,
        FileSizeBytes = 0,
        LastWriteUtc = info.LastWriteTimeUtc,
        StatusMessage = "Telemetry file is empty."
    };

    public static TelemetrySnapshot NoValidRows(string statsPath, FileInfo info, int totalLineCount, int skippedLines, int startLine) => new()
    {
        StatsPath = statsPath,
        Exists = true,
        FileSizeBytes = info.Length,
        LastWriteUtc = info.LastWriteTimeUtc,
        TotalLineCount = totalLineCount,
        StartLine = startLine,
        NextLine = totalLineCount + 1,
        SkippedLineCount = skippedLines,
        StatusMessage = "No valid telemetry rows were found in the selected scope."
    };

    public string BuildClipboardSummary()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "Unity MCP Lens Telemetry",
            $"Stats path: {StatsPath}",
            $"Status: {StatusMessage}",
            $"Scope: {Scope}",
            $"Rows: {RowSummaryDisplay}",
            $"Date range: {DateRangeDisplay}",
            $"Payload: {PayloadSummaryDisplay}",
            $"Bridge: {BridgeSummaryDisplay}",
            $"Tool snapshots: {SnapshotSummaryDisplay}"
        });
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = Math.Max(0L, bytes);
        int unit = 0;
        while (value >= 1024d && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.0} {units[unit]}";
    }
}

public sealed class TelemetrySummaryRow
{
    public string Label { get; init; } = string.Empty;
    public int Count { get; init; }
    public long RawBytes { get; init; }
    public long ShapedBytes { get; init; }
    public long SavedBytes => Math.Max(0L, RawBytes - ShapedBytes);
    public double SavingsPct => RawBytes <= 0 ? 0d : Math.Round((SavedBytes / (double)RawBytes) * 100d, 2);
    public string RawBytesDisplay => TelemetrySnapshot.FormatBytes(RawBytes);
    public string ShapedBytesDisplay => TelemetrySnapshot.FormatBytes(ShapedBytes);
    public string SavedBytesDisplay => TelemetrySnapshot.FormatBytes(SavedBytes);
    public string SavingsPctDisplay => $"{SavingsPct:0.00}%";
}

public sealed class TelemetrySlowOperationRow
{
    public string Label { get; init; } = string.Empty;
    public int Count { get; init; }
    public double MeanMs { get; init; }
    public double P95Ms { get; init; }
    public double MaxMs { get; init; }
}

public sealed class TelemetryFailureClassRow
{
    public string Stage { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ErrorKind { get; init; } = string.Empty;
    public int Count { get; init; }
}

public sealed class TelemetryUnmatchedRequestRow
{
    public string TimestampUtc { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public string ConnectionId { get; init; } = string.Empty;
    public string RequestId { get; init; } = string.Empty;
    public long RequestBytes { get; init; }
}

sealed class TelemetryRow
{
    public int LineNumber { get; init; }
    public DateTimeOffset? TimestampUtc { get; init; }
    public string EventKind { get; init; } = string.Empty;
    public string Stage { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public long RawBytes { get; init; }
    public long ShapedBytes { get; init; }
    public string ConnectionId { get; init; } = string.Empty;
    public string RequestId { get; init; } = string.Empty;
    public bool? Success { get; init; }
    public bool Unchanged { get; init; }
    public string ErrorKind { get; init; } = string.Empty;
    public string CommandType { get; init; } = string.Empty;
    public long RequestBytes { get; init; }
    public double DurationMs { get; init; }
    public string SnapshotHashMinimal { get; init; } = string.Empty;
    public string SnapshotHashFull { get; init; } = string.Empty;

    public static TelemetryRow FromJson(int lineNumber, JsonElement entry)
    {
        return new TelemetryRow
        {
            LineNumber = lineNumber,
            TimestampUtc = ReadDate(entry, "timestampUtc"),
            EventKind = ReadString(entry, "eventKind"),
            Stage = ReadString(entry, "stage", ReadNestedString(entry, "meta", "stage")),
            Name = ReadString(entry, "name"),
            RawBytes = ReadLong(entry, "rawBytes"),
            ShapedBytes = ReadLong(entry, "shapedBytes"),
            ConnectionId = ReadString(entry, "connectionId", ReadNestedString(entry, "meta", "connectionId")),
            RequestId = ReadString(entry, "requestId", ReadNestedString(entry, "meta", "requestId")),
            Success = ReadNullableBool(entry, "success"),
            Unchanged = ReadBool(entry, "unchanged"),
            ErrorKind = ReadString(entry, "errorKind"),
            CommandType = ReadString(entry, "commandType", ReadString(entry, "name")),
            RequestBytes = ReadLong(entry, "requestBytes", ReadNestedLong(entry, "meta", "payloadBytes")),
            DurationMs = ReadDouble(entry, "durationMs"),
            SnapshotHashMinimal = ReadString(entry, "snapshotHashMinimal"),
            SnapshotHashFull = ReadString(entry, "snapshotHashFull")
        };
    }

    static DateTimeOffset? ReadDate(JsonElement entry, string name)
    {
        string value = ReadString(entry, name);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
            ? parsed
            : null;
    }

    static string ReadString(JsonElement entry, string name, string fallback = "")
    {
        if (!entry.TryGetProperty(name, out JsonElement value))
            return fallback;

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : value.ToString();
    }

    static string ReadNestedString(JsonElement entry, string parent, string name)
    {
        return entry.TryGetProperty(parent, out JsonElement parentElement) && parentElement.ValueKind == JsonValueKind.Object
            ? ReadString(parentElement, name)
            : string.Empty;
    }

    static long ReadLong(JsonElement entry, string name, long fallback = 0)
    {
        if (!entry.TryGetProperty(name, out JsonElement value))
            return fallback;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long parsed))
            return parsed;

        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
            ? parsed
            : fallback;
    }

    static long ReadNestedLong(JsonElement entry, string parent, string name)
    {
        return entry.TryGetProperty(parent, out JsonElement parentElement) && parentElement.ValueKind == JsonValueKind.Object
            ? ReadLong(parentElement, name)
            : 0;
    }

    static double ReadDouble(JsonElement entry, string name)
    {
        if (!entry.TryGetProperty(name, out JsonElement value))
            return 0d;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double parsed))
            return parsed;

        return value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
            ? parsed
            : 0d;
    }

    static bool ReadBool(JsonElement entry, string name)
    {
        return ReadNullableBool(entry, name) == true;
    }

    static bool? ReadNullableBool(JsonElement entry, string name)
    {
        if (!entry.TryGetProperty(name, out JsonElement value))
            return null;

        if (value.ValueKind == JsonValueKind.True)
            return true;
        if (value.ValueKind == JsonValueKind.False)
            return false;

        return value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out bool parsed)
            ? parsed
            : null;
    }
}
