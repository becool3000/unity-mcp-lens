using System;

namespace Unity.AI.Generators.UI.Utilities
{
    record Timestamp(long lastWriteTimeUtcTicks)
    {
        public Timestamp(DateTime lastWriteTime) : this(lastWriteTime.ToUniversalTime().Ticks) {}

        public static Timestamp FromDateTime(DateTime lastWriteTime) => new (lastWriteTime.ToUniversalTime().Ticks);
        public static Timestamp FromUtcTicks(long lastWriteTimeUtcTicks) => new (lastWriteTimeUtcTicks);
    }
}
