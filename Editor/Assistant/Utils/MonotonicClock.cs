using System.Diagnostics;

namespace Unity.AI.Assistant.Utils
{
    static class MonotonicClock
    {
        public static double NowSeconds => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

        public static double SecondsSince(double startedAtSeconds)
        {
            return NowSeconds - startedAtSeconds;
        }
    }
}
