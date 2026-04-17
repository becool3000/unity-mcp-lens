using System;
using System.IO;
using System.Linq;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.MCP.Editor.Tools
{
    public record AssetSearchParams
    {
        [McpDescription("Free-text AssetDatabase search query.", Required = false)]
        public string Query { get; set; }

        [McpDescription("Optional Unity asset type filter, for example Texture2D, Prefab, Material, Scene.", Required = false)]
        public string Type { get; set; }

        [McpDescription("Optional root folder. Defaults to Assets.", Required = false)]
        public string Folder { get; set; } = "Assets";

        [McpDescription("Optional label filters.", Required = false)]
        public string[] Labels { get; set; } = Array.Empty<string>();

        [McpDescription("Maximum results to return.", Required = false, Default = 50)]
        public int Limit { get; set; } = 50;

        [McpDescription("Include compact preview metadata such as dimensions for textures.", Required = false, Default = true)]
        public bool IncludePreviewMetadata { get; set; } = true;
    }

    public static class AssetSearchTools
    {
        const string Description = "Searches Unity assets and returns compact path, GUID, label, type, and preview metadata.";

        [McpTool("Unity.Asset.Search", Description, "Search Unity Assets", Groups = new[] { "assets", "project" }, EnabledByDefault = true)]
        public static object Search(AssetSearchParams parameters)
        {
            parameters ??= new AssetSearchParams();
            int limit = Math.Clamp(parameters.Limit, 1, 200);
            string filter = BuildFilter(parameters);
            string[] folders = ResolveFolders(parameters.Folder);

            var guids = AssetDatabase.FindAssets(filter, folders)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .ToArray();

            var results = guids
                .Select(guid => BuildAssetSummary(guid, parameters.IncludePreviewMetadata))
                .Where(summary => summary != null)
                .ToArray();

            return Response.Success("Asset search completed.", new
            {
                query = parameters.Query,
                type = parameters.Type,
                labels = parameters.Labels ?? Array.Empty<string>(),
                folder = parameters.Folder,
                filter,
                returned = results.Length,
                truncated = results.Length == limit,
                assets = results
            });
        }

        static string BuildFilter(AssetSearchParams parameters)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(parameters.Query))
                parts.Add(parameters.Query.Trim());

            if (!string.IsNullOrWhiteSpace(parameters.Type))
                parts.Add($"t:{parameters.Type.Trim()}");

            foreach (var label in parameters.Labels ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(label))
                    parts.Add($"l:{label.Trim()}");
            }

            return string.Join(" ", parts);
        }

        static string[] ResolveFolders(string folder)
        {
            var normalized = string.IsNullOrWhiteSpace(folder) ? "Assets" : folder.Replace('\\', '/').TrimEnd('/');
            if (!normalized.StartsWith("Assets", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("Packages", StringComparison.OrdinalIgnoreCase))
                normalized = "Assets/" + normalized.TrimStart('/');

            if (AssetDatabase.IsValidFolder(normalized))
                return new[] { normalized };

            return new[] { "Assets" };
        }

        static object BuildAssetSummary(string guid, bool includePreviewMetadata)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrWhiteSpace(path))
                return null;

            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            var labels = AssetDatabase.GetLabels(AssetDatabase.LoadMainAssetAtPath(path));
            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), path);
            long? sizeBytes = File.Exists(fullPath) ? new FileInfo(fullPath).Length : null;
            object preview = includePreviewMetadata ? GetPreviewMetadata(path, type) : null;

            return new
            {
                guid,
                path,
                name = Path.GetFileNameWithoutExtension(path),
                extension = Path.GetExtension(path),
                type = type?.FullName,
                labels,
                sizeBytes,
                preview
            };
        }

        static object GetPreviewMetadata(string path, Type type)
        {
            if (type == typeof(Texture2D) || type == typeof(Sprite))
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null)
                {
                    return new
                    {
                        kind = "texture",
                        texture.width,
                        texture.height,
                        texture.format,
                        texture.mipmapCount
                    };
                }
            }

            if (type == typeof(GameObject))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    return new
                    {
                        kind = "gameObject",
                        componentCount = prefab.GetComponents<Component>().Count(component => component != null),
                        childCount = prefab.transform.childCount
                    };
                }
            }

            return null;
        }
    }
}
