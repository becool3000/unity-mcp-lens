using System;

namespace Unity.AI.Assistant.Editor.Utils
{
    internal static class DateTimeExtensions
    {
        /// <summary>
        /// Helper extension to directly convert from DateTime to Unix Time without having to go through DateTimeOffset
        /// </summary>
        /// <param name="dt">the time to convert</param>
        /// <returns>the unix time in milliseconds (UTC)</returns>
        public static long ToUnixTimeMilliseconds(this DateTime dt)
        {
            var utcDt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            return new DateTimeOffset(utcDt).ToUnixTimeMilliseconds();
        }
    }
}
