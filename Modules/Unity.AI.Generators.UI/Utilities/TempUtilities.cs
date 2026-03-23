using System;
using System.IO;
using Unity.AI.Generators.Asset;
using UnityEngine;

namespace Unity.AI.Generators.UI.Utilities
{
    static class TempUtilities
    {
        public static readonly string projectRootPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        public static string GetTempFileNameUndo() => GetTempFileName("Undo");

        public static string GetTempFileName(string suffix)
        {
            if (string.IsNullOrEmpty(suffix))
                throw new ArgumentException("Suffix cannot be null or empty", nameof(suffix));

            // this folder is automatically cleaned up by Unity Editor
            var tempFolderPath = Path.Combine(projectRootPath, "Temp", AssetReferenceExtensions.GetGeneratedAssetsRoot(), suffix);

            if (!Directory.Exists(tempFolderPath))
                Directory.CreateDirectory(tempFolderPath);

            var fileName = Guid.NewGuid().ToString("N") + ".tmp";
            var fullFilePath = Path.Combine(tempFolderPath, fileName);

            using (File.Create(fullFilePath))
            {
                // release immediately
            }

            return fullFilePath;
        }
    }
}
