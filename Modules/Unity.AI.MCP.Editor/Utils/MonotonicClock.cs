using System.Diagnostics;

namespace Unity.AI.MCP.Editor.Utils
{
    static class MonotonicClock
    {
        public static double NowSeconds => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

        public static double SecondsSince(double startedAtSeconds) => NowSeconds - startedAtSeconds;
    }
}
