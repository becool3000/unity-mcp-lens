using System;
using System.IO;
using System.Text;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry;
using UnityEditor;

namespace Unity.AI.MCP.Editor.Tools
{
    public record ResourceWriteParams
    {
        [McpDescription("Resource URI or project-relative path. Supports unity://path/Assets/..., Assets/..., and Packages/...", Required = true)]
        public string Uri { get; set; }

        [McpDescription("UTF-8 text content to write.", Required = true)]
        public string Text { get; set; }

        [McpDescription("Required SHA-256 of the current file when overwriting an existing file.", Required = false)]
        public string PreconditionSha256 { get; set; }

        [McpDescription("Create parent directories when they do not exist.", Required = false, Default = true)]
        public bool CreateDirectories { get; set; } = true;

        [McpDescription("Optional project root override.", Required = false)]
        public string ProjectRoot { get; set; }
    }

    public record ResourceDeleteParams
    {
        [McpDescription("Resource URI or project-relative path under Assets/ or Packages/.", Required = true)]
        public string Uri { get; set; }

        [McpDescription("Optional SHA-256 precondition for file deletion.", Required = false)]
        public string PreconditionSha256 { get; set; }

        [McpDescription("Allow deleting a directory tree.", Required = false, Default = false)]
        public bool Recursive { get; set; }

        [McpDescription("Optional project root override.", Required = false)]
        public string ProjectRoot { get; set; }
    }

    public static class ResourceMutationTools
    {
        const string WriteDescription = "Writes UTF-8 text under allowed Unity project roots. Existing files require a SHA-256 precondition.";
        const string DeleteDescription = "Deletes files or directories under allowed Unity project roots. This is an admin/full-pack tool.";

        [McpTool("Unity.Resource.Write", WriteDescription, "Write Unity Resource", Groups = new[] { "scripting", "assets" }, EnabledByDefault = true)]
        public static object Write(ResourceWriteParams parameters)
        {
            parameters ??= new ResourceWriteParams();
            if (string.IsNullOrWhiteSpace(parameters.Uri))
                return Response.Error("URI_REQUIRED: Uri is required.");

            if (parameters.Text == null)
                return Response.Error("TEXT_REQUIRED: Text is required.");

            if (!TryResolveAllowedProjectPath(parameters.Uri, parameters.ProjectRoot, out var projectRoot, out var fullPath, out var assetPath, out var pathError))
                return Response.Error(pathError);

            var existed = File.Exists(fullPath);
            string previousSha = null;
            if (existed)
            {
                previousSha = ResourceUriHelper.ComputeSha256(File.ReadAllBytes(fullPath));
                if (string.IsNullOrWhiteSpace(parameters.PreconditionSha256))
                {
                    return Response.Error("PRECONDITION_REQUIRED", new
                    {
                        uri = parameters.Uri,
                        currentSha256 = previousSha,
                        hint = "Provide preconditionSha256 to overwrite an existing file."
                    });
                }

                if (!string.Equals(previousSha, parameters.PreconditionSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return Response.Error("STALE_FILE", new
                    {
                        uri = parameters.Uri,
                        expectedSha256 = parameters.PreconditionSha256,
                        currentSha256 = previousSha
                    });
                }
            }

            var directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory))
            {
                if (!parameters.CreateDirectories)
                    return Response.Error("DIRECTORY_MISSING", new { directory = ToProjectRelativePath(projectRoot, directory) });

                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, parameters.Text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            AssetDatabase.Refresh();

            var bytes = File.ReadAllBytes(fullPath);
            return Response.Success(existed ? "Resource overwritten." : "Resource written.", new
            {
                uri = $"unity://path/{assetPath}",
                path = assetPath,
                created = !existed,
                overwritten = existed,
                previousSha256 = previousSha,
                sha256 = ResourceUriHelper.ComputeSha256(bytes),
                bytes = bytes.Length
            });
        }

