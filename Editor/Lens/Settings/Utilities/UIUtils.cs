using System;
using UnityEngine.UIElements;

namespace Becool.UnityMcpLens.Editor.Settings.Utilities
{
    static class UIUtils
    {
        public static void SetDisplay(this VisualElement target, bool display) => target.style.display = display ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>
        /// Formats a DateTime as relative time (e.g., "5 min ago", "2 hours ago", etc.)
        /// </summary>
        public static string FormatRelativeTime(DateTime timestamp)
        {
            var now = DateTime.UtcNow;
            var localTime = timestamp.ToUniversalTime();
            var timeSpan = now - localTime;

            if (timeSpan.TotalSeconds < 60)
                return "Just now";

            if (timeSpan.TotalMinutes < 60)
            {
                int minutes = (int)timeSpan.TotalMinutes;
                return $"{minutes} min ago";
            }

            if (timeSpan.TotalHours < 24)
            {
                int hours = (int)timeSpan.TotalHours;
                return $"{hours} hour{(hours == 1 ? "" : "s")} ago";
            }

            if (timeSpan.TotalDays < 7)
            {
                int days = (int)timeSpan.TotalDays;
                return $"{days} day{(days == 1 ? "" : "s")} ago";
            }

            if (timeSpan.TotalDays < 30)
            {
                int weeks = (int)(timeSpan.TotalDays / 7);
                return $"{weeks} week{(weeks == 1 ? "" : "s")} ago";
            }

            if (timeSpan.TotalDays < 365)
            {
                int months = (int)(timeSpan.TotalDays / 30);
                return $"{months} month{(months == 1 ? "" : "s")} ago";
            }

            int years = (int)(timeSpan.TotalDays / 365);
            return $"{years} year{(years == 1 ? "" : "s")} ago";
        }
    }
}
