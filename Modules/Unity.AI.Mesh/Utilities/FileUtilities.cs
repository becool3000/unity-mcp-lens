using System;
using System.IO;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEngine;

namespace Unity.AI.Mesh.Services.Utilities
{
    static class FileUtilities
    {
        public const string failedDownloadPath = "Packages/com.unity.ai.assistant/Modules/Unity.AI.Mesh/Meshes/Cube.fbx";

        public static string GetFailedMeshUrl(string guid)
        {
            var sourceFile = Path.GetFullPath(failedDownloadPath);
            var tempFolder = Path.Combine(TempUtilities.projectRootPath, "Temp");

            if (!Directory.Exists(tempFolder))
                Directory.CreateDirectory(tempFolder);

            var destinationFile = Path.Combine(tempFolder, guid);
            destinationFile = Path.ChangeExtension(destinationFile, Path.GetExtension(sourceFile));
            FileIO.CopyFile(sourceFile, destinationFile, true);

            var fileUri = new Uri(destinationFile);
            return fileUri.AbsoluteUri;
        }
    }
}
