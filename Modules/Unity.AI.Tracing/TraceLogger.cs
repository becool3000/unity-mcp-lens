using System;
using System.Collections.Generic;

namespace Unity.AI.Tracing
{
    /// <summary>
    /// A category-scoped logger that emits trace events.
    /// Stores base <see cref="TraceEventOptions"/> and merges per-call overrides.
    /// When <see cref="TraceEventOptions.Console"/> is false (default), output is controlled
    /// by category config — events only reach the console if the category is enabled.
    /// When true, all output also goes to the Unity console (like Debug.Log).
    /// </summary>
    class TraceLogger
    {
        readonly TraceEventOptions m_BaseOptions;

        public TraceLogger(string category)
            : this(new TraceEventOptions { Category = category }) { }

        public TraceLogger(TraceEventOptions baseOptions)
        {
            m_BaseOptions = baseOptions;
        }

        public void Log(string level, string msg, string sessionId = null,
            string traceId = null, TraceEventOptions opts = null)
            => Emit(msg, level, sessionId, traceId, opts);

        public void Debug(string msg, string sessionId = null, string traceId = null,
            TraceEventOptions opts = null)
            => Emit(msg, "debug", sessionId, traceId, opts);

        public void Info(string msg, string sessionId = null, string traceId = null,
            TraceEventOptions opts = null)
            => Emit(msg, "info", sessionId, traceId, opts);

        public void Warn(string msg, string sessionId = null, string traceId = null,
            TraceEventOptions opts = null)
            => Emit(msg, "warn", sessionId, traceId, opts);

        public void Error(string msg, string sessionId = null, string traceId = null,
            TraceEventOptions opts = null)
            => Emit(msg, "error", sessionId, traceId, opts);

        public void Exception(Exception ex, string sessionId = null, string traceId = null,
            TraceEventOptions opts = null)
        {
            Emit(ex.Message, "error", sessionId, traceId, opts, BuildExceptionData(ex), ex);
        }

        public void Exception(Exception ex, string context, string sessionId = null,
            string traceId = null, TraceEventOptions opts = null)
        {
            var data = BuildExceptionData(ex);
            data["context"] = context;
            Emit($"{context}: {ex.Message}", "error", sessionId, traceId, opts, data, ex);
        }

        static Dictionary<string, object> BuildExceptionData(Exception ex)
        {
            var data = new Dictionary<string, object>
            {
                { "exceptionType", ex.GetType().FullName },
                { "message", ex.Message },
                { "stackTrace", ex.StackTrace },
            };

            if (ex.InnerException != null)
            {
                data["innerException"] = new Dictionary<string, object>
                {
                    { "type", ex.InnerException.GetType().FullName },
                    { "message", ex.InnerException.Message },
                };
            }

            return data;
        }

        void Emit(string msg, string level, string sessionId, string traceId,
            TraceEventOptions callOpts, object data = null, Exception exception = null)
        {
            var merged = m_BaseOptions with
            {
                Level = level,
                SessionId = sessionId ?? callOpts?.SessionId ?? m_BaseOptions.SessionId,
                TraceId = traceId ?? callOpts?.TraceId ?? m_BaseOptions.TraceId,
                ParentSpanId = callOpts?.ParentSpanId ?? m_BaseOptions.ParentSpanId,
                Category = callOpts?.Category ?? m_BaseOptions.Category,
                LazyData = callOpts?.LazyData ?? m_BaseOptions.LazyData,
                Recurring = m_BaseOptions.Recurring || callOpts is { Recurring: true },
                Console = m_BaseOptions.Console || callOpts is { Console: true },
                Exception = exception ?? m_BaseOptions.Exception,
                Data = data ?? callOpts?.Data,
                Message = msg ?? callOpts?.Message
            };

            if (merged.Console)
            {
                Trace.Writer.Log(merged with { Message = msg });
            }
            else
            {
                // Event path: category as event name, message in Data.
                Trace.Writer.Event(m_BaseOptions.Category ?? "log", merged with
                {
                    Category = null,
                    Data = merged.Data ?? new Dictionary<string, object> { { "message", msg } },
                });
            }
        }
    }
}
