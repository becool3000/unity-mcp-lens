#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Helpers
{
    static class LensScratchRegistry
    {
        public const string AssetScratchRoot = "Assets/__LensScratch";
        public const string TempScratchRoot = "Temp/LensProbes";
        const string RegistryRelativePath = "ProjectSettings/Packages/com.becool3000.unity-mcp-lens/ScratchRegistry.json";

        sealed class RegistryFile
        {
            public int schemaVersion = 1;
            public List<ScratchArtifact> artifacts = new();
        }

        sealed class ScratchArtifact
        {
            public string id;
            public string owner;
            public string workflowId;
            public string kind;
            public string path;
            public string fullPath;
            public string createdUtc;
            public bool cleanupEligible;
            public string status;
        }

        public static object RegisterArtifact(
            string owner,
            string workflowId,
            string path,
            string kind = "artifact",
            bool cleanupEligible = true)
        {
            string normalizedPath = NormalizeProjectRelativePath(path);
            if (!IsApprovedScratchPath(normalizedPath))
                throw new InvalidOperationException($"Refusing to register non-scratch Lens artifact path '{path}'.");

            var registry = Load();
            var artifact = new ScratchArtifact
            {
                id = Guid.NewGuid().ToString("N"),
                owner = string.IsNullOrWhiteSpace(owner) ? "lens" : owner.Trim(),
                workflowId = string.IsNullOrWhiteSpace(workflowId) ? "unspecified" : workflowId.Trim(),
                kind = string.IsNullOrWhiteSpace(kind) ? "artifact" : kind.Trim(),
                path = normalizedPath,
                fullPath = ToFullPath(normalizedPath),
                createdUtc = DateTime.UtcNow.ToString("O"),
                cleanupEligible = cleanupEligible,
                status = "registered"
            };
            registry.artifacts.Add(artifact);
            Save(registry);
            return ToPublicArtifact(artifact);
        }

        public static object GetRegistrySummary()
        {
            var registry = Load();
            return new
            {
                registryPath = GetRegistryPath(),
                assetScratchRoot = AssetScratchRoot,
                tempScratchRoot = TempScratchRoot,
                artifactCount = registry.artifacts.Count,
                cleanupEligibleCount = registry.artifacts.Count(artifact => artifact.cleanupEligible && artifact.status == "registered"),
                artifacts = registry.artifacts
                    .OrderByDescending(artifact => artifact.createdUtc)
                    .Take(25)
                    .Select(ToPublicArtifact)
                    .ToArray()
            };
        }

        public static object CleanupRegisteredArtifacts(string owner = null, string workflowId = null, bool dryRun = true)
        {
            var registry = Load();
            var deleted = new List<object>();
            var skipped = new List<object>();
            bool changed = false;

            foreach (var artifact in registry.artifacts)
            {
                if (!artifact.cleanupEligible || artifact.status != "registered")
                    continue;
                if (!string.IsNullOrWhiteSpace(owner) && !string.Equals(artifact.owner, owner, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrWhiteSpace(workflowId) && !string.Equals(artifact.workflowId, workflowId, StringComparison.OrdinalIgnoreCase))
                    continue;

                string normalizedPath = NormalizeProjectRelativePath(artifact.path);
                if (!IsApprovedScratchPath(normalizedPath))
                {
                    skipped.Add(new { artifact.id, artifact.path, reason = "not_approved_scratch_path" });
                    continue;
                }

                string fullPath = ToFullPath(normalizedPath);
                if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                {
                    artifact.status = "missing";
                    changed = true;
                    skipped.Add(new { artifact.id, artifact.path, reason = "missing" });
                    continue;
                }

                if (!dryRun)
                {
                    if (Directory.Exists(fullPath))
                        Directory.Delete(fullPath, recursive: true);
                    else
                        File.Delete(fullPath);
                    artifact.status = "deleted";
                    changed = true;
                }

                deleted.Add(new { artifact.id, artifact.path, artifact.kind, dryRun });
            }

            if (changed)
                Save(registry);

            return new
            {
                dryRun,
                registryPath = GetRegistryPath(),
                deletedCount = deleted.Count,
                skippedCount = skipped.Count,
                deleted = deleted.ToArray(),
                skipped = skipped.ToArray()
            };
        }

        public static string GetRegistryPath() => Path.Combine(GetProjectRoot(), RegistryRelativePath);

        static RegistryFile Load()
        {
            string path = GetRegistryPath();
            if (!File.Exists(path))
                return new RegistryFile();

            try
            {
                return JsonConvert.DeserializeObject<RegistryFile>(File.ReadAllText(path)) ?? new RegistryFile();
            }
            catch
            {
                return new RegistryFile();
            }
        }

        static void Save(RegistryFile registry)
        {
            string path = GetRegistryPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, JsonConvert.SerializeObject(registry, Formatting.Indented));
            if (File.Exists(path))
                File.Replace(tempPath, path, null);
            else
                File.Move(tempPath, path);
        }

        static object ToPublicArtifact(ScratchArtifact artifact)
        {
            return new
            {
                artifact.id,
                artifact.owner,
                artifact.workflowId,
                artifact.kind,
                artifact.path,
                artifact.createdUtc,
                artifact.cleanupEligible,
                artifact.status
            };
        }

        static bool IsApprovedScratchPath(string path)
        {
            string normalized = NormalizeProjectRelativePath(path);
            return normalized.StartsWith(AssetScratchRoot + "/", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals(AssetScratchRoot, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(TempScratchRoot + "/", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals(TempScratchRoot, StringComparison.OrdinalIgnoreCase);
        }

        static string NormalizeProjectRelativePath(string path)
        {
            path = (path ?? string.Empty).Trim().Replace('\\', '/').TrimStart('/');
            string projectRoot = GetProjectRoot().Replace('\\', '/').TrimEnd('/');
            if (path.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
                path = path.Substring(projectRoot.Length + 1);
            return path;
        }

        static string ToFullPath(string projectRelativePath)
        {
            return Path.GetFullPath(Path.Combine(GetProjectRoot(), projectRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        static string GetProjectRoot()
        {
            return Path.GetFullPath(Path.GetDirectoryName(Application.dataPath) ?? Directory.GetCurrentDirectory())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
