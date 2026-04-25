using System;
using System.Collections.Generic;
using System.Linq;
using Becool.UnityMcpLens.Editor.Utils;
using Becool.UnityMcpLens.Editor.ToolRegistry;

namespace Becool.UnityMcpLens.Editor.Lens
{
    sealed class ToolPackDefinition
    {
        public string Id { get; }
        public string Title { get; }
        public string Description { get; }
        public bool AlwaysOn { get; }
        public bool AdminOnly { get; }
        public string[] IncludeGroups { get; }
        public string[] IncludeTools { get; }
        public string[] ExcludeTools { get; }
        public string[] RecommendedNextPacks { get; }

        public ToolPackDefinition(
            string id,
            string title,
            string description,
            bool alwaysOn = false,
            bool adminOnly = false,
            string[] includeGroups = null,
            string[] includeTools = null,
            string[] excludeTools = null,
            string[] recommendedNextPacks = null)
        {
            Id = id;
            Title = title;
            Description = description;
            AlwaysOn = alwaysOn;
            AdminOnly = adminOnly;
            IncludeGroups = includeGroups ?? Array.Empty<string>();
            IncludeTools = includeTools ?? Array.Empty<string>();
            ExcludeTools = excludeTools ?? Array.Empty<string>();
            RecommendedNextPacks = recommendedNextPacks ?? Array.Empty<string>();
        }
    }

    sealed class ToolPackSummary
    {
        public string packId { get; set; }
        public string title { get; set; }
        public string description { get; set; }
        public bool alwaysOn { get; set; }
        public bool adminOnly { get; set; }
        public bool isActive { get; set; }
        public string[] recommendedNextPacks { get; set; }
    }

    static class ToolPackCatalog
    {
        public const string FoundationPackId = "foundation";
        public const string ConsolePackId = "console";
        public const string ProjectPackId = "project";
        public const string ScenePackId = "scene";
        public const string UiPackId = "ui";
        public const string ScriptingPackId = "scripting";
        public const string AssetsPackId = "assets";
        public const string DebugPackId = "debug";
        public const string FullPackId = "full";
        public const int MaxAdditionalPacks = 2;

        public const string GetLensHealthToolName = "Unity.GetLensHealth";
        public const string ListToolPacksToolName = "Unity.ListToolPacks";
        public const string SetToolPacksToolName = "Unity.SetToolPacks";
        public const string ReadDetailRefToolName = "Unity.ReadDetailRef";

        static string NormalizeToolName(string toolName) => McpToolRegistry.NormalizeToolName(toolName) ?? string.Empty;

        static readonly string[] k_FoundationToolNames =
        {
            NormalizeToolName(GetLensHealthToolName),
            NormalizeToolName(ListToolPacksToolName),
            NormalizeToolName(SetToolPacksToolName),
            NormalizeToolName(ReadDetailRefToolName),
            NormalizeToolName("Unity.ReadConsole"),
            NormalizeToolName("Unity.ListResources"),
            NormalizeToolName("Unity.ReadResource"),
            NormalizeToolName("Unity.FindInFile"),
            NormalizeToolName("Unity.GetSha"),
            NormalizeToolName("Unity.ValidateScript"),
            NormalizeToolName("Unity.ManageScript_capabilities"),
            NormalizeToolName("Unity.Project.GetInfo")
        };

