#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Becool.UnityMcpLens.Editor.Tracing;
using Becool.UnityMcpLens.Editor.Utils;

namespace Becool.UnityMcpLens.Editor.Services.GameObjects
{
    sealed class GameObjectToolTiming
    {
        sealed class StageScope : IDisposable
        {
            readonly GameObjectToolTiming m_Owner;
            readonly string m_Stage;
            readonly Stopwatch m_Stopwatch;
            bool m_Disposed;

            public StageScope(GameObjectToolTiming owner, string stage)
            {
                m_Owner = owner;
                m_Stage = stage;
                m_Stopwatch = Stopwatch.StartNew();
            }

            public void Dispose()
            {
                if (m_Disposed)
                    return;

                m_Disposed = true;
                m_Stopwatch.Stop();
                m_Owner.AddStageDuration(m_Stage, (int)m_Stopwatch.ElapsedMilliseconds);
            }
        }

        readonly Dictionary<string, int> m_Durations = new(StringComparer.Ordinal);
        readonly string m_ToolName;
        readonly string m_Action;
        readonly int m_RequestBytes;

        int m_ResponseBytes;

        public GameObjectToolTiming(string toolName, string action, int requestBytes)
        {
            m_ToolName = toolName;
            m_Action = action;
            m_RequestBytes = requestBytes;
        }

        public IDisposable Measure(string stage)
        {
            return new StageScope(this, stage);
        }

        public void SetResponseBytes(int responseBytes)
        {
            m_ResponseBytes = responseBytes;
        }

        public void Record(bool success, string errorKind = null)
        {
            foreach (var pair in m_Durations)
            {
                PayloadStats.RecordCoverage(
                    "tool_tsam_stage",
                    $"{m_ToolName}.{m_Action}.{pair.Key}",
                    meta: new
                    {
                        toolName = m_ToolName,
                        action = m_Action,
                        stage = pair.Key
                    },
                    options: new PayloadStatOptions
                    {
                        EventKind = "tool_tsam_stage",
                        PayloadClass = "tool_timing",
                        RepresentationKind = "reference",
                        DurationMs = pair.Value,
                        Success = success,
                        ErrorKind = errorKind,
                        ExtraFields = new
                        {
                            toolName = m_ToolName,
                            action = m_Action,
                            stage = pair.Key,
                            requestBytes = m_RequestBytes,
                            responseBytes = m_ResponseBytes
                        }
                    });
            }

            Becool.UnityMcpLens.Editor.Tracing.Trace.Event("mcp.tool.tsam_timing", new TraceEventOptions
            {
                Category = "mcp",
                Data = new
                {
                    toolName = m_ToolName,
                    action = m_Action,
                    success,
                    errorKind,
                    requestBytes = m_RequestBytes,
                    responseBytes = m_ResponseBytes,
                    durations = m_Durations
                }
            });
        }

        void AddStageDuration(string stage, int durationMs)
        {
            if (m_Durations.TryGetValue(stage, out int existing))
                m_Durations[stage] = existing + durationMs;
            else
                m_Durations[stage] = durationMs;
        }
    }
}
