using System;
using System.IO;
using System.Threading.Tasks;
using Unity.AI.Assistant.FunctionCalling;
using UnityEditor;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class DeleteFileTool
    {
        const string k_FunctionId = "Unity.DeleteFile";

        public static readonly string FilePathNullOrEmptyError = "File path cannot be null or empty.";
        public static readonly string UnauthorizedAccessErrorTemplate = "File deletion is only allowed within the Assets or Packages directories. Path must be a valid asset path. Provided: {0}";
        public static readonly string FailedToDeleteAssetErrorTemplate = "Failed to delete asset: {0}. The asset might not exist, be in use, or be protected.";

        [AgentTool("Delete a file from the Unity project. This tool can only delete files within the Assets or Packages directories. File paths must be relative to Unity project root (e.g., \"Assets/Scripts/OldScript.cs\"). The file will be permanently deleted.",
            k_FunctionId,
            ToolCallEnvironment.EditMode,
            tags: FunctionCallingUtilities.k_GameObjectTag)]
        public static async Task<string> Delete(
            ToolExecutionContext context,
            [Parameter("Path to the file to delete. Must be relative to Unity project root and within the Assets or Packages directories.")]
            string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException(FilePathNullOrEmptyError);
            }

            if (!IsValidAssetPath(filePath))
            {
                throw new UnauthorizedAccessException(string.Format(UnauthorizedAccessErrorTemplate, filePath));
            }

            await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Delete, filePath);

            // Use MoveAssetToTrash to delete the file safely
            if (!AssetDatabase.MoveAssetToTrash(filePath))
            {
                throw new InvalidOperationException(string.Format(FailedToDeleteAssetErrorTemplate, filePath));
            }

            return $"Successfully deleted file: {Path.GetFileName(filePath)}";
        }

        static bool IsValidAssetPath(string path)
        {
            string normalizedPath = path.Replace('\\', '/');

            // Check if path is within valid asset directories (Assets/ or Packages/)
            return normalizedPath.StartsWith("Assets/") || normalizedPath.StartsWith("Packages/");
        }
    }
}
