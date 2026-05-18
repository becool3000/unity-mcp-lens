#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class ProjectBlockedLanguageScanTools
    {
        const string ToolName = "Unity.Project.BlockedLanguageScan";

        sealed class Request
        {
            public string[] BlockedTerms = Array.Empty<string>();
            public string TermsAssetPath;
            public string MatchMode = "literal";
            public bool CaseSensitive;
            public bool WholeWord;
            public string Under = "Assets";
            public string[] AssetPaths = Array.Empty<string>();
            public bool IncludeScripts = true;
            public bool IncludeScenes = true;
            public bool IncludeLoadedScenes = true;
            public bool IncludePrefabs = true;
            public bool IncludeScriptableObjects = true;
            public bool IncludeTextAssets = true;
            public bool IncludePackages;
            public int MaxAssets = 500;
            public int MaxFindings = PayloadBudgetPolicy.MaxDiagnosticFindings;
            public int MaxTextBytes = 4 * 1024 * 1024;
            public int MaxSerializedObjectsPerAsset = 1000;
            public int ContextChars = 60;
        }

        sealed class TermMatcher
        {
            public string Term;
            public Regex Regex;
        }

        sealed class ScanAccumulator
        {
            public int FindingCount;
            public int StoredFindingCount;
            public readonly List<object> Findings = new();
            public readonly Dictionary<string, int> TermCounts = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, int> AssetKindCounts = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, int> FieldKindCounts = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, int> ScannedSourceCounts = new(StringComparer.OrdinalIgnoreCase);
            public readonly List<object> Warnings = new();

            public void AddFinding(Request request, object finding, string term, string assetKind, string fieldKind)
            {
                FindingCount++;
                TermCounts[term] = TermCounts.TryGetValue(term, out int termCount) ? termCount + 1 : 1;
                AssetKindCounts[assetKind] = AssetKindCounts.TryGetValue(assetKind, out int assetCount) ? assetCount + 1 : 1;
                FieldKindCounts[fieldKind] = FieldKindCounts.TryGetValue(fieldKind, out int fieldCount) ? fieldCount + 1 : 1;
                if (Findings.Count < request.MaxFindings)
                {
                    Findings.Add(finding);
                    StoredFindingCount++;
                }
            }

            public void IncrementScanned(string source)
            {
                ScannedSourceCounts[source] = ScannedSourceCounts.TryGetValue(source, out int count) ? count + 1 : 1;
            }
        }

        [McpSchema(ToolName)]
        public static object GetSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    blockedTerms = new { type = "array", description = "Project-specific forbidden words or phrases. Literal by default.", items = new { type = "string" } },
                    terms = new { type = "array", description = "Alias for blockedTerms.", items = new { type = "string" } },
                    forbiddenTerms = new { type = "array", description = "Alias for blockedTerms.", items = new { type = "string" } },
                    blockedTermsPath = new { type = "string", description = "Optional path to a terms file with one blocked term per line." },
                    termFile = new { type = "string", description = "Alias for blockedTermsPath." },
                    termsAssetPath = new { type = "string", description = "Optional TextAsset path under Assets/ or Packages/ with one blocked term per line." },
                    matchMode = new { type = "string", description = "literal or regex. Defaults to literal." },
                    caseSensitive = new { type = "boolean", description = "Use case-sensitive matching. Defaults to false." },
                    wholeWord = new { type = "boolean", description = "For literal terms, match whole words only. Defaults to false." },
                    under = new { type = "string", description = "Folder under Assets/ or Packages/ to scan. Defaults to Assets." },
                    assetPaths = new { type = "array", description = "Explicit asset paths to scan in addition to under.", items = new { type = "string" } },
                    includeScripts = new { type = "boolean", description = "Scan script/source text files. Defaults to true." },
                    includeScenes = new { type = "boolean", description = "Scan .unity scene assets as serialized text. Defaults to true." },
                    includeLoadedScenes = new { type = "boolean", description = "Scan currently loaded scene GameObject/component serialized strings. Defaults to true." },
                    includePrefabs = new { type = "boolean", description = "Scan prefab GameObject/component serialized strings. Defaults to true." },
                    includeScriptableObjects = new { type = "boolean", description = "Scan ScriptableObject serialized strings. Defaults to true." },
                    includeTextAssets = new { type = "boolean", description = "Scan TextAsset-like project files such as json, txt, uxml, uss, shader, and asmdef. Defaults to true." },
                    includePackages = new { type = "boolean", description = "Include Packages/ when under is not explicit. Defaults to false." },
                    maxAssets = new { type = "integer", description = "Maximum asset files to inspect. Defaults to 500 and is capped at 5000." },
                    maxFindings = new { type = "integer", description = "Maximum finding rows returned inline. Defaults to diagnostic policy cap." },
                    maxTextBytes = new { type = "integer", description = "Maximum bytes read from a single text file. Defaults to 4 MiB." },
                    maxSerializedObjectsPerAsset = new { type = "integer", description = "Maximum Unity objects/components inspected per serialized asset. Defaults to 1000." },
                    contextChars = new { type = "integer", description = "Characters of context around each match. Defaults to 60 and is capped at 200." }
                },
                required = Array.Empty<string>()
            };
        }

        [McpTool(ToolName,
            "Scans scripts, loaded scenes, scene assets, prefabs, ScriptableObjects, and UI/string-bearing serialized fields for project-specific blocked language.",
            "Blocked Language Scan",
            Groups = new[] { "project", "diagnostics" },
            EnabledByDefault = true)]
        public static object BlockedLanguageScan(JObject @params)
        {
            @params ??= new JObject();
            var timing = new ToolOperationTiming(ToolName, "blocked_language_scan", PayloadBudgeting.GetUtf8ByteCount(@params.ToString(Formatting.None)));
            bool success = true;
            string errorKind = null;
            object data;

            try
            {
                Request request;
                using (timing.Measure("normalization"))
                {
                    request = Normalize(@params);
                }

                using (timing.Measure("service"))
                {
                    data = Scan(request);
                }
            }
            catch (Exception ex)
            {
                success = false;
                errorKind = ex.GetType().Name;
                data = new
                {
                    status = "failed",
                    errorKind,
                    error = ex.Message
                };
            }

            object response;
            using (timing.Measure("result_shaping"))
            {
                response = success
                    ? Response.Success("Blocked-language scan completed.", ToolResultCompactor.ShapeStructuredPayload(
                        ToolName,
                        data,
                        BuildCompactData(data),
                        new { kind = "project_blocked_language_scan_full_result" },
                        "project_blocked_language_scan",
                        detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes))
                    : Response.Error("PROJECT_BLOCKED_LANGUAGE_SCAN_FAILED", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, success ? null : errorKind);
            return response;
        }

        static Request Normalize(JObject parameters)
        {
            var request = new Request
            {
                BlockedTerms = GetStringArray(parameters, "blockedTerms", "BlockedTerms", "terms", "Terms", "forbiddenTerms", "ForbiddenTerms"),
                TermsAssetPath = NormalizeProjectAssetPath(GetString(
                    parameters,
                    "termsAssetPath",
                    "TermsAssetPath",
                    "termsPath",
                    "TermsPath",
                    "blockedTermsPath",
                    "BlockedTermsPath",
                    "termFile",
                    "TermFile"), defaultPath: null),
                MatchMode = (GetString(parameters, "matchMode", "MatchMode") ?? "literal").Trim().ToLowerInvariant(),
                CaseSensitive = GetBool(parameters, false, "caseSensitive", "CaseSensitive"),
                WholeWord = GetBool(parameters, false, "wholeWord", "WholeWord"),
                Under = NormalizeProjectAssetPath(GetString(parameters, "under", "Under"), defaultPath: "Assets"),
                AssetPaths = GetStringArray(parameters, "assetPaths", "AssetPaths")
                    .Select(path => NormalizeProjectAssetPath(path, defaultPath: null))
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                IncludeScripts = GetBool(parameters, true, "includeScripts", "IncludeScripts"),
                IncludeScenes = GetBool(parameters, true, "includeScenes", "IncludeScenes"),
                IncludeLoadedScenes = GetBool(parameters, true, "includeLoadedScenes", "IncludeLoadedScenes", "includeOpenScenes", "IncludeOpenScenes"),
                IncludePrefabs = GetBool(parameters, true, "includePrefabs", "IncludePrefabs"),
                IncludeScriptableObjects = GetBool(parameters, true, "includeScriptableObjects", "IncludeScriptableObjects"),
                IncludeTextAssets = GetBool(parameters, true, "includeTextAssets", "IncludeTextAssets"),
                IncludePackages = GetBool(parameters, false, "includePackages", "IncludePackages"),
                MaxAssets = Math.Clamp(GetInt(parameters, 500, "maxAssets", "MaxAssets"), 1, 5000),
                MaxFindings = Math.Clamp(GetInt(parameters, PayloadBudgetPolicy.MaxDiagnosticFindings, "maxFindings", "MaxFindings"), 1, PayloadBudgetPolicy.MaxDiagnosticFindings),
                MaxTextBytes = Math.Clamp(GetInt(parameters, 4 * 1024 * 1024, "maxTextBytes", "MaxTextBytes"), 1024, 64 * 1024 * 1024),
                MaxSerializedObjectsPerAsset = Math.Clamp(GetInt(parameters, 1000, "maxSerializedObjectsPerAsset", "MaxSerializedObjectsPerAsset"), 1, 10000),
                ContextChars = Math.Clamp(GetInt(parameters, 60, "contextChars", "ContextChars"), 0, 200)
            };

            if (!request.MatchMode.Equals("literal", StringComparison.Ordinal) &&
                !request.MatchMode.Equals("regex", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("matchMode must be 'literal' or 'regex'.");
            }

            request.BlockedTerms = ResolveTerms(request)
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Select(term => term.Trim())
                .Distinct(request.CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (request.BlockedTerms.Length == 0)
                throw new InvalidOperationException("At least one blocked term is required.");

            return request;
        }

        static object Scan(Request request)
        {
            TermMatcher[] matchers = BuildMatchers(request);
            var accumulator = new ScanAccumulator();
            string[] candidateAssetPaths = ResolveAssetPaths(request);
            string[] assetPaths = candidateAssetPaths.Take(request.MaxAssets).ToArray();

            if (request.IncludeLoadedScenes)
                ScanLoadedScenes(request, matchers, accumulator);

            foreach (string assetPath in assetPaths)
            {
                if (accumulator.StoredFindingCount >= request.MaxFindings && accumulator.FindingCount >= request.MaxFindings)
                {
                    // Continue counts would be expensive once output is saturated; report truncation clearly.
                    break;
                }

                ScanAssetPath(assetPath, request, matchers, accumulator);
            }

            bool truncated = accumulator.FindingCount > accumulator.StoredFindingCount ||
                accumulator.StoredFindingCount >= request.MaxFindings ||
                candidateAssetPaths.Length > assetPaths.Length;

            return new
            {
                status = "ready",
                termCount = request.BlockedTerms.Length,
                terms = request.BlockedTerms,
                blockedTermCount = request.BlockedTerms.Length,
                blockedTerms = request.BlockedTerms,
                matchMode = request.MatchMode,
                caseSensitive = request.CaseSensitive,
                wholeWord = request.WholeWord,
                under = request.Under,
                explicitAssetPathCount = request.AssetPaths.Length,
                candidateAssetCount = candidateAssetPaths.Length,
                scannedAssetCount = assetPaths.Length,
                scannedSourceCounts = ToCountRows(accumulator.ScannedSourceCounts),
                findingCount = accumulator.FindingCount,
                storedFindingCount = accumulator.StoredFindingCount,
                truncated,
                maxAssets = request.MaxAssets,
                maxFindings = request.MaxFindings,
                contextChars = request.ContextChars,
                termCounts = ToCountRows(accumulator.TermCounts),
                assetKindCounts = ToCountRows(accumulator.AssetKindCounts),
                fieldKindCounts = ToCountRows(accumulator.FieldKindCounts),
                warnings = accumulator.Warnings,
                findings = accumulator.Findings,
                importSideEffects = new
                {
                    mutatesAssets = false,
                    usesAssetDatabaseFindAssets = true,
                    readsSerializedObjects = true,
                    readsTextFiles = true,
                    opensScenes = false,
                    requestsImport = false,
                    callsRefresh = false
                }
            };
        }

        static void ScanLoadedScenes(Request request, TermMatcher[] matchers, ScanAccumulator accumulator)
        {
            int inspectedObjects = 0;
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                accumulator.IncrementScanned("loaded_scene");
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    TraverseGameObject(root, scene.path, "loaded_scene", request, matchers, accumulator, ref inspectedObjects);
                    if (inspectedObjects >= request.MaxSerializedObjectsPerAsset)
                    {
                        accumulator.Warnings.Add(new { kind = "loaded_scene_object_limit", scene = scene.path, maxSerializedObjectsPerAsset = request.MaxSerializedObjectsPerAsset });
                        break;
                    }
                }
            }
        }

        static void ScanAssetPath(string assetPath, Request request, TermMatcher[] matchers, ScanAccumulator accumulator)
        {
            string kind = ClassifyAssetKind(assetPath);
            if (!ShouldScanKind(kind, request))
                return;

            switch (kind)
            {
                case "script":
                case "scene_asset":
                case "text_asset":
                    ScanTextFile(assetPath, kind, request, matchers, accumulator);
                    break;
                case "prefab":
                    ScanPrefab(assetPath, request, matchers, accumulator);
                    break;
                case "scriptable_object":
                    ScanScriptableObjectAsset(assetPath, request, matchers, accumulator);
                    break;
            }
        }

        static void ScanPrefab(string assetPath, Request request, TermMatcher[] matchers, ScanAccumulator accumulator)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                ScanTextFile(assetPath, "prefab_yaml_fallback", request, matchers, accumulator);
                return;
            }

            accumulator.IncrementScanned("prefab");
            int inspectedObjects = 0;
            TraverseGameObject(prefab, assetPath, "prefab", request, matchers, accumulator, ref inspectedObjects);
            if (inspectedObjects >= request.MaxSerializedObjectsPerAsset)
                accumulator.Warnings.Add(new { kind = "prefab_object_limit", assetPath, maxSerializedObjectsPerAsset = request.MaxSerializedObjectsPerAsset });
        }

        static void TraverseGameObject(GameObject gameObject, string ownerPath, string assetKind, Request request, TermMatcher[] matchers, ScanAccumulator accumulator, ref int inspectedObjects)
        {
            if (gameObject == null || inspectedObjects >= request.MaxSerializedObjectsPerAsset)
                return;

            string hierarchyPath = BuildHierarchyPath(gameObject.transform);
            ScanTextValue(ownerPath, assetKind, "game_object_name", hierarchyPath, gameObject.name, gameObject.name, request, matchers, accumulator);
            foreach (Component component in gameObject.GetComponents<Component>())
            {
                if (component == null)
                    continue;

                inspectedObjects++;
                ScanUnityObject(component, ownerPath, assetKind, hierarchyPath, request, matchers, accumulator);
                if (inspectedObjects >= request.MaxSerializedObjectsPerAsset)
                    break;
            }

            for (int index = 0; index < gameObject.transform.childCount && inspectedObjects < request.MaxSerializedObjectsPerAsset; index++)
            {
                TraverseGameObject(gameObject.transform.GetChild(index).gameObject, ownerPath, assetKind, request, matchers, accumulator, ref inspectedObjects);
            }
        }

        static void ScanScriptableObjectAsset(string assetPath, Request request, TermMatcher[] matchers, ScanAccumulator accumulator)
        {
            UnityEngine.Object[] objects = AssetDatabase.LoadAllAssetsAtPath(assetPath) ?? Array.Empty<UnityEngine.Object>();
            ScriptableObject[] scriptableObjects = objects.OfType<ScriptableObject>().Take(request.MaxSerializedObjectsPerAsset).ToArray();
            if (scriptableObjects.Length == 0)
            {
                ScanTextFile(assetPath, "asset_yaml_fallback", request, matchers, accumulator);
                return;
            }

            accumulator.IncrementScanned("scriptable_object");
            foreach (ScriptableObject scriptableObject in scriptableObjects)
            {
                ScanUnityObject(scriptableObject, assetPath, "scriptable_object", scriptableObject.name, request, matchers, accumulator);
            }
        }

        static void ScanUnityObject(UnityEngine.Object target, string ownerPath, string assetKind, string hierarchyPath, Request request, TermMatcher[] matchers, ScanAccumulator accumulator)
        {
            if (target == null)
                return;

            try
            {
                SerializedObject serializedObject = new(target);
                SerializedProperty iterator = serializedObject.GetIterator();
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (iterator.propertyType != SerializedPropertyType.String)
                        continue;

                    string value = iterator.stringValue;
                    if (string.IsNullOrEmpty(value))
                        continue;

                    string fieldKind = IsLikelyUiTextProperty(target, iterator.propertyPath) ? "ui_string" : "serialized_string";
                    ScanTextValue(
                        ownerPath,
                        assetKind,
                        fieldKind,
                        hierarchyPath,
                        target.GetType().FullName,
                        $"{target.GetType().Name}.{iterator.propertyPath}",
                        value,
                        request,
                        matchers,
                        accumulator);
                }
            }
            catch (Exception ex)
            {
                accumulator.Warnings.Add(new { kind = "serialized_object_scan_failed", ownerPath, target = target.name, targetType = target.GetType().FullName, error = ex.Message });
            }
        }

        static void ScanTextFile(string assetPath, string assetKind, Request request, TermMatcher[] matchers, ScanAccumulator accumulator)
        {
            string fullPath = ToFullPath(assetPath);
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
                return;

            FileInfo info = new(fullPath);
            if (info.Length > request.MaxTextBytes)
            {
                accumulator.Warnings.Add(new { kind = "text_file_too_large", assetPath, bytes = info.Length, maxTextBytes = request.MaxTextBytes });
                return;
            }

            accumulator.IncrementScanned(assetKind);
            int lineNumber = 0;
            IEnumerable<string> lines;
            try
            {
                lines = File.ReadLines(fullPath);
            }
            catch (Exception ex)
            {
                accumulator.Warnings.Add(new { kind = "text_file_read_failed", assetPath, error = ex.Message });
                return;
            }

            try
            {
                foreach (string line in lines)
                {
                    lineNumber++;
                    ScanLine(assetPath, assetKind, lineNumber, line, request, matchers, accumulator);
                }
            }
            catch (Exception ex)
            {
                accumulator.Warnings.Add(new { kind = "text_file_scan_failed", assetPath, line = lineNumber, error = ex.Message });
            }
        }

        static void ScanLine(string assetPath, string assetKind, int lineNumber, string line, Request request, TermMatcher[] matchers, ScanAccumulator accumulator)
        {
            foreach (object finding in BuildFindings(line, request, matchers))
            {
                JObject row = JObject.FromObject(finding);
                string term = row.Value<string>("term");
                accumulator.AddFinding(request, new
                {
                    assetPath,
                    assetKind,
                    fieldKind = "text_line",
                    line = lineNumber,
                    term,
                    match = row.Value<string>("match"),
                    index = row.Value<int>("index"),
                    column = row.Value<int>("index") + 1,
                    excerpt = row.Value<string>("excerpt")
                }, term, assetKind, "text_line");
            }
        }

        static void ScanTextValue(string ownerPath, string assetKind, string fieldKind, string hierarchyPath, string objectName, string value, Request request, TermMatcher[] matchers, ScanAccumulator accumulator)
        {
            ScanTextValue(ownerPath, assetKind, fieldKind, hierarchyPath, objectName, null, value, request, matchers, accumulator);
        }

        static void ScanTextValue(string ownerPath, string assetKind, string fieldKind, string hierarchyPath, string objectName, string propertyPath, string value, Request request, TermMatcher[] matchers, ScanAccumulator accumulator)
        {
            foreach (object finding in BuildFindings(value, request, matchers))
            {
                JObject row = JObject.FromObject(finding);
                string term = row.Value<string>("term");
                accumulator.AddFinding(request, new
                {
                    assetPath = ownerPath,
                    assetKind,
                    fieldKind,
                    hierarchyPath,
                    objectName,
                    propertyPath,
                    term,
                    match = row.Value<string>("match"),
                    index = row.Value<int>("index"),
                    excerpt = row.Value<string>("excerpt")
                }, term, assetKind, fieldKind);
            }
        }

        static object[] BuildFindings(string value, Request request, TermMatcher[] matchers)
        {
            if (string.IsNullOrEmpty(value))
                return Array.Empty<object>();

            var rows = new List<object>();
            foreach (TermMatcher matcher in matchers)
            {
                foreach (Match match in matcher.Regex.Matches(value))
                {
                    if (!match.Success)
                        continue;

                    rows.Add(new
                    {
                        term = matcher.Term,
                        match = match.Value,
                        index = match.Index,
                        excerpt = BuildExcerpt(value, match.Index, match.Length, request.ContextChars)
                    });
                }
            }

            return rows.ToArray();
        }

        static TermMatcher[] BuildMatchers(Request request)
        {
            RegexOptions options = RegexOptions.CultureInvariant;
            if (!request.CaseSensitive)
                options |= RegexOptions.IgnoreCase;

            return request.BlockedTerms.Select(term =>
            {
                string pattern;
                if (request.MatchMode.Equals("regex", StringComparison.Ordinal))
                {
                    pattern = term;
                }
                else
                {
                    pattern = Regex.Escape(term);
                    if (request.WholeWord)
                        pattern = $@"(?<![\p{{L}}\p{{N}}_]){pattern}(?![\p{{L}}\p{{N}}_])";
                }

                return new TermMatcher
                {
                    Term = term,
                    Regex = new Regex(pattern, options, TimeSpan.FromMilliseconds(200))
                };
            }).ToArray();
        }

        static string[] ResolveTerms(Request request)
        {
            var terms = new List<string>();
            if (request.BlockedTerms != null)
                terms.AddRange(request.BlockedTerms);

            if (!string.IsNullOrWhiteSpace(request.TermsAssetPath))
            {
                string fullPath = ToFullPath(request.TermsAssetPath);
                if (!File.Exists(fullPath))
                    throw new InvalidOperationException($"termsAssetPath '{request.TermsAssetPath}' could not be read.");

                terms.AddRange(File.ReadLines(fullPath)
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#", StringComparison.Ordinal)));
            }

            return terms.ToArray();
        }

        static string[] ResolveAssetPaths(Request request)
        {
            var paths = new List<string>();
            paths.AddRange(request.AssetPaths ?? Array.Empty<string>());
            var roots = new List<string>();
            if (!string.IsNullOrWhiteSpace(request.Under))
                roots.Add(request.Under);
            if (request.IncludePackages && !roots.Any(root => root.StartsWith("Packages", StringComparison.OrdinalIgnoreCase)))
                roots.Add("Packages");

            foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (AssetDatabase.IsValidFolder(root))
                {
                    paths.AddRange(AssetDatabase.FindAssets(string.Empty, new[] { root }).Select(AssetDatabase.GUIDToAssetPath));
                }
                else if (File.Exists(ToFullPath(root)))
                {
                    paths.Add(root);
                }
            }

            return paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Replace('\\', '/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => ShouldScanKind(ClassifyAssetKind(path), request))
                .ToArray();
        }

        static bool ShouldScanKind(string kind, Request request)
        {
            return kind switch
            {
                "script" => request.IncludeScripts,
                "scene_asset" => request.IncludeScenes,
                "prefab" => request.IncludePrefabs,
                "scriptable_object" => request.IncludeScriptableObjects,
                "text_asset" => request.IncludeTextAssets,
                _ => false
            };
        }

        static string ClassifyAssetKind(string assetPath)
        {
            string extension = Path.GetExtension(assetPath)?.ToLowerInvariant() ?? string.Empty;
            if (extension == ".cs" || extension == ".asmdef" || extension == ".asmref")
                return "script";
            if (extension == ".unity")
                return "scene_asset";
            if (extension == ".prefab")
                return "prefab";
            if (extension == ".asset")
            {
                Type type = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
                return type != null && typeof(ScriptableObject).IsAssignableFrom(type) ? "scriptable_object" : "text_asset";
            }

            if (IsTextAssetExtension(extension))
                return "text_asset";

            Type mainType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
            return mainType != null && typeof(ScriptableObject).IsAssignableFrom(mainType) ? "scriptable_object" : "unsupported";
        }

        static bool IsTextAssetExtension(string extension)
        {
            return extension is ".txt" or ".json" or ".xml" or ".csv" or ".tsv" or ".md" or ".uxml" or ".uss" or ".shader" or ".hlsl" or ".cginc" or ".compute" or ".inputactions" or ".yaml" or ".yml" or ".mat";
        }

        static bool IsLikelyUiTextProperty(UnityEngine.Object target, string propertyPath)
        {
            string typeName = target.GetType().FullName ?? target.GetType().Name;
            return typeName.IndexOf("UnityEngine.UI.Text", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("TextMesh", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("TMP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(propertyPath, "m_Text", StringComparison.Ordinal) ||
                string.Equals(propertyPath, "m_text", StringComparison.Ordinal);
        }

        static string BuildHierarchyPath(Transform transform)
        {
            if (transform == null)
                return string.Empty;

            var names = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names);
        }

        static string BuildExcerpt(string value, int index, int length, int contextChars)
        {
            int radius = Math.Max(0, contextChars);
            int start = Math.Max(0, index - radius);
            int end = Math.Min(value.Length, index + Math.Max(1, length) + radius);
            string prefix = start > 0 ? "..." : string.Empty;
            string suffix = end < value.Length ? "..." : string.Empty;
            return prefix + value.Substring(start, end - start).Replace("\r", "\\r").Replace("\n", "\\n") + suffix;
        }

        static object[] ToCountRows(Dictionary<string, int> counts)
        {
            return counts
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new { value = pair.Key, count = pair.Value })
                .ToArray();
        }

        static string NormalizeProjectAssetPath(string path, string defaultPath)
        {
            if (string.IsNullOrWhiteSpace(path))
                return defaultPath;

            string normalized = path.Replace('\\', '/').Trim();
            if (Path.IsPathRooted(normalized))
                return normalized.TrimEnd('/');

            if (normalized.StartsWith("Assets", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Packages", StringComparison.OrdinalIgnoreCase))
                return normalized.TrimEnd('/');

            return "Assets/" + normalized.TrimStart('/');
        }

        static string ToFullPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;

            string normalized = assetPath.Replace('\\', '/');
            if (Path.IsPathRooted(normalized))
                return normalized;

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            return string.IsNullOrWhiteSpace(projectRoot) ? null : Path.GetFullPath(Path.Combine(projectRoot, normalized));
        }

        static JToken GetToken(JObject obj, params string[] names)
        {
            if (obj == null)
                return null;

            foreach (string name in names)
            {
                if (obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token))
                    return token;
            }

            return null;
        }

        static string GetString(JObject obj, params string[] names)
        {
            JToken token = GetToken(obj, names);
            return token == null || token.Type == JTokenType.Null ? null : token.ToString();
        }

        static string[] GetStringArray(JObject obj, params string[] names)
        {
            JToken token = GetToken(obj, names);
            if (token is JArray array)
            {
                return array
                    .Select(item => item?.ToString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();
            }

            if (token != null && token.Type != JTokenType.Null)
                return new[] { token.ToString() };

            return Array.Empty<string>();
        }

        static bool GetBool(JObject obj, bool fallback, params string[] names)
        {
            JToken token = GetToken(obj, names);
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<bool>();
        }

        static int GetInt(JObject obj, int fallback, params string[] names)
        {
            JToken token = GetToken(obj, names);
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<int>();
        }

        static object BuildCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            JArray findings = root["findings"] as JArray ?? new JArray();
            return new
            {
                status = root["status"],
                blockedTermCount = root["blockedTermCount"],
                matchMode = root["matchMode"],
                caseSensitive = root["caseSensitive"],
                wholeWord = root["wholeWord"],
                under = root["under"],
                explicitAssetPathCount = root["explicitAssetPathCount"],
                candidateAssetCount = root["candidateAssetCount"],
                scannedAssetCount = root["scannedAssetCount"],
                scannedSourceCounts = root["scannedSourceCounts"],
                findingCount = root["findingCount"],
                storedFindingCount = root["storedFindingCount"],
                truncated = root["truncated"],
                contextChars = root["contextChars"],
                termCounts = root["termCounts"],
                assetKindCounts = root["assetKindCounts"],
                fieldKindCounts = root["fieldKindCounts"],
                warnings = root["warnings"],
                findings = findings.Take(30).ToArray(),
                compactOmittedFindingCount = Math.Max(0, findings.Count - 30),
                importSideEffects = root["importSideEffects"]
            };
        }
    }
}
