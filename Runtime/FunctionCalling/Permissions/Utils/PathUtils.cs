using System;
using System.IO;
using UnityEngine;

namespace Unity.AI.Assistant.FunctionCalling
{
    static class PathUtils
    {
        /// <summary>
        /// Checks whether the given path (file or directory, relative or absolute)
        /// is located inside the current Unity project folder.
        /// </summary>
        public static bool IsProjectPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            try
            {
                var projectPath = Path.GetFullPath(Application.dataPath + "/..").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                return fullPath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool IsFilePath(string path)
        {
            path = path.Replace('\\', '/').Trim();
            if (path.EndsWith("/"))
                return false;

            if (!string.IsNullOrEmpty(Path.GetExtension(path)))
                return true;

            // Treat as folder by default
            return false;
        }
    }
}
