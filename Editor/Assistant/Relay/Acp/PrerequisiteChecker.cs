using System.Collections.Generic;
using System.IO;

namespace Unity.Relay.Editor.Acp
{
    /// <summary>
    /// Utility for checking if provider prerequisites are met.
    /// </summary>
    static class PrerequisiteChecker
    {
        /// <summary>
        /// Check if ALL prerequisites are met (AND logic between prereqs).
        /// </summary>
        /// <returns>List of unmet prerequisites (empty if all met).</returns>
        public static List<AcpPrerequisiteCheck> GetUnmetPrerequisites(AcpPrerequisiteCheck[] checks)
        {
            var unmet = new List<AcpPrerequisiteCheck>();
            if (checks == null || checks.Length == 0)
                return unmet;

            foreach (var check in checks)
            {
                if (!IsMet(check))
                    unmet.Add(check);
            }

            return unmet;
        }

        /// <summary>
        /// Check if a single prerequisite is met (OR logic within paths).
        /// </summary>
        public static bool IsMet(AcpPrerequisiteCheck check)
        {
            if (check?.CheckPaths == null || check.CheckPaths.Length == 0)
                return true;

            foreach (var path in check.CheckPaths)
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Check if ALL prerequisites are met.
        /// </summary>
        public static bool AllMet(AcpPrerequisiteCheck[] checks)
        {
            return GetUnmetPrerequisites(checks).Count == 0;
        }
    }
}
