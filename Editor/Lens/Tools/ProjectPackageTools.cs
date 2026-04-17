using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils;
using Becool.UnityMcpLens.Editor.Utils.Async;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public record ProjectGetInfoParams
    {
        [McpDescription("Sections to include: summary, unityVersion, settings, dependencies, packages, guidelines. Empty returns summary and unityVersion.", Required = false)]
        public string[] Sections { get; set; } = Array.Empty<string>();

        [McpDescription("Maximum number of packages to include when the packages section is requested.", Required = false)]
        public int PackageLimit { get; set; } = 80;

        [McpDescription("Maximum number of dependency entries to include when the dependencies section is requested.", Required = false)]
        public int DependencyLimit { get; set; } = 120;
    }

    public record ProjectPackagesParams
    {
        [McpDescription("Optional package name or display-name substring filter.", Required = false)]
        public string Query { get; set; }

        [McpDescription("Maximum packages to return.", Required = false)]
        public int Limit { get; set; } = 120;
    }

    public record ManagePackagesParams
    {
        [McpDescription("Package action: add, remove, embed, resolve.", Required = true)]
        public string Action { get; set; }

        [McpDescription("Package name or package ID. Required for add, remove, and embed.", Required = false)]
        public string Package { get; set; }
    }

    public static class ProjectPackageTools
    {
        const string GetInfoDescription = "Returns compact Unity project information through canonical Lens sections.";
        const string GetPackagesDescription = "Returns a compact read-only package list from Unity Package Manager metadata.";
        const string ManagePackagesDescription = "Performs high-risk package manager mutations. This tool is intended for the full/admin pack only.";

        [McpTool("Unity.Project.GetInfo", GetInfoDescription, "Get Unity Project Info", Groups = new[] { "project", "validation" }, EnabledByDefault = true)]
        public static object GetInfo(ProjectGetInfoParams parameters)
        {
            parameters ??= new ProjectGetInfoParams();
            var requested = NormalizeSections(parameters.Sections);
            if (requested.Count == 0)
            {
                requested.Add("summary");
                requested.Add("unityversion");
            }

            var projectRoot = GetProjectRoot();
            var data = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            if (requested.Contains("summary"))
            {
                data["summary"] = new
                {
                    projectRoot,
                    projectName = PlayerSettings.productName,
                    companyName = PlayerSettings.companyName,
                    activeBuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                    buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup.ToString(),
                    colorSpace = PlayerSettings.colorSpace.ToString(),
                    scriptingBackend = GetScriptingBackendName()
                };
            }

            if (requested.Contains("unityversion"))
            {
                data["unityVersion"] = new
                {
                    Application.unityVersion,
                    Application.platform,
                    Application.systemLanguage,
                    Application.isBatchMode
                };
            }

            if (requested.Contains("settings"))
                data["settings"] = ReadSettingsPreview(projectRoot);

            if (requested.Contains("dependencies"))
                data["dependencies"] = ReadManifestDependencies(projectRoot, Math.Max(1, parameters.DependencyLimit));

            if (requested.Contains("packages"))
                data["packages"] = GetRegisteredPackages(Math.Max(1, parameters.PackageLimit), null);

            if (requested.Contains("guidelines"))
                data["guidelines"] = ReadGuidelines(projectRoot);

            return Response.Success("Retrieved Unity project information.", new
            {
                sections = requested.ToArray(),
                data
            });
        }

        [McpTool("Unity.Project.GetPackages", GetPackagesDescription, "Get Unity Packages", Groups = new[] { "project" }, EnabledByDefault = true)]
        public static object GetPackages(ProjectPackagesParams parameters)
        {
            parameters ??= new ProjectPackagesParams();
            var packages = GetRegisteredPackages(Math.Max(1, parameters.Limit), parameters.Query);
            return Response.Success("Retrieved Unity package list.", packages);
        }

        [McpTool("Unity.Project.ManagePackages", ManagePackagesDescription, "Manage Unity Packages", Groups = new[] { "admin" }, EnabledByDefault = true)]
        public static async Task<object> ManagePackages(ManagePackagesParams parameters)
        {
            parameters ??= new ManagePackagesParams();
            var action = (parameters.Action ?? string.Empty).Trim().ToLowerInvariant();

            try
            {
                if (action == "resolve")
                {
                    Client.Resolve();
                    return Response.Success("Package manager resolve requested.", new
                    {
                        action
                    });
                }

                Request request = action switch
                {
                    "add" => Client.Add(RequirePackage(parameters.Package, action)),
                    "remove" => Client.Remove(RequirePackage(parameters.Package, action)),
                    "embed" => Client.Embed(RequirePackage(parameters.Package, action)),
                    _ => null
                };

                if (request == null)
                    return Response.Error("INVALID_PACKAGE_ACTION: action must be add, remove, embed, or resolve.");

                await request.WaitForCompletion();
                if (request.Status == StatusCode.Failure)
                {
                    return Response.Error("PACKAGE_ACTION_FAILED", new
                    {
                        action,
                        parameters.Package,
                        request.Error?.errorCode,
                        request.Error?.message
                    });
                }

                return Response.Success("Package manager action completed.", new
                {
                    action,
                    parameters.Package,
                    request.Status
                });
            }
            catch (Exception ex)
            {
                return Response.Error($"PACKAGE_ACTION_FAILED: {ex.Message}");
            }
        }

        static HashSet<string> NormalizeSections(IEnumerable<string> sections)
        {
            var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var section in sections ?? Array.Empty<string>())
            {
                var value = (section ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(value))
                    continue;

                normalized.Add(value.Replace("-", string.Empty).Replace("_", string.Empty));
            }

            return normalized;
        }

        static string RequirePackage(string package, string action)
        {
            if (string.IsNullOrWhiteSpace(package))
                throw new ArgumentException($"Package is required for '{action}'.");

            return package.Trim();
        }

        static string GetProjectRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        static string GetScriptingBackendName()
        {
#if UNITY_2021_2_OR_NEWER
            var namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            return PlayerSettings.GetScriptingBackend(namedBuildTarget).ToString();
#else
            return PlayerSettings.GetScriptingBackend(EditorUserBuildSettings.selectedBuildTargetGroup).ToString();
#endif
        }

        static object ReadSettingsPreview(string projectRoot)
        {
            var settingsPath = Path.Combine(projectRoot, "ProjectSettings", "ProjectSettings.asset");
            if (!File.Exists(settingsPath))
                return new { exists = false };

            var text = File.ReadAllText(settingsPath);
            var preview = PayloadBudgeting.CreateTextPreview(text, 80, PayloadBudgetPolicy.MaxProjectDataChars, out var truncated);
            return new
            {
                exists = true,
                path = "ProjectSettings/ProjectSettings.asset",
                sha256 = PayloadBudgeting.ComputeSha256(text),
                bytes = PayloadBudgeting.GetUtf8ByteCount(text),
                truncated,
                preview
            };
        }

        static object ReadManifestDependencies(string projectRoot, int limit)
        {
            var manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
            if (!File.Exists(manifestPath))
                return new { exists = false, count = 0, entries = Array.Empty<object>() };

            var json = JObject.Parse(File.ReadAllText(manifestPath));
            var dependencies = json["dependencies"] as JObject;
            var entries = dependencies?.Properties()
                .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .Select(property => new { name = property.Name, version = property.Value?.ToString() })
                .ToArray() ?? Array.Empty<object>();

            return new
            {
                exists = true,
                count = dependencies?.Count ?? 0,
                returned = entries.Length,
                entries
            };
        }

        static object GetRegisteredPackages(int limit, string query)
        {
            var packageQuery = (query ?? string.Empty).Trim();
            var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .Where(package => string.IsNullOrEmpty(packageQuery) ||
                    (package.name?.IndexOf(packageQuery, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (package.displayName?.IndexOf(packageQuery, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                .OrderBy(package => package.name, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .Select(package => new
                {
                    package.name,
                    package.displayName,
                    package.version,
                    package.source,
                    package.packageId,
                    package.assetPath,
                    isEmbedded = IsEmbedded(package),
                    isLocal = package.source == PackageSource.Local || package.source == PackageSource.Embedded
                })
                .ToArray();

            return new
            {
                count = packages.Length,
                packages
            };
        }

        static bool IsEmbedded(UnityEditor.PackageManager.PackageInfo package) =>
            package != null && package.source == PackageSource.Embedded;

        static object ReadGuidelines(string projectRoot)
        {
            var candidates = new[]
            {
                Path.Combine(projectRoot, "ProjectSettings", "Packages", "com.becool3000.unity-mcp-lens", "Settings.json"),
                Path.Combine(projectRoot, "ProjectSettings", "Packages", "com.unity.ai.mcp", "Settings.json")
            };

            foreach (var candidate in candidates)
            {
                if (!File.Exists(candidate))
                    continue;

                try
                {
                    var json = JObject.Parse(File.ReadAllText(candidate));
                    var guidelines = json["Guidelines"] ?? json["UserGuidelines"] ?? json["guidelines"];
                    if (guidelines != null)
                        return new { found = true, source = Path.GetRelativePath(projectRoot, candidate).Replace('\\', '/'), guidelines };
                }
                catch
                {
                    return new { found = false, error = "Guidelines settings file could not be parsed." };
                }
            }

            return new { found = false, guidelines = Array.Empty<string>() };
        }
    }
}