        [McpTool("Unity.Resource.Delete", DeleteDescription, "Delete Unity Resource", Groups = new[] { "admin" }, EnabledByDefault = true)]
        public static object Delete(ResourceDeleteParams parameters)
        {
            parameters ??= new ResourceDeleteParams();
            if (string.IsNullOrWhiteSpace(parameters.Uri))
                return Response.Error("URI_REQUIRED: Uri is required.");

            if (!TryResolveAllowedProjectPath(parameters.Uri, parameters.ProjectRoot, out var projectRoot, out var fullPath, out var assetPath, out var pathError))
                return Response.Error(pathError);

            bool isFile = File.Exists(fullPath);
            bool isDirectory = Directory.Exists(fullPath);
            if (!isFile && !isDirectory)
                return Response.Error("RESOURCE_NOT_FOUND", new { uri = parameters.Uri, path = assetPath });

            if (isDirectory && !parameters.Recursive)
                return Response.Error("RECURSIVE_REQUIRED", new { uri = parameters.Uri, path = assetPath });

            string currentSha = null;
            if (isFile)
            {
                currentSha = ResourceUriHelper.ComputeSha256(File.ReadAllBytes(fullPath));
                if (!string.IsNullOrWhiteSpace(parameters.PreconditionSha256) &&
                    !string.Equals(currentSha, parameters.PreconditionSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return Response.Error("STALE_FILE", new
                    {
                        uri = parameters.Uri,
                        expectedSha256 = parameters.PreconditionSha256,
                        currentSha256 = currentSha
                    });
                }
            }

            bool deletedByAssetDatabase = false;
            if (!string.IsNullOrEmpty(assetPath) && assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                deletedByAssetDatabase = AssetDatabase.DeleteAsset(assetPath);

            if (!deletedByAssetDatabase)
            {
                if (isDirectory)
                    Directory.Delete(fullPath, recursive: true);
                else
                    File.Delete(fullPath);
            }

            AssetDatabase.Refresh();
            return Response.Success("Resource deleted.", new
            {
                uri = parameters.Uri,
                path = assetPath,
                wasDirectory = isDirectory,
                sha256 = currentSha,
                deletedByAssetDatabase
            });
        }

        internal static bool TryResolveAllowedProjectPath(
            string uri,
            string projectRootOverride,
            out string projectRoot,
            out string fullPath,
            out string projectRelativePath,
            out string error)
        {
            projectRoot = ResourceUriHelper.ResolveProjectRoot(projectRootOverride);
            fullPath = ResolveSafePath(uri, projectRoot);
            projectRelativePath = fullPath != null ? ToProjectRelativePath(projectRoot, fullPath) : null;
            error = null;

            if (string.IsNullOrEmpty(fullPath))
            {
                error = "INVALID_URI: Uri must resolve inside the current Unity project.";
                return false;
            }

            if (!IsAllowedMutableRoot(projectRelativePath))
            {
                error = "PATH_OUTSIDE_ALLOWED_ROOTS: Mutating resource tools are restricted to Assets/ and Packages/.";
                return false;
            }

            return true;
        }

        internal static string ResolveSafePath(string uri, string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(uri))
                return null;

            if (uri.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                var full = Path.GetFullPath(Path.Combine(projectRoot, uri.Replace('/', Path.DirectorySeparatorChar)));
                return ResourceUriHelper.IsPathUnderProject(full, projectRoot) ? full : null;
            }

            return ResourceUriHelper.ResolveSafePathFromUri(uri, projectRoot);
        }

        internal static bool IsAllowedMutableRoot(string projectRelativePath) =>
            !string.IsNullOrWhiteSpace(projectRelativePath) &&
            (projectRelativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
             projectRelativePath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase));

        internal static string ToProjectRelativePath(string projectRoot, string fullPath)
        {
            var relative = Path.GetRelativePath(projectRoot, fullPath).Replace('\\', '/');
            return relative == "." ? string.Empty : relative;
        }
    }
}
