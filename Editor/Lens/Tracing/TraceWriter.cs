using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace Becool.UnityMcpLens.Editor.Tracing
{
    /// <summary>
    /// Options for creating a span or event.
    /// </summary>
    record TraceEventOptions
    {
        public string Level { get; init; } = "info";
        public string SessionId { get; init; }
        public string TraceId { get; init; }
        public string ParentSpanId { get; init; }
        public object Data { get; init; }

        /// <summary>
        /// Lazy data — only evaluated if the event passes the filter.
        /// Set this instead of Data for expensive construction.
        /// </summary>
        public Func<object> LazyData { get; init; }

        /// <summary>
        /// Force console output regardless of level filtering.
        /// Use for visibility without implying warning/error severity.
        /// </summary>
        public bool Console { get; init; }

        /// <summary>
        /// Category for filtering (e.g. "search", "mcp").
        /// If not set, category is extracted from the event name.
        /// </summary>
        public string Category { get; init; }

        /// <summary>
        /// Marks high-frequency events (e.g., polling, heartbeats) that can be hidden in the UI.
        /// </summary>
        public bool Recurring { get; init; }

        /// <summary>
        /// Exception to attach to the event.
        /// </summary>
        public Exception Exception { get; init; }

        /// <summary>
        /// Log message. For Log-kind events, merged into Data as { "message": ... }.
        /// </summary>
        public string Message { get; init; }
    }

    /// <summary>
    /// Represents a traced operation with start/end and duration.
    /// Created by TraceWriter.StartSpan(). Call End() when the operation completes.
    /// </summary>
    class TraceSpan
    {
        public readonly string SpanId;
        public readonly string Name;

        public string TraceId => m_Opts.TraceId;
        public string ParentSpanId => m_Opts.ParentSpanId;
        public string SessionId => m_Opts.SessionId;
        public string Level => m_Opts.Level;
        public bool Recurring => m_Opts.Recurring;

        readonly TraceWriter m_Writer;
        readonly TraceEventOptions m_Opts;
        readonly long m_StartTicks;
        bool m_Ended;

        internal TraceSpan(TraceWriter writer, string name, string spanId, TraceEventOptions opts)
        {
            m_Writer = writer;
            Name = name;
            SpanId = spanId;
            m_Opts = opts;
            m_StartTicks = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// End this span and record its duration.
        /// </summary>
        public void End(object attrs = null)
        {
            if (m_Ended) return;
            m_Ended = true;

            var elapsedTicks = Stopwatch.GetTimestamp() - m_StartTicks;
            var durationMs = (int)(elapsedTicks * 1000 / Stopwatch.Frequency);

            m_Writer.WriteEventInternal(
                Name, TraceEventKind.SpanEnd,
                m_Opts with { Data = attrs, LazyData = null },
                spanId: SpanId,
                durationMs: durationMs);
        }

        /// <summary>
        /// Create a child span nested under this one.
        /// </summary>
        public TraceSpan Child(string name, TraceEventOptions opts)
        {
            return m_Writer.StartSpan(name, opts with
            {
                TraceId = opts.TraceId ?? m_Opts.TraceId,
                ParentSpanId = SpanId,
                SessionId = opts.SessionId ?? m_Opts.SessionId,
            });
        }

        /// <summary>
        /// Emit a point-in-time event within this span's context.
        /// </summary>
        public void Event(string name, TraceEventOptions opts)
        {
            m_Writer.Event(name, opts with
            {
                TraceId = opts.TraceId ?? m_Opts.TraceId,
                ParentSpanId = SpanId,
                SessionId = opts.SessionId ?? m_Opts.SessionId,
            });
        }
    }

    /// <summary>
    /// Central trace writer. Dispatches events to registered sinks.
    /// Thread-safe for concurrent writes.
    /// </summary>
    class TraceWriter
    {
        public string Component { get; }
        public string LogDir { get; }

        readonly int m_ArtifactThreshold;
        readonly List<ITraceSink> m_Sinks = new List<ITraceSink>();
        readonly object m_Lock = new object();
        readonly ThreadLocal<bool> m_IsEmitting = new();

        // Cached max levels across all sinks per category
        readonly Dictionary<string, TraceLevel> m_MaxLevelCache = new Dictionary<string, TraceLevel>();
        readonly HashSet<string> m_AllCategories = new HashSet<string>();

        static readonly System.Random s_Random = new System.Random();

        public TraceWriter(string component, string logDir, int artifactThreshold = 8192)
        {
            Component = component;
            LogDir = logDir;
            m_ArtifactThreshold = artifactThreshold;
        }

        // -- Sink management --

        public void AddSink(ITraceSink sink)
        {
            lock (m_Lock)
            {
                m_Sinks.Add(sink);
                RebuildMaxLevelCache();
            }
        }

        public void RemoveSink(ITraceSink sink)
        {
            lock (m_Lock)
            {
                m_Sinks.Remove(sink);
                RebuildMaxLevelCache();
            }
        }

        public void UpdateSinkConfig(string sinkName, TraceConfig config)
        {
            lock (m_Lock)
            {
                foreach (var sink in m_Sinks)
                {
                    if (sink.Name == sinkName)
                    {
                        sink.Config = config;
                        break;
                    }
                }
                RebuildMaxLevelCache();
            }
        }

        /// <summary>
        /// Merge a single category override into an existing sink's config,
        /// preserving all other categories and settings.
        /// </summary>
        public void MergeSinkCategoryOverride(string sinkName, string category, string level)
        {
            lock (m_Lock)
            {
                foreach (var sink in m_Sinks)
                {
                    if (sink.Name == sinkName)
                    {
                        sink.Config.Categories ??= new Dictionary<string, string>();
                        sink.Config.Categories[category] = level;
                        break;
                    }
                }
                RebuildMaxLevelCache();
            }
        }

        // -- Fast path check --

        /// <summary>
        /// Check if ANY sink is interested in an event with this category and level.
        /// This is the zero-cost fast path.
        /// </summary>
        public bool IsEnabled(string category, string level)
        {
            if (m_Sinks.Count == 0) return false;
            var levelNum = TraceLevelUtils.Parse(level);
            // error/warn always pass
            if (levelNum <= TraceLevel.Warn) return true;

            lock (m_Lock)
            {
                if (m_MaxLevelCache.TryGetValue(category, out var maxLevel))
                    return levelNum <= maxLevel;

                if (m_MaxLevelCache.TryGetValue("__default", out var defaultMax))
                    return levelNum <= defaultMax;
            }

            return false;
        }

        /// <summary>
        /// Check if an event is enabled considering session overrides.
        /// </summary>
        public bool IsEnabledFull(string category, string level, string sessionId = null)
        {
            if (m_Sinks.Count == 0) return false;
            var levelNum = TraceLevelUtils.Parse(level);
            if (levelNum <= TraceLevel.Warn) return true;

            lock (m_Lock)
            {
                foreach (var sink in m_Sinks)
                {
                    var sinkLevel = TraceLevelUtils.ResolveLevel(sink.Config, category, sessionId, Component);
                    if (levelNum <= sinkLevel) return true;
                }
            }

            return false;
        }

        // -- Core API --

        /// <summary>
        /// Start a new span. Returns a TraceSpan; call End() when the operation completes.
        /// </summary>
        public TraceSpan StartSpan(string name, TraceEventOptions opts)
        {
            var category = opts.Category ?? TraceLevelUtils.ExtractCategory(name);
            var spanId = GenerateSpanId();
            var resolved = opts with { Category = category };

            if (opts.Console || IsEnabledFull(category, opts.Level, opts.SessionId))
            {
                WriteEventInternal(name, TraceEventKind.SpanStart, resolved, spanId: spanId);
            }

            return new TraceSpan(this, name, spanId, resolved);
        }

        /// <summary>
        /// Emit a point-in-time event.
        /// </summary>
        public void Event(string name, TraceEventOptions opts)
        {
            var category = opts.Category ?? TraceLevelUtils.ExtractCategory(name);
            if (!opts.Console && !IsEnabledFull(category, opts.Level, opts.SessionId)) return;

            WriteEventInternal(name, TraceEventKind.Event, opts with { Category = category });
        }

        /// <summary>
        /// Emit a log-kind event. Merges <see cref="TraceEventOptions.Message"/> into Data
        /// as { "message": ... }. Category defaults to "log" if not set.
        /// </summary>
        public void Log(TraceEventOptions opts)
        {
            var category = opts.Category ?? "log";
            if (!opts.Console && !IsEnabledFull(category, opts.Level, opts.SessionId)) return;
            WriteEventInternal("log", TraceEventKind.Log, opts with { Category = category });
        }

        // -- Internal --

        internal void WriteEventInternal(
            string name,
            string eventKind,
            TraceEventOptions opts,
            string spanId = null,
            int? durationMs = null)
        {
            if (m_IsEmitting.Value) return;
            m_IsEmitting.Value = true;
            try
            {
                var category = opts.Category ?? TraceLevelUtils.ExtractCategory(name);
                var levelNum = TraceLevelUtils.Parse(opts.Level);

                var resolvedSpanId = spanId ?? GenerateSpanId();
                var data = ResolveData(opts);

                // Log events merge Message into Data
                if (opts.Message != null)
                {
                    var merged = new JObject { ["message"] = opts.Message };
                    if (data != null)
                        merged.Merge(data is JObject joMsg ? joMsg : JObject.FromObject(data));
                    data = merged;
                }

                var processed = data != null ? ProcessArtifact(data, resolvedSpanId) : null;

                var evt = new TraceEvent
                {
                    ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    component = Component,
                    name = name,
                    kind = eventKind,
                    level = opts.Level,
                    spanId = resolvedSpanId,
                    traceId = opts.TraceId,
                    parentSpanId = opts.ParentSpanId,
                    sessionId = opts.SessionId,
                    durationMs = durationMs,
                    data = processed != null ? (processed is JObject jo ? jo : JObject.FromObject(processed)) : null,
                    recurring = opts.Recurring ? true : null,
                    exception = opts.Exception,
                };

                // Dispatch to each interested sink
                lock (m_Lock)
                {
                    foreach (var sink in m_Sinks)
                    {
                        if (opts.Recurring && sink.Config.FilterRecurring)
                            continue;

                        bool forceWrite = opts.Console && sink.Name == "console";

                        var sinkLevel = TraceLevelUtils.ResolveLevel(sink.Config, category, opts.SessionId, Component);
                        if (forceWrite || levelNum <= sinkLevel)
                        {
                            try
                            {
                                sink.Write(evt);
                            }
                            catch (Exception e)
                            {
                                // Sink errors must never break the application
                                LogException(e);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                // Trace errors must never affect application behavior
                LogException(e);
            }
            finally
            {
                m_IsEmitting.Value = false;
            }
        }

        [Conditional("ASSISTANT_INTERNAL")]
        static void LogException(Exception e) => UnityEngine.Debug.LogException(e);

        object ProcessArtifact(object data, string spanId)
        {
            var json = data is JObject jObj ? jObj.ToString(Newtonsoft.Json.Formatting.None)
                : Newtonsoft.Json.JsonConvert.SerializeObject(data);
            if (json.Length <= m_ArtifactThreshold)
                return data;

            var artifactName = $"{spanId}-data.json";
            WriteArtifact(artifactName, json);
            return new ArtifactRef
            {
                _artifact = artifactName,
                size = json.Length,
                preview = json.Substring(0, Math.Min(200, json.Length)),
            };
        }

        void WriteArtifact(string filename, string content)
        {
            try
            {
                var artifactDir = System.IO.Path.Combine(LogDir, "artifacts");
                if (!System.IO.Directory.Exists(artifactDir))
                    System.IO.Directory.CreateDirectory(artifactDir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(artifactDir, filename), content);
            }
            catch
            {
                // Artifact write failure is non-fatal
            }
        }

        void RebuildMaxLevelCache()
        {
            m_MaxLevelCache.Clear();

            if (m_Sinks.Count == 0) return;

            foreach (var sink in m_Sinks)
            {
                if (sink.Config.Categories != null)
                {
                    foreach (var cat in sink.Config.Categories.Keys)
                        m_AllCategories.Add(cat);
                }
            }

            // Max default level
            var maxDefault = TraceLevel.None;
            foreach (var sink in m_Sinks)
            {
                var sinkDefault = TraceLevelUtils.Parse(sink.Config.DefaultLevel);
                if (sinkDefault > maxDefault) maxDefault = sinkDefault;
            }

            m_MaxLevelCache["__default"] = maxDefault;

            // Max level per category
            foreach (var cat in m_AllCategories)
            {
                var maxCat = TraceLevel.None;
                foreach (var sink in m_Sinks)
                {
                    var catLevel = TraceLevelUtils.ResolveLevel(sink.Config, cat);
                    if (catLevel > maxCat) maxCat = catLevel;
                }

                m_MaxLevelCache[cat] = maxCat;
            }

            // Session/component overrides can elevate above defaults
            foreach (var sink in m_Sinks)
            {
                if (sink.Config.Sessions != null)
                {
                    foreach (var level in sink.Config.Sessions.Values)
                    {
                        var num = TraceLevelUtils.Parse(level);
                        if (num > m_MaxLevelCache["__default"])
                            m_MaxLevelCache["__default"] = num;
                    }
                }

                if (sink.Config.Components != null)
                {
                    foreach (var level in sink.Config.Components.Values)
                    {
                        var num = TraceLevelUtils.Parse(level);
                        if (num > m_MaxLevelCache["__default"])
                            m_MaxLevelCache["__default"] = num;
                    }
                }
            }
        }

        static string GenerateSpanId()
        {
            var bytes = new byte[8];
            lock (s_Random)
            {
                s_Random.NextBytes(bytes);
            }

            var sb = new StringBuilder(16);
            foreach (var b in bytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        static object ResolveData(TraceEventOptions opts)
        {
            if (opts.LazyData != null)
                return opts.LazyData();
            return opts.Data;
        }
    }
}
