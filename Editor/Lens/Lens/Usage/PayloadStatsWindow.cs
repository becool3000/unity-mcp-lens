using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Lens.Usage
{
    class PayloadStatsWindow : EditorWindow
    {
        const string k_WindowName = "Lens Usage";
        const int k_TopCount = 8;
        const double k_CoverageDominatedThresholdPct = 95.0d;

        Vector2 m_ScrollPosition;
        PayloadStatsReport m_Report;
        string m_ErrorMessage;
        bool m_ShowMissingFileHelp;
        DateTime m_LastRefreshUtc;

        [MenuItem("Tools/Unity MCP Lens/Usage Report", false, 1040)]
        static PayloadStatsWindow ShowWindow()
        {
            var window = GetWindow<PayloadStatsWindow>();
            window.titleContent = new GUIContent(k_WindowName);
            window.minSize = new Vector2(540f, 360f);
            window.Show();
            return window;
        }

        void OnEnable()
        {
            RefreshReport();
        }

        void OnFocus()
        {
            RefreshReport();
        }

        void OnGUI()
        {
            DrawToolbar();

            if (!string.IsNullOrWhiteSpace(m_ErrorMessage))
            {
                EditorGUILayout.HelpBox(m_ErrorMessage, MessageType.Info);
                if (m_ShowMissingFileHelp)
                    DrawMissingFileHelp();
                return;
            }

            if (m_Report == null)
            {
                EditorGUILayout.HelpBox("No usage data is loaded for the current project.", MessageType.Info);
                return;
            }

            if (m_Report.CoverageDominated)
            {
                EditorGUILayout.HelpBox(
                    $"Coverage-dominated log: {m_Report.CoveragePct:F2}% of rows are coverage events. This project log is mostly bridge/tool polling, refresh, and command traffic rather than novel payload rows.",
                    MessageType.Warning);
            }

            m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);
            DrawProjectSummary();
            EditorGUILayout.Space(8f);
            DrawTotalsSection();
            EditorGUILayout.Space(8f);
            DrawBridgeSection();
            EditorGUILayout.Space(8f);
            DrawBridgeCommandsSection();
            EditorGUILayout.Space(8f);
            DrawSessionChurnSection();
            EditorGUILayout.Space(8f);
            DrawChurnSection();
            EditorGUILayout.Space(8f);
            DrawTopRows("Top Payload Stages", m_Report.TopStages);
            EditorGUILayout.Space(8f);
            DrawTopRows("Top Payload Names", m_Report.TopNames);
            EditorGUILayout.Space(8f);
            DrawRunSummaries();
            EditorGUILayout.EndScrollView();
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                    RefreshReport();

                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(GetStatsPath())))
                {
                    if (GUILayout.Button("Reveal Stats File", EditorStyles.toolbarButton, GUILayout.Width(110f)))
                    {
                        var statsPath = GetStatsPath();
                        if (!string.IsNullOrWhiteSpace(statsPath) && File.Exists(statsPath))
                            EditorUtility.RevealInFinder(statsPath);
                    }
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(GetStatsPath())))
                {
                    if (GUILayout.Button("Reset Stats", EditorStyles.toolbarButton, GUILayout.Width(85f)))
                        ResetStatsFile();
                }

                using (new EditorGUI.DisabledScope(m_Report == null))
                {
                    if (GUILayout.Button("Copy Summary", EditorStyles.toolbarButton, GUILayout.Width(95f)))
                        CopySummaryToClipboard();
                }

                GUILayout.FlexibleSpace();

                var refreshedText = m_LastRefreshUtc == default
                    ? "Not refreshed yet"
                    : $"Last refresh: {m_LastRefreshUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
                GUILayout.Label(refreshedText, EditorStyles.miniLabel);
            }
        }

        void CopySummaryToClipboard()
        {
            if (m_Report == null)
                return;

            EditorGUIUtility.systemCopyBuffer = BuildClipboardReport(m_Report);
            ShowNotification(new GUIContent("Lens usage summary copied"));
        }

        void ResetStatsFile()
        {
            var statsPath = GetStatsPath();
            if (string.IsNullOrWhiteSpace(statsPath))
                return;

            var confirmed = EditorUtility.DisplayDialog(
                "Reset Lens Usage",
                $"Clear the Lens usage log for the current project?\n\n{statsPath}\n\nThis only resets the current project's telemetry file.",
                "Reset",
                "Cancel");

            if (!confirmed)
                return;

            try
            {
                var directory = Path.GetDirectoryName(statsPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(statsPath, string.Empty, new UTF8Encoding(false));
                RefreshReport();
                ShowNotification(new GUIContent("Lens usage log reset"));
            }
            catch (Exception ex)
            {
                m_Report = null;
                m_ErrorMessage = $"Failed to reset Lens usage stats.\n{ex.Message}";
                m_ShowMissingFileHelp = false;
            }
        }

        void DrawProjectSummary()
        {
            DrawSectionHeader("Project Scope");
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Scope", "Current Unity project only");
                EditorGUILayout.SelectableLabel(m_Report.StatsPath, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUILayout.LabelField("Entries", m_Report.EntryCount.ToString(CultureInfo.InvariantCulture));
                EditorGUILayout.LabelField("Payload Rows", m_Report.PayloadEntryCount.ToString(CultureInfo.InvariantCulture));
                EditorGUILayout.LabelField("Coverage Rows", $"{m_Report.CoverageEntryCount.ToString(CultureInfo.InvariantCulture)} ({m_Report.CoveragePct:F2}%)");

                var dateRange = m_Report.FirstTimestampUtc.HasValue && m_Report.LastTimestampUtc.HasValue
                    ? $"{m_Report.FirstTimestampUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss} -> {m_Report.LastTimestampUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
                    : "No timestamps recorded";
                EditorGUILayout.LabelField("Date Range", dateRange);
            }
        }

        void DrawTotalsSection()
        {
            DrawSectionHeader("Payload Totals");
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Estimated Raw Tokens", FormatNumber(m_Report.RawTokens));
                EditorGUILayout.LabelField("Estimated Shaped Tokens", FormatNumber(m_Report.ShapedTokens));
                EditorGUILayout.LabelField("Estimated Saved Tokens", $"{FormatNumber(m_Report.SavedTokens)} ({m_Report.SavingsPct:F2}%)");
                EditorGUILayout.LabelField("Raw Bytes", FormatBytes(m_Report.RawBytes));
                EditorGUILayout.LabelField("Shaped Bytes", FormatBytes(m_Report.ShapedBytes));
                EditorGUILayout.LabelField("Shaping Scope", m_Report.ShapingApplicability?.Summary ?? "Payload rows are shaping eligible; coverage rows are shaping n/a.");
                EditorGUILayout.LabelField("Eligible Payload Rows", FormatNumber(m_Report.ShapingApplicability?.PayloadRowsEligible ?? m_Report.PayloadEntryCount));
                EditorGUILayout.LabelField("Coverage Rows Shaping", $"n/a ({FormatNumber(m_Report.ShapingApplicability?.CoverageRowsNotApplicable ?? m_Report.CoverageEntryCount)} rows)");
                EditorGUILayout.LabelField("Exact Duplicate Raw Estimate", $"{FormatBytes(m_Report.ExactRepeatedRawBytes)} ({m_Report.ExactRepeatedRawPct:F2}%)");
                EditorGUILayout.LabelField("Normalized Duplicate Raw Estimate", $"{FormatBytes(m_Report.NormalizedRepeatedRawBytes)} ({m_Report.NormalizedRepeatedRawPct:F2}%)");
            }
        }

        void DrawBridgeSection()
        {
            DrawSectionHeader("Bridge Coverage");
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Bridge Requests", FormatNumber(m_Report.BridgeRequestCount));
                EditorGUILayout.LabelField("Bridge Responses", FormatNumber(m_Report.BridgeResponseCount));
                EditorGUILayout.LabelField("Bridge Connections", FormatNumber(m_Report.BridgeConnectionCount));
                EditorGUILayout.LabelField("Unmatched Requests", FormatNumber(m_Report.UnmatchedRequests?.Count ?? 0));
                EditorGUILayout.LabelField("Bridge Request Bytes", FormatBytes(m_Report.BridgeRequestBytes));
                EditorGUILayout.LabelField("Bridge Response Bytes", FormatBytes(m_Report.BridgeResponseBytes));
            }
        }

        void DrawBridgeCommandsSection()
        {
            DrawSectionHeader("Top Bridge Commands");
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (m_Report.BridgeTopCommands == null || m_Report.BridgeTopCommands.Count == 0)
                {
                    EditorGUILayout.LabelField("No bridge command rows recorded.");
                    return;
                }

                foreach (var command in m_Report.BridgeTopCommands)
                {
                    EditorGUILayout.LabelField(
                        command.Label,
                        $"count {command.Count}, request bytes {FormatBytes(command.RequestBytes)}");
                }
            }
        }

        void DrawSessionChurnSection()
        {
            DrawSectionHeader("Session Churn");
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Connections", FormatNumber(m_Report.BridgeConnectionCount));
                EditorGUILayout.LabelField("Setup Cycles", FormatNumber(m_Report.SetupCycleCount));
                EditorGUILayout.LabelField("Pack-Set Transitions", FormatNumber(m_Report.PackSetTransitions?.Count ?? 0));
                EditorGUILayout.LabelField("Unmatched Requests", FormatNumber(m_Report.UnmatchedRequests?.Count ?? 0));

                if (m_Report.ConnectionSummaries != null && m_Report.ConnectionSummaries.Count > 0)
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.LabelField("Connections", EditorStyles.boldLabel);
                    foreach (var connection in m_Report.ConnectionSummaries.Take(k_TopCount))
                    {
                        EditorGUILayout.LabelField(
                            connection.ConnectionId,
                            $"requests {connection.RequestCount}, responses {connection.ResponseCount}, unmatched {connection.UnmatchedRequestCount}, top {connection.TopCommand}");
                    }
                }

                if (m_Report.PackSetTransitions != null && m_Report.PackSetTransitions.Count > 0)
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.LabelField("Pack Sets", EditorStyles.boldLabel);
                    foreach (var transition in m_Report.PackSetTransitions.Take(k_TopCount))
                    {
                        EditorGUILayout.LabelField(
                            transition.ConnectionId,
                            $"{transition.ActiveToolPacks} -> {transition.ManifestKind}, unchanged {transition.Unchanged}, response {FormatBytes(transition.ResponseBytes)}");
                    }
                }

                if (m_Report.UnmatchedRequests != null && m_Report.UnmatchedRequests.Count > 0)
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.LabelField("Unmatched", EditorStyles.boldLabel);
                    foreach (var request in m_Report.UnmatchedRequests.Take(k_TopCount))
                    {
                        EditorGUILayout.LabelField(
                            request.CommandType,
                            $"{request.ConnectionId}, {ShortId(request.RequestId)}, {request.Classification}");
                    }
                }
            }
        }

        void DrawChurnSection()
        {
            DrawSectionHeader("Tool Snapshot Churn");
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Snapshot Rows", FormatNumber(m_Report.ToolSnapshotCount));
                EditorGUILayout.LabelField("Minimal Hash Transitions", FormatNumber(m_Report.MinimalHashTransitions));
                EditorGUILayout.LabelField("Full Hash Transitions", FormatNumber(m_Report.FullHashTransitions));
                EditorGUILayout.LabelField("False-Stable Minimal Transitions", FormatNumber(m_Report.FalseStableMinimalTransitions));
            }
        }

        void DrawTopRows(string label, IReadOnlyList<PayloadSummaryRow> rows)
        {
            DrawSectionHeader(label);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (rows == null || rows.Count == 0)
                {
                    EditorGUILayout.LabelField("No data recorded.");
                    return;
                }

                foreach (var row in rows)
                {
                    EditorGUILayout.LabelField(
                        row.Label,
                        $"{FormatBytes(row.RawBytes)} raw -> {FormatBytes(row.ShapedBytes)} shaped, saved {FormatBytes(row.SavedBytes)} ({row.SavingsPct:F2}%), count {row.Count}");
                }
            }
        }

        void DrawRunSummaries()
        {
            DrawSectionHeader("Top Runs");
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (m_Report.RunSummaries == null || m_Report.RunSummaries.Count == 0)
                {
                    EditorGUILayout.LabelField("No run IDs recorded yet.");
                    return;
                }

                foreach (var summary in m_Report.RunSummaries)
                {
                    EditorGUILayout.LabelField(
                        summary.Label,
                        $"rows {summary.Count}, raw {FormatBytes(summary.RawBytes)}, failures {summary.FailureCount}");
                }
            }
        }

        void DrawMissingFileHelp()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("What this means", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("This package writes usage stats per host project to Library/AI.Gateway.PayloadStats.jsonl.");
                EditorGUILayout.LabelField("If the file is missing, either this project is not resolving the local fork yet, or no instrumented Lens/MCP/tool path has run.");
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("Check", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("1. Packages/manifest.json points com.becool3000.unity-mcp-lens at this local fork.");
                EditorGUILayout.LabelField("2. Unity finished recompiling after the package change.");
                EditorGUILayout.LabelField("3. Tools/Unity MCP Lens/Usage Report opens in this project.");
                EditorGUILayout.LabelField("4. You trigger MCP bridge activity or an instrumented Lens tool call at least once.");
            }
        }

        void RefreshReport()
        {
            m_LastRefreshUtc = DateTime.UtcNow;
            try
            {
                var statsPath = GetStatsPath();
                if (string.IsNullOrWhiteSpace(statsPath) || !File.Exists(statsPath))
                {
                    m_Report = null;
                    m_ErrorMessage = $"No stats file found for the current project at:\n{statsPath}";
                    m_ShowMissingFileHelp = true;
                    return;
                }

                if (new FileInfo(statsPath).Length == 0)
                {
                    m_Report = null;
                    m_ErrorMessage = $"Lens usage log is empty for the current project.\nTrigger MCP bridge activity or an instrumented Lens tool call to collect fresh data at:\n{statsPath}";
                    m_ShowMissingFileHelp = false;
                    return;
                }

                m_Report = LoadReport(statsPath);
                m_ErrorMessage = null;
                m_ShowMissingFileHelp = false;
            }
            catch (Exception ex)
            {
                m_Report = null;
                m_ErrorMessage = $"Failed to read Lens usage stats.\n{ex.Message}";
                m_ShowMissingFileHelp = false;
            }
        }

        static PayloadStatsReport LoadReport(string statsPath)
        {
            var rows = new List<PayloadRow>();
            foreach (var line in File.ReadLines(statsPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var entry = JObject.Parse(line);
                    rows.Add(new PayloadRow
                    {
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
                        CommandType = ReadString(entry["commandType"], ReadString(entry["name"])),
                        RequestBytes = ReadLong(entry["requestBytes"], ReadLong(entry.SelectToken("meta.payloadBytes"))),
                        ResponseBytes = ReadLong(entry["responseBytes"]),
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
                    // Ignore malformed lines to keep the window resilient inside the editor.
                }
            }

            if (rows.Count == 0)
                throw new InvalidOperationException("No valid payload stat rows were found.");

            var coverageRows = rows.Where(IsCoverageRow).ToList();
            var payloadRows = rows.Where(row => !IsCoverageRow(row)).ToList();
            var orderedRows = rows.OrderBy(row => row.TimestampUtc ?? DateTimeOffset.MinValue).ToList();

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
                .Take(k_TopCount)
                .ToList();

            var responseKeys = new HashSet<string>(
                bridgeResponseRows
                    .Select(BuildBridgeRowKey)
                    .Where(key => !string.IsNullOrWhiteSpace(key)),
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
            var connectionSummaries = rows
                .Where(row => !string.IsNullOrWhiteSpace(row.ConnectionId))
                .GroupBy(row => row.ConnectionId)
                .Select(group =>
                {
                    var connectionRows = group.OrderBy(row => row.TimestampUtc ?? DateTimeOffset.MinValue).ToList();
                    var requests = connectionRows.Where(row => string.Equals(row.Stage, "coverage_bridge_command_request", StringComparison.Ordinal)).ToList();
                    var responses = connectionRows.Where(row => string.Equals(row.Stage, "coverage_bridge_command_response", StringComparison.Ordinal)).ToList();
                    var topCommand = requests
                        .GroupBy(row => string.IsNullOrWhiteSpace(row.CommandType) ? "(unknown)" : row.CommandType)
                        .OrderByDescending(commandGroup => commandGroup.Count())
                        .Select(commandGroup => commandGroup.Key)
                        .FirstOrDefault() ?? "(none)";
                    return new ConnectionSummaryRow
                    {
                        ConnectionId = group.Key,
                        FirstUtc = connectionRows.FirstOrDefault()?.TimestampUtc,
                        LastUtc = connectionRows.LastOrDefault()?.TimestampUtc,
                        RequestCount = requests.Count,
                        ResponseCount = responses.Count,
                        UnmatchedRequestCount = unmatchedRequests.Count(request => string.Equals(request.ConnectionId, group.Key, StringComparison.Ordinal)),
                        TopCommand = topCommand
                    };
                })
                .OrderBy(row => row.FirstUtc ?? DateTimeOffset.MinValue)
                .ToList();
            var setupCycleCount = CountSetupCycles(bridgeRequestRows);
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
            var toolSnapshotRows = rows.Where(row =>
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

            return new PayloadStatsReport
            {
                StatsPath = statsPath,
                EntryCount = rows.Count,
                PayloadEntryCount = payloadRows.Count,
                CoverageEntryCount = coverageRows.Count,
                CoveragePct = Percent(coverageRows.Count, rows.Count),
                FirstTimestampUtc = orderedRows.FirstOrDefault()?.TimestampUtc,
                LastTimestampUtc = orderedRows.LastOrDefault()?.TimestampUtc,
                RawBytes = payloadRows.Sum(row => row.RawBytes),
                ShapedBytes = payloadRows.Sum(row => row.ShapedBytes),
                RawTokens = payloadRows.Sum(row => EstimateTokensFromBytes(row.RawBytes)),
                ShapedTokens = payloadRows.Sum(row => EstimateTokensFromBytes(row.ShapedBytes)),
                ShapingApplicability = CreateShapingApplicability(payloadRows.Count, coverageRows.Count, payloadRows.Sum(row => row.RawBytes), payloadRows.Sum(row => row.ShapedBytes), payloadRows.Count(row => row.RawBytes > row.ShapedBytes)),
                ExactRepeatedRawBytes = exactRepeatedRawBytes,
                ExactRepeatedRawPct = Percent(exactRepeatedRawBytes, payloadRows.Sum(row => row.RawBytes)),
                NormalizedRepeatedRawBytes = normalizedRepeatedRawBytes,
                NormalizedRepeatedRawPct = Percent(normalizedRepeatedRawBytes, payloadRows.Sum(row => row.RawBytes)),
                BridgeRequestCount = bridgeRequestRows.Count,
                BridgeResponseCount = bridgeResponseRows.Count,
                BridgeRequestBytes = bridgeRequestRows.Sum(row => row.RequestBytes),
                BridgeResponseBytes = bridgeResponseRows.Sum(row => row.ResponseBytes),
                BridgeTopCommands = bridgeTopCommands,
                BridgeConnectionCount = connectionSummaries.Count,
                ConnectionSummaries = connectionSummaries,
                SetupCycleCount = setupCycleCount,
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
                    .Take(k_TopCount)
                    .ToList(),
                TopNames = payloadRows
                    .GroupBy(row => string.IsNullOrWhiteSpace(row.Name) ? "(none)" : row.Name)
                    .Select(CreateSummaryRow)
                    .OrderByDescending(row => row.RawBytes)
                    .Take(k_TopCount)
                    .ToList(),
                RunSummaries = rows
                    .GroupBy(row => string.IsNullOrWhiteSpace(row.RunId) ? "(none)" : row.RunId)
                    .Select(group => new RunSummaryRow
                    {
                        Label = group.Key,
                        Count = group.Count(),
                        RawBytes = group.Sum(row => row.RawBytes),
                        FailureCount = group.Count(row => row.Success == false || !string.IsNullOrWhiteSpace(row.ErrorKind))
                    })
                    .OrderByDescending(row => row.Count)
                    .Take(k_TopCount)
                    .ToList()
            };
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

        static bool IsCoverageRow(PayloadRow row)
        {
            if (row == null)
                return false;

            if (!string.IsNullOrWhiteSpace(row.Stage) && row.Stage.StartsWith("coverage_", StringComparison.Ordinal))
                return true;

            return string.Equals(row.EventKind, "coverage", StringComparison.Ordinal) ||
                string.Equals(row.EventKind, "bridge_coverage", StringComparison.Ordinal);
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

        static string GetStatsPath()
        {
            var dataPath = Application.dataPath;
            var projectRoot = !string.IsNullOrWhiteSpace(dataPath)
                ? Directory.GetParent(dataPath)?.FullName
                : Directory.GetCurrentDirectory();
            if (string.IsNullOrWhiteSpace(projectRoot))
                projectRoot = Directory.GetCurrentDirectory();

            return Path.Combine(projectRoot, "Library", "AI.Gateway.PayloadStats.jsonl");
        }

        static DateTimeOffset? ParseDate(JToken token)
        {
            var value = ReadString(token);
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : null;
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

        static bool? ReadBool(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            if (token.Type == JTokenType.Boolean)
                return token.Value<bool>();

            return bool.TryParse(token.ToString(), out var parsed)
                ? parsed
                : null;
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

        static string FormatNumber(long value)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }

        static string ShortId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length <= 10)
                return value ?? string.Empty;

            return value.Substring(0, 10);
        }

        static string BuildClipboardReport(PayloadStatsReport report)
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

            AppendBridgeCommands(builder, report.BridgeTopCommands);
            AppendConnectionSummaries(builder, report.ConnectionSummaries);
            AppendPackSetTransitions(builder, report.PackSetTransitions);
            AppendUnmatchedRequests(builder, report.UnmatchedRequests);
            AppendSummaryRows(builder, "Top payload stages", report.TopStages);
            AppendSummaryRows(builder, "Top payload names", report.TopNames);
            AppendRunSummaries(builder, report.RunSummaries);

            return builder.ToString().TrimEnd();
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

        static void DrawSectionHeader(string label)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        }

        sealed class PayloadStatsReport
        {
            public string StatsPath { get; set; }
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
            public double SavingsPct => Percent(Math.Max(0L, RawBytes - ShapedBytes), RawBytes);
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

        sealed class PayloadRow
        {
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
            public string CommandType { get; set; }
            public long RequestBytes { get; set; }
            public long ResponseBytes { get; set; }
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
}
