using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Lens.Usage
{
    class PayloadStatsWindow : EditorWindow
    {
        const string k_WindowName = "Lens Usage";
        const int k_TopCount = 8;

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

            EditorGUIUtility.systemCopyBuffer = PayloadStatsReportFormatter.BuildClipboardReport(m_Report);
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

                m_Report = PayloadStatsAnalyzer.LoadReport(statsPath, new PayloadStatsQuery
                {
                    AllRows = true,
                    MaxItems = k_TopCount
                });
                m_ErrorMessage = null;
                m_ShowMissingFileHelp = false;
            }
            catch (PayloadStatsException ex)
            {
                m_Report = null;
                m_ErrorMessage = $"Failed to read Lens usage stats.\n{ex.Message}";
                m_ShowMissingFileHelp = string.Equals(ex.ErrorKind, "stats_file_not_found", StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                m_Report = null;
                m_ErrorMessage = $"Failed to read Lens usage stats.\n{ex.Message}";
                m_ShowMissingFileHelp = false;
            }
        }

        static string GetStatsPath()
        {
            return PayloadStatsFileAdapter.ResolveStatsPath();
        }

        static string FormatBytes(long bytes)
        {
            return PayloadStatsReportFormatter.FormatBytes(bytes);
        }

        static string FormatNumber(long value)
        {
            return PayloadStatsReportFormatter.FormatNumber(value);
        }

        static string ShortId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length <= 10)
                return value ?? string.Empty;

            return value.Substring(0, 10);
        }

        static void DrawSectionHeader(string label)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        }
    }
}
