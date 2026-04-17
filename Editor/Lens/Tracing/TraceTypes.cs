using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Becool.UnityMcpLens.Editor.Tracing
{
    /// <summary>
    /// Trace levels as numeric values for fast comparison.
    /// NONE disables all tracing (zero-cost when set).
    /// </summary>
    enum TraceLevel
    {
        None = -1,
        Error = 0,
        Warn = 1,
        Info = 2,
        Debug = 3,
    }

    /// <summary>
    /// The component that emitted the trace event.
    /// </summary>
    static class TraceComponent
    {
        public const string Unity = "unity";
        public const string Relay = "relay";
        public const string McpServer = "mcp-server";
    }

    /// <summary>
    /// The kind of trace event.
    /// </summary>
    static class TraceEventKind
    {
        public const string SpanStart = "span_start";
        public const string SpanEnd = "span_end";
        public const string Event = "event";
        public const string Log = "log";
    }

    /// <summary>
    /// A structured trace event — one JSON line in traces.jsonl.
    /// Pure data object: fields match the JSON schema exactly.
    /// </summary>
    class TraceEvent
    {
        public string ts;
        public string traceId;
        public string spanId;
        public string parentSpanId;
        public string component;
        public string name;
        public string kind;
        public string level;
        public string sessionId;
        public int? durationMs;
        public JObject data;
        /// <summary>
        /// Marks high-frequency events (e.g., polling, heartbeats) that can be hidden in the UI.
        /// </summary>
        public bool? recurring;

        /// <summary>
        /// Original exception, if this event was created from <see cref="Trace.Exception"/>.
        /// Not serialized — only used by <see cref="ConsoleSink"/> for <c>Debug.LogException</c>.
        /// </summary>
        [JsonIgnore]
        public Exception exception;
    }

    /// <summary>
    /// Configuration for trace filtering. Used per-sink.
    /// </summary>
    [Serializable]
    class TraceConfig
    {
        public string DefaultLevel = "info";
        public Dictionary<string, string> Categories;
        public Dictionary<string, string> Sessions;
        public Dictionary<string, string> Components;
        /// <summary>
        /// When true, recurring events (marked with recurring=true) are filtered out.
        /// Defaults to true.
        /// </summary>
        public bool FilterRecurring = true;
    }

    /// <summary>
    /// Artifact reference placed in data when a value exceeds the size threshold.
    /// </summary>
    [Serializable]
    class ArtifactRef
    {
        // ReSharper disable InconsistentNaming
        public string _artifact;
        public int size;
        public string preview;
        // ReSharper restore InconsistentNaming
    }

    /// <summary>
    /// A trace sink receives built TraceEvents and writes them to an output.
    /// Each sink has its own TraceConfig for independent filtering.
    /// </summary>
    interface ITraceSink
    {
        string Name { get; }
        TraceConfig Config { get; set; }
        void Write(TraceEvent evt);
    }

    /// <summary>
    /// Utility methods for trace level conversion.
    /// </summary>
    static class TraceLevelUtils
    {
        /// <summary>
        /// Parse a lowercase level string ("error", "warn", "info", "debug") to TraceLevel.
        /// </summary>
        public static TraceLevel Parse(string level) =>
            Enum.TryParse<TraceLevel>(level, ignoreCase: true, out var result) ? result : TraceLevel.None;

        /// <summary>
        /// Extract the category from a hierarchical event name.
        /// e.g. "tool_call.execute" returns "tool_call".
        /// </summary>
        public static string ExtractCategory(string name)
        {
            var dotIndex = name.IndexOf('.');
            return dotIndex >= 0 ? name.Substring(0, dotIndex) : name;
        }

        /// <summary>
        /// Get the USS class name for a level dot element.
        /// </summary>
        public static string LevelDotClass(TraceLevel level)
        {
            return level switch
            {
                TraceLevel.Error => "trace-level-dot--error",
                TraceLevel.Warn => "trace-level-dot--warn",
                TraceLevel.Info => "trace-level-dot--info",
                _ => "trace-level-dot--debug",
            };
        }

        /// <summary>
        /// Get the USS class name for a level dot element from a level string.
        /// </summary>
        public static string LevelDotClass(string level) => LevelDotClass(Parse(level));

        /// <summary>
        /// Resolve the effective level for an event given a TraceConfig.
        /// Resolution order: session override > category override > component override > defaultLevel.
        /// </summary>
        public static TraceLevel ResolveLevel(
            TraceConfig config,
            string category,
            string sessionId = null,
            string component = null)
        {
            if (sessionId != null && config.Sessions != null &&
                config.Sessions.TryGetValue(sessionId, out var sessionLevel))
                return Parse(sessionLevel);

            if (config.Categories != null &&
                config.Categories.TryGetValue(category, out var categoryLevel))
                return Parse(categoryLevel);

            if (component != null && config.Components != null &&
                config.Components.TryGetValue(component, out var componentLevel))
                return Parse(componentLevel);

            return Parse(config.DefaultLevel);
        }
    }
}