        static readonly Dictionary<string, ToolPackDefinition> k_Definitions = new(StringComparer.OrdinalIgnoreCase)
        {
            [FoundationPackId] = new(
                FoundationPackId,
                "Foundation",
                "Narrow default surface for discovery, safe reads, and pack control.",
                alwaysOn: true,
                includeTools: k_FoundationToolNames,
                recommendedNextPacks: new[] { ConsolePackId, ProjectPackId, ScriptingPackId }),
            [ConsolePackId] = new(
                ConsolePackId,
                "Console Diagnostics",
                "Console reads, health checks, and lightweight troubleshooting.",
                includeTools: new[] { NormalizeToolName("Unity.ReadConsole"), NormalizeToolName("Unity.ManageEditor"), NormalizeToolName("Unity.ManageMenuItem") },
                recommendedNextPacks: new[] { ProjectPackId, DebugPackId }),
            [ProjectPackId] = new(
                ProjectPackId,
                "Project Diagnostics",
                "Project-wide scans, package/import diagnostics, guidelines, and lightweight validation.",
                includeGroups: new[] { "project", "validation" },
                includeTools: new[] { NormalizeToolName("Unity.Project.GetInfo"), NormalizeToolName("Unity.Project.GetPackages") },
                recommendedNextPacks: new[] { ScriptingPackId, AssetsPackId }),
            [ScenePackId] = new(
                ScenePackId,
                "Scene Editing",
                "Scene hierarchy, runtime snapshots, and GameObject operations.",
                includeGroups: new[] { "scene", "runtime" },
                recommendedNextPacks: new[] { UiPackId, DebugPackId }),
            [UiPackId] = new(
                UiPackId,
                "UI Authoring",
                "UI hierarchy authoring, layout verification, raycasts, regions, and capture tools.",
                includeGroups: new[] { "ui" },
                recommendedNextPacks: new[] { ScenePackId, DebugPackId }),
            [ScriptingPackId] = new(
                ScriptingPackId,
                "Scripting",
                "Script/resource reads, edits, and validation workflows.",
                includeGroups: new[] { "scripting", "resources" },
                recommendedNextPacks: new[] { ProjectPackId, AssetsPackId }),
            [AssetsPackId] = new(
                AssetsPackId,
                "Assets",
                "Asset, prefab, import, and generation operations.",
                includeGroups: new[] { "assets", "external" },
                recommendedNextPacks: new[] { ScenePackId, ProjectPackId }),
            [DebugPackId] = new(
                DebugPackId,
                "Debug",
                "Profiler, diagnostics, and deeper troubleshooting tools.",
                includeGroups: new[] { "debug", "diagnostics", "profiler" },
                recommendedNextPacks: new[] { ConsolePackId, ScenePackId }),
            [FullPackId] = new(
                FullPackId,
                "Full",
                "Administrative escape hatch exposing the full enabled tool surface.",
                adminOnly: true,
                recommendedNextPacks: Array.Empty<string>())
        };

        static readonly string s_ProfileCatalogVersion = PayloadBudgeting.ComputeSha256(
            string.Join("|", k_Definitions.OrderBy(kvp => kvp.Key).Select(kvp =>
                $"{kvp.Key}:{kvp.Value.Title}:{string.Join(",", kvp.Value.IncludeGroups)}:{string.Join(",", kvp.Value.IncludeTools)}:{string.Join(",", kvp.Value.ExcludeTools)}")));

        public static string ProfileCatalogVersion => s_ProfileCatalogVersion;

        public static IReadOnlyCollection<string> AlwaysVisibleToolNames => k_FoundationToolNames;

        public static string[] DefaultActivePacks => new[] { FoundationPackId };

        public static IEnumerable<ToolPackSummary> GetPackSummaries(IEnumerable<string> activePacks)
        {
            var active = new HashSet<string>(NormalizeRequestedPacks(activePacks), StringComparer.OrdinalIgnoreCase);
            foreach (var definition in k_Definitions.Values.OrderBy(definition => definition.Id, StringComparer.Ordinal))
            {
                yield return new ToolPackSummary
                {
                    packId = definition.Id,
                    title = definition.Title,
                    description = definition.Description,
                    alwaysOn = definition.AlwaysOn,
                    adminOnly = definition.AdminOnly,
                    isActive = active.Contains(definition.Id),
                    recommendedNextPacks = definition.RecommendedNextPacks
                };
            }
        }

        public static string[] GetRecommendedNextPacks(IEnumerable<string> activePacks)
        {
            var active = new HashSet<string>(NormalizeRequestedPacks(activePacks), StringComparer.OrdinalIgnoreCase);
            var recommendations = new List<string>();

            foreach (var packId in active)
            {
                if (!k_Definitions.TryGetValue(packId, out var definition))
                    continue;

                foreach (var nextPack in definition.RecommendedNextPacks)
                {
                    if (!active.Contains(nextPack) && !recommendations.Contains(nextPack, StringComparer.OrdinalIgnoreCase))
                        recommendations.Add(nextPack);
                }
            }

            return recommendations.ToArray();
        }

