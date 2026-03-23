using System.IO;
using UnityEngine;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    static class PathUtils
    {
        public static bool PathsEqual(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return false;

            // Convert Unity-style slashes → OS slashes
            a = a.Replace('/', Path.DirectorySeparatorChar);
            b = b.Replace('/', Path.DirectorySeparatorChar);

            // Convert relative paths to absolute using Unity project root
            if (!Path.IsPathRooted(a))
                a = Path.GetFullPath(Path.Combine(Application.dataPath, "..", a));

            if (!Path.IsPathRooted(b))
                b = Path.GetFullPath(Path.Combine(Application.dataPath, "..", b));

            // Normalize, remove ../ and redundant segments
            a = Path.GetFullPath(a);
            b = Path.GetFullPath(b);

            // Compare using OS rules
            return string.Equals(
                a.TrimEnd(Path.DirectorySeparatorChar),
                b.TrimEnd(Path.DirectorySeparatorChar),
                System.StringComparison.OrdinalIgnoreCase
            );
        }

    }
}
