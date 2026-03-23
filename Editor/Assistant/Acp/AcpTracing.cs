using Unity.AI.Tracing;

namespace Unity.AI.Assistant.Editor.Acp
{
    /// <summary>
    /// Trace loggers for AI Gateway lifecycle instrumentation.
    /// Enable categories in Developer Tools > Tracing to see console output.
    /// All traces are written to Logs/traces.jsonl regardless of console settings.
    /// </summary>
    static class AcpTracing
    {
        /// <summary>
        /// Session lifecycle traces: creation, start, prompt, cancel, end, dispose.
        /// </summary>
        public static readonly TraceLogger Session = new("gateway.session");

        /// <summary>
        /// Provider layer traces: session switches, conversation load, abort, errors.
        /// </summary>
        public static readonly TraceLogger Provider = new("gateway.provider");

        /// <summary>
        /// Session cleanup traces: mark for release, turn complete, cancel pending.
        /// </summary>
        public static readonly TraceLogger Cleanup = new("gateway.cleanup");

        /// <summary>
        /// Connection state traces: relay connect/disconnect, connection changes.
        /// </summary>
        public static readonly TraceLogger Connection = new("gateway.connection");

        /// <summary>
        /// Registry traces: session acquire, release, end all.
        /// </summary>
        public static readonly TraceLogger Registry = new("gateway.registry");

        /// <summary>
        /// Observer traces: provider state changes, ready state, phases.
        /// </summary>
        public static readonly TraceLogger Observer = new("gateway.observer");
    }
}
