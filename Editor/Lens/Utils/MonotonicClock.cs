using System.Diagnostics;

namespace Becool.UnityMcpLens.Editor.Utils
{
    static class MonotonicClock
    {
        public static double NowSeconds => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

        public static double SecondsSince(double startedAtSeconds) => NowSeconds - startedAtSeconds;
    }
}
