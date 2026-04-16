using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.MCP.Editor.Utils.File
{
    internal static class FileUtils
    {
        // Heuristic to check if a file is binary or not
        public static bool IsTextFile(string path, int sampleSize = 4096)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);

                var len = (int)Mathf.Min(fs.Length, sampleSize);
                var buffer = new byte[len];
                var readCount = fs.Read(buffer, 0, len);

                var nonPrintable = 0;
                for (var i = 0; i < readCount; i++)
                {
                    var b = buffer[i];

                    // Count the number of non printable characters
                    // Allow: tab (9), LF (10), CR (13), printable ASCII (32-126)
                    if (b != 9 && b != 10 && b != 13 && (b < 32 || b > 126))
                        nonPrintable++;
                }

                // If more than 5% of characters are non-printable, consider it binary
                var ratio = (float)nonPrintable / len;
                return ratio < 0.05f;
            }
            catch
            {
                return false;
            }
        }
    }
}