        public static bool TryNormalizeSelection(IEnumerable<string> requestedPacks, out string[] normalizedPacks, out string error)
        {
            var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                FoundationPackId
            };

            foreach (var requested in requestedPacks ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(requested))
                    continue;

                if (!k_Definitions.ContainsKey(requested))
                {
                    normalizedPacks = Array.Empty<string>();
                    error = $"Unknown tool pack '{requested}'.";
                    return false;
                }

                normalized.Add(requested.Trim());
            }

            if (normalized.Contains(FullPackId))
            {
                normalizedPacks = new[] { FoundationPackId, FullPackId };
                error = null;
                return true;
            }

            var additionalPackCount = normalized.Count(packId => !string.Equals(packId, FoundationPackId, StringComparison.OrdinalIgnoreCase));
            if (additionalPackCount > MaxAdditionalPacks)
            {
                normalizedPacks = Array.Empty<string>();
                error = $"At most {MaxAdditionalPacks} non-foundation tool packs may be active at once.";
                return false;
            }

            normalizedPacks = normalized
                .OrderBy(packId => string.Equals(packId, FoundationPackId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(packId => packId, StringComparer.Ordinal)
                .ToArray();
            error = null;
            return true;
        }

        public static string[] NormalizeRequestedPacks(IEnumerable<string> requestedPacks)
        {
            TryNormalizeSelection(requestedPacks, out var normalizedPacks, out _);
            return normalizedPacks.Length > 0 ? normalizedPacks : DefaultActivePacks;
        }

        public static string[] GetMatchingPackIds(string toolName, IReadOnlyCollection<string> groups)
        {
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(toolName))
                return Array.Empty<string>();

            string normalizedToolName = NormalizeToolName(toolName);
            if (k_FoundationToolNames.Contains(normalizedToolName, StringComparer.OrdinalIgnoreCase))
                results.Add(FoundationPackId);

            foreach (var definition in k_Definitions.Values)
            {
                if (string.Equals(definition.Id, FoundationPackId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.Equals(definition.Id, FullPackId, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(FullPackId);
                    continue;
                }

                if (MatchesDefinition(definition, toolName, groups))
                    results.Add(definition.Id);
            }

            return results.OrderBy(packId => packId, StringComparer.Ordinal).ToArray();
        }

        public static bool ShouldIncludeTool(string toolName, IReadOnlyCollection<string> groups, IEnumerable<string> activePacks)
        {
            var normalizedPacks = NormalizeRequestedPacks(activePacks);
            if (normalizedPacks.Contains(FullPackId, StringComparer.OrdinalIgnoreCase))
                return true;

            string normalizedToolName = NormalizeToolName(toolName);
            if (k_FoundationToolNames.Contains(normalizedToolName, StringComparer.OrdinalIgnoreCase))
                return true;

            foreach (var packId in normalizedPacks)
            {
                if (!k_Definitions.TryGetValue(packId, out var definition))
                    continue;

                if (MatchesDefinition(definition, toolName, groups))
                    return true;
            }

            return false;
        }

        public static bool IsReadOnlyHint(string toolName)
        {
            return ToolMetadataPolicy.IsReadOnlyHint(toolName);
        }

        static bool MatchesDefinition(ToolPackDefinition definition, string toolName, IReadOnlyCollection<string> groups)
        {
            if (definition == null || string.IsNullOrWhiteSpace(toolName))
                return false;

            string normalizedToolName = NormalizeToolName(toolName);
            if (definition.ExcludeTools.Any(excludeTool => string.Equals(NormalizeToolName(excludeTool), normalizedToolName, StringComparison.OrdinalIgnoreCase)))
                return false;

            if (definition.IncludeTools.Any(includeTool => string.Equals(NormalizeToolName(includeTool), normalizedToolName, StringComparison.OrdinalIgnoreCase)))
                return true;

            if (groups == null || groups.Count == 0)
                return false;

            return definition.IncludeGroups.Any(group => groups.Contains(group, StringComparer.OrdinalIgnoreCase));
        }
    }
}
