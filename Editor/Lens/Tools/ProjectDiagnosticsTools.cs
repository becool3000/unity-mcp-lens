using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.Adapters.Unity;
using Becool.UnityMcpLens.Editor.Utils;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Tools.Parameters;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class ProjectDiagnosticsTools
    {
        const string ScanMissingScriptsToolName = "Unity.Project.ScanMissingScripts";
        const string ValidateReferencesToolName = "Unity.Object.ValidateReferences";
        const string DiagnoseImportSideEffectsToolName = "Unity.Project.DiagnoseImportSideEffects";

        public const string ScanMissingScriptsDescription = @"Scans open scenes and prefab assets for missing MonoBehaviour script references.

Args:
    Under: Folder under Assets to scan for prefabs. Defaults to Assets.
    IncludeOpenScenes: Scan currently open scenes.
    IncludePrefabs: Scan prefab assets on disk.
    MaxPrefabs: Maximum number of prefab assets to inspect.

Returns:
    Dictionary with success/message/data. Data contains scene findings, prefab findings, and counts.";

        public const string ValidateReferencesDescription = @"Audits serialized object-reference fields on a GameObject, component, or asset.

Args:
    Target: Target GameObject/path, instance id string, or asset path.
    SearchMethod: How to find a scene object target ('by_name', 'by_id', 'by_path').
    ComponentName: Optional component type name to narrow the audit.
    IncludeInactive: Include inactive scene objects when resolving the target.

Returns:
    Dictionary with success/message/data. Data contains null and missing object-reference fields without project-specific interpretation.";

        public const string DiagnoseImportSideEffectsDescription = @"Inspects asset importer and dependency signals that can explain refresh/reimport side effects without mutating assets.

Args:
    Under: Folder under Assets or Packages to inspect. Defaults to Assets.
    Filter: Optional AssetDatabase.FindAssets filter such as 't:Texture2D', 't:Prefab', or empty for all assets.
    AssetPaths: Explicit asset paths to inspect in addition to filter results.
    MaxAssets: Maximum number of candidate assets to inspect.
    MaxFindings: Maximum number of notable findings to return.
    IncludeDependencies: Include direct dependency counts and script dependency signals.

Returns:
    Dictionary with success/message/data. Data contains importer type counts, risky importer/dependency findings, and read-only import side-effect flags.";

        [McpTool(ScanMissingScriptsToolName, ScanMissingScriptsDescription, Groups = new[] { "diagnostics", "project" }, EnabledByDefault = true)]
        public static object ScanMissingScripts(ScanMissingScriptsParams parameters)
        {
            parameters ??= new ScanMissingScriptsParams();
            var timing = new ToolOperationTiming(ScanMissingScriptsToolName, "scan_missing_scripts", GetUtf8ByteCount(JsonConvert.SerializeObject(parameters, Formatting.None)));
            object response;
            bool success = false;
            string errorKind = null;

            try
            {
                ScanMissingScriptsParams request;
                using (timing.Measure("normalization"))
                {
                    request = NormalizeScanMissingScriptsParams(parameters);
                }

                var sceneFindings = new List<object>();
                var prefabFindings = new List<object>();
                int scannedSceneCount = 0;
                int scannedPrefabCount = 0;
                int candidatePrefabCount = 0;

                using (timing.Measure("adapter"))
                {
                    if (request.IncludeOpenScenes)
                    {
                        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
                        {
                            Scene scene = SceneManager.GetSceneAt(sceneIndex);
                            if (!scene.IsValid() || !scene.isLoaded)
                                continue;

                            scannedSceneCount++;
                            foreach (GameObject root in scene.GetRootGameObjects())
                            {
                                CollectMissingScripts(root.transform, scene.path, sceneFindings, request.MaxFindings);
                                if (sceneFindings.Count + prefabFindings.Count >= request.MaxFindings)
                                    break;
                            }
                        }
                    }

                    if (request.IncludePrefabs)
                    {
                        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { request.Under });
                        candidatePrefabCount = prefabGuids.Length;
                        foreach (string guid in prefabGuids.Take(request.MaxPrefabs))
                        {
                            if (sceneFindings.Count + prefabFindings.Count >= request.MaxFindings)
                                break;

                            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);
                            scannedPrefabCount++;
                            try
                            {
                                CollectMissingScripts(prefabRoot.transform, assetPath, prefabFindings, request.MaxFindings - sceneFindings.Count);
                            }
                            finally
                            {
                                PrefabUtility.UnloadPrefabContents(prefabRoot);
                            }
                        }
                    }
                }

                object payload;
                using (timing.Measure("service"))
                {
                    payload = new
                    {
                        sceneFindingCount = sceneFindings.Count,
                        prefabFindingCount = prefabFindings.Count,
                        findingCount = sceneFindings.Count + prefabFindings.Count,
                        truncated = sceneFindings.Count + prefabFindings.Count >= request.MaxFindings,
                        scannedSceneCount,
                        candidatePrefabCount,
                        scannedPrefabCount,
                        maxFindings = request.MaxFindings,
                        sceneFindings,
                        prefabFindings,
                        importSideEffects = new
                        {
                            mutatesAssets = false,
                            usesAssetDatabaseFindAssets = request.IncludePrefabs,
                            loadsPrefabContentsReadOnly = request.IncludePrefabs,
                            requestsImport = false
                        }
                    };
                }

                string summary = $"Missing-script scan completed with {sceneFindings.Count + prefabFindings.Count} finding(s).";
                using (timing.Measure("result_shaping"))
                {
                    response = Response.Success(
                        "Missing-script scan completed.",
                        ShapePayload(
                            ScanMissingScriptsToolName,
                            summary,
                            payload,
                            BuildMissingScriptsCompactData(payload),
                            new
                            {
                                kind = "project_missing_scripts_full_result",
                                tool = ScanMissingScriptsToolName,
                                args = new
                                {
                                    request.Under,
                                    request.IncludeOpenScenes,
                                    request.IncludePrefabs,
                                    request.MaxPrefabs,
                                    request.MaxFindings
                                }
                            }));
                    timing.SetResponseBytes(GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
                }

                success = true;
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                using (timing.Measure("result_shaping"))
                {
                    response = Response.Error($"Missing-script scan failed: {ex.Message}", new { errorKind });
                    timing.SetResponseBytes(GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
                }
            }

            timing.Record(success, success ? null : errorKind);
            return response;
        }

        [McpTool(ValidateReferencesToolName, ValidateReferencesDescription, Groups = new[] { "diagnostics", "project" }, EnabledByDefault = true)]
        public static object ValidateReferences(ValidateReferencesParams parameters)
        {
            parameters ??= new ValidateReferencesParams();
            var timing = new ToolOperationTiming(ValidateReferencesToolName, "validate_references", GetUtf8ByteCount(JsonConvert.SerializeObject(parameters, Formatting.None)));
            object response;
            bool success = false;
            string errorKind = null;

            try
            {
                ValidateReferencesParams request;
                using (timing.Measure("normalization"))
                {
                    request = NormalizeValidateReferencesParams(parameters);
                }

                if (string.IsNullOrWhiteSpace(request.Target))
                    throw new ArgumentException("Target is required.", nameof(parameters.Target));

                UnityEngine.Object targetObject;
                string targetLabel;
                string resolveError;
                List<UnityEngine.Object> auditTargets;
                var findings = new List<object>();

                using (timing.Measure("adapter"))
                {
                    if (!TryResolveValidationTarget(request, out targetObject, out targetLabel, out resolveError))
                        throw new InvalidOperationException(resolveError ?? "Target could not be resolved.");

                    auditTargets = ResolveAuditTargets(targetObject, request.ComponentName);
                    if (auditTargets.Count == 0)
                        throw new InvalidOperationException("No matching objects or components were found to validate.");

                    foreach (UnityEngine.Object auditTarget in auditTargets)
                    {
                        SerializedObject serializedObject = new(auditTarget);
                        SerializedProperty iterator = serializedObject.GetIterator();
                        bool enterChildren = true;
                        while (iterator.NextVisible(enterChildren))
                        {
                            enterChildren = false;
                            if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                                continue;

                            bool missingReference = iterator.objectReferenceValue == null && UnityApiAdapter.HasObjectReferenceId(iterator);
                            bool nullReference = iterator.objectReferenceValue == null;
                            if (!missingReference && !nullReference)
                                continue;

                            findings.Add(new
                            {
                                objectName = auditTarget.name,
                                objectType = auditTarget.GetType().FullName,
                                propertyPath = iterator.propertyPath,
                                displayName = iterator.displayName,
                                isMissingReference = missingReference,
                                isNullReference = nullReference,
                                instanceID = UnityApiAdapter.GetObjectReferenceId(iterator)
                            });

                            if (findings.Count >= request.MaxFindings)
                                break;
                        }

                        if (findings.Count >= request.MaxFindings)
                            break;
                    }
                }

                object payload;
                using (timing.Measure("service"))
                {
                    payload = new
                    {
                        target = targetLabel,
                        inspectedObjectCount = auditTargets.Count,
                        findingCount = findings.Count,
                        missingReferenceCount = findings.Count(finding => JObject.FromObject(finding).Value<bool>("isMissingReference")),
                        nullReferenceCount = findings.Count(finding => JObject.FromObject(finding).Value<bool>("isNullReference")),
                        truncated = findings.Count >= request.MaxFindings,
                        maxFindings = request.MaxFindings,
                        findings
                    };
                }

                string summary = $"Validated {auditTargets.Count} object(s) and found {findings.Count} reference issue(s).";
                using (timing.Measure("result_shaping"))
                {
                    response = Response.Success(
                        $"Validated {auditTargets.Count} object(s).",
                        ShapePayload(
                            ValidateReferencesToolName,
                            summary,
                            payload,
                            BuildValidateReferencesCompactData(payload),
                            new
                            {
                                kind = "project_validate_references_full_result",
                                tool = ValidateReferencesToolName,
                                args = new
                                {
                                    request.Target,
                                    request.SearchMethod,
                                    request.ComponentName,
                                    request.IncludeInactive,
                                    request.MaxFindings
                                }
                            }));
                    timing.SetResponseBytes(GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
                }

                success = true;
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                using (timing.Measure("result_shaping"))
                {
                    response = Response.Error($"Reference validation failed: {ex.Message}", new { errorKind });
                    timing.SetResponseBytes(GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
                }
            }

            timing.Record(success, success ? null : errorKind);
            return response;
        }

        [McpTool(DiagnoseImportSideEffectsToolName, DiagnoseImportSideEffectsDescription, Groups = new[] { "diagnostics", "project" }, EnabledByDefault = true)]
        public static object DiagnoseImportSideEffects(DiagnoseImportSideEffectsParams parameters)
        {
            parameters ??= new DiagnoseImportSideEffectsParams();
            var timing = new ToolOperationTiming(DiagnoseImportSideEffectsToolName, "diagnose_import_side_effects", GetUtf8ByteCount(JsonConvert.SerializeObject(parameters, Formatting.None)));
            object response;
            bool success = false;
            string errorKind = null;

            try
            {
                DiagnoseImportSideEffectsParams request;
                using (timing.Measure("normalization"))
                {
                    request = NormalizeDiagnoseImportSideEffectsParams(parameters);
                }

                string[] assetPaths;
                var rows = new List<object>();
                var findings = new List<object>();

                using (timing.Measure("adapter"))
                {
                    assetPaths = ResolveImportDiagnosticAssetPaths(request);
                    foreach (string assetPath in assetPaths)
                    {
                        object row = BuildImportDiagnosticRow(assetPath, request.IncludeDependencies, findings, request.MaxFindings);
                        if (row != null)
                            rows.Add(row);
                    }
                }

                object payload;
                using (timing.Measure("service"))
                {
                    payload = new
                    {
                        under = request.Under,
                        filter = request.Filter,
                        requestedAssetPathCount = request.AssetPaths?.Length ?? 0,
                        candidateAssetCount = assetPaths.Length,
                        inspectedAssetCount = rows.Count,
                        findingCount = findings.Count,
                        truncated = findings.Count >= request.MaxFindings || rows.Count >= request.MaxAssets,
                        maxAssets = request.MaxAssets,
                        maxFindings = request.MaxFindings,
                        importerTypeCounts = CountByString(rows, "importerType"),
                        assetTypeCounts = CountByString(rows, "mainAssetType"),
                        findings,
                        assets = rows,
                        importSideEffects = new
                        {
                            mutatesAssets = false,
                            usesAssetDatabaseFindAssets = true,
                            readsImporters = true,
                            readsDependencies = request.IncludeDependencies,
                            requestsImport = false,
                            callsRefresh = false
                        }
                    };
                }

                string summary = $"Inspected {rows.Count} asset import state row(s) and found {findings.Count} notable import side-effect signal(s).";
                using (timing.Measure("result_shaping"))
                {
                    response = Response.Success(
                        "Import side-effect diagnosis completed.",
                        ShapePayload(
                            DiagnoseImportSideEffectsToolName,
                            summary,
                            payload,
                            BuildImportSideEffectsCompactData(payload),
                            new
                            {
                                kind = "project_import_side_effects_full_result",
                                tool = DiagnoseImportSideEffectsToolName,
                                args = new
                                {
                                    request.Under,
                                    request.Filter,
                                    request.MaxAssets,
                                    request.MaxFindings,
                                    request.IncludeDependencies
                                }
                            }));
                    timing.SetResponseBytes(GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
                }

                success = true;
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                using (timing.Measure("result_shaping"))
                {
                    response = Response.Error($"Import side-effect diagnosis failed: {ex.Message}", new { errorKind });
                    timing.SetResponseBytes(GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
                }
            }

            timing.Record(success, success ? null : errorKind);
            return response;
        }

        static ScanMissingScriptsParams NormalizeScanMissingScriptsParams(ScanMissingScriptsParams parameters)
        {
            parameters ??= new ScanMissingScriptsParams();
            return new ScanMissingScriptsParams
            {
                Under = NormalizeAssetsPath(parameters.Under),
                IncludeOpenScenes = parameters.IncludeOpenScenes,
                IncludePrefabs = parameters.IncludePrefabs,
                MaxPrefabs = Math.Max(1, parameters.MaxPrefabs),
                MaxFindings = Math.Clamp(parameters.MaxFindings <= 0 ? PayloadBudgetPolicy.MaxDiagnosticFindings : parameters.MaxFindings, 1, PayloadBudgetPolicy.MaxDiagnosticFindings)
            };
        }

        static ValidateReferencesParams NormalizeValidateReferencesParams(ValidateReferencesParams parameters)
        {
            parameters ??= new ValidateReferencesParams();
            return new ValidateReferencesParams
            {
                Target = parameters.Target?.Trim(),
                SearchMethod = string.IsNullOrWhiteSpace(parameters.SearchMethod) ? "by_name" : parameters.SearchMethod.Trim(),
                ComponentName = parameters.ComponentName?.Trim(),
                IncludeInactive = parameters.IncludeInactive,
                MaxFindings = Math.Clamp(parameters.MaxFindings <= 0 ? PayloadBudgetPolicy.MaxDiagnosticFindings : parameters.MaxFindings, 1, PayloadBudgetPolicy.MaxDiagnosticFindings)
            };
        }

        static DiagnoseImportSideEffectsParams NormalizeDiagnoseImportSideEffectsParams(DiagnoseImportSideEffectsParams parameters)
        {
            parameters ??= new DiagnoseImportSideEffectsParams();
            return new DiagnoseImportSideEffectsParams
            {
                Under = NormalizeProjectAssetPath(parameters.Under),
                Filter = parameters.Filter?.Trim(),
                AssetPaths = parameters.AssetPaths?.Where(path => !string.IsNullOrWhiteSpace(path)).Select(NormalizeProjectAssetPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>(),
                MaxAssets = Math.Clamp(parameters.MaxAssets <= 0 ? 100 : parameters.MaxAssets, 1, 500),
                MaxFindings = Math.Clamp(parameters.MaxFindings <= 0 ? PayloadBudgetPolicy.MaxDiagnosticFindings : parameters.MaxFindings, 1, PayloadBudgetPolicy.MaxDiagnosticFindings),
                IncludeDependencies = parameters.IncludeDependencies
            };
        }

        static string NormalizeAssetsPath(string path)
        {
            string normalized = string.IsNullOrWhiteSpace(path) ? "Assets" : path.Replace('\\', '/').Trim();
            return normalized.StartsWith("Assets", StringComparison.OrdinalIgnoreCase) ? normalized : "Assets";
        }

        static string NormalizeProjectAssetPath(string path)
        {
            string normalized = string.IsNullOrWhiteSpace(path) ? "Assets" : path.Replace('\\', '/').Trim();
            if (normalized.StartsWith("Assets", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Packages", StringComparison.OrdinalIgnoreCase))
            {
                return normalized.TrimEnd('/');
            }

            return "Assets/" + normalized.TrimStart('/');
        }

        static object BuildMissingScriptsCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            JArray sceneFindings = root["sceneFindings"] as JArray ?? new JArray();
            JArray prefabFindings = root["prefabFindings"] as JArray ?? new JArray();
            return new
            {
                sceneFindingCount = root["sceneFindingCount"],
                prefabFindingCount = root["prefabFindingCount"],
                findingCount = root["findingCount"],
                truncated = root["truncated"],
                scannedSceneCount = root["scannedSceneCount"],
                candidatePrefabCount = root["candidatePrefabCount"],
                scannedPrefabCount = root["scannedPrefabCount"],
                sceneFindings = new JArray(sceneFindings.Take(8).Select(row => row.DeepClone())),
                prefabFindings = new JArray(prefabFindings.Take(8).Select(row => row.DeepClone())),
                importSideEffects = root["importSideEffects"]
            };
        }

        static object BuildValidateReferencesCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            JArray findings = root["findings"] as JArray ?? new JArray();
            return new
            {
                target = root["target"],
                inspectedObjectCount = root["inspectedObjectCount"],
                findingCount = root["findingCount"],
                missingReferenceCount = root["missingReferenceCount"],
                nullReferenceCount = root["nullReferenceCount"],
                truncated = root["truncated"],
                findings = new JArray(findings.Take(8).Select(row => row.DeepClone()))
            };
        }

        static object BuildImportSideEffectsCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            JArray findings = root["findings"] as JArray ?? new JArray();
            JArray assets = root["assets"] as JArray ?? new JArray();
            return new
            {
                under = root["under"],
                filter = root["filter"],
                candidateAssetCount = root["candidateAssetCount"],
                inspectedAssetCount = root["inspectedAssetCount"],
                findingCount = root["findingCount"],
                truncated = root["truncated"],
                importerTypeCounts = root["importerTypeCounts"],
                assetTypeCounts = root["assetTypeCounts"],
                findings = new JArray(findings.Take(8).Select(row => row.DeepClone())),
                assets = new JArray(assets.Take(8).Select(row => row.DeepClone())),
                importSideEffects = root["importSideEffects"]
            };
        }

        static string[] ResolveImportDiagnosticAssetPaths(DiagnoseImportSideEffectsParams request)
        {
            var paths = new List<string>();
            if (request.AssetPaths != null)
            {
                paths.AddRange(request.AssetPaths);
            }

            string root = string.IsNullOrWhiteSpace(request.Under) ? "Assets" : request.Under;
            if (AssetDatabase.IsValidFolder(root))
            {
                string filter = string.IsNullOrWhiteSpace(request.Filter) ? string.Empty : request.Filter;
                string[] guids = AssetDatabase.FindAssets(filter, new[] { root });
                paths.AddRange(guids.Select(AssetDatabase.GUIDToAssetPath));
            }
            else if (!paths.Contains(root, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(root);
            }

            return paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Replace('\\', '/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => AssetDatabase.LoadMainAssetAtPath(path) != null || AssetImporter.GetAtPath(path) != null)
                .Take(request.MaxAssets)
                .ToArray();
        }

        static object BuildImportDiagnosticRow(string assetPath, bool includeDependencies, List<object> findings, int maxFindings)
        {
            AssetImporter importer = AssetImporter.GetAtPath(assetPath);
            Type mainAssetType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
            UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (importer == null && mainAsset == null)
            {
                return null;
            }

            string importerType = importer?.GetType().Name ?? "None";
            string mainAssetTypeName = mainAssetType?.FullName ?? mainAsset?.GetType().FullName;
            string[] dependencies = includeDependencies ? AssetDatabase.GetDependencies(assetPath, recursive: false) ?? Array.Empty<string>() : Array.Empty<string>();
            int scriptDependencyCount = includeDependencies
                ? dependencies.Count(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".asmref", StringComparison.OrdinalIgnoreCase))
                : 0;
            int packageDependencyCount = includeDependencies
                ? dependencies.Count(path => path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                : 0;
            bool sourceControlledImporter = importer is TextureImporter or ModelImporter or AudioImporter or MonoImporter or AssetImporter;
            bool likelyScriptReload = assetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                assetPath.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase) ||
                assetPath.EndsWith(".asmref", StringComparison.OrdinalIgnoreCase) ||
                scriptDependencyCount > 0;
            bool likelyReimportCascade = includeDependencies && dependencies.Length > 12;
            string severity = likelyScriptReload ? "warning" : likelyReimportCascade ? "info" : "none";

            if (findings.Count < maxFindings && (likelyScriptReload || likelyReimportCascade || packageDependencyCount > 0))
            {
                findings.Add(new
                {
                    assetPath,
                    severity,
                    reason = likelyScriptReload
                        ? "script_or_assembly_dependency"
                        : likelyReimportCascade
                            ? "many_direct_dependencies"
                            : "package_dependency",
                    importerType,
                    mainAssetType = mainAssetTypeName,
                    directDependencyCount = dependencies.Length,
                    scriptDependencyCount,
                    packageDependencyCount,
                    likelyScriptReload,
                    likelyReimportCascade
                });
            }

            return new
            {
                assetPath,
                guid = AssetDatabase.AssetPathToGUID(assetPath),
                importerType,
                mainAssetType = mainAssetTypeName,
                assetBundleName = importer?.assetBundleName,
                userDataPresent = !string.IsNullOrEmpty(importer?.userData),
                directDependencyCount = includeDependencies ? dependencies.Length : (int?)null,
                scriptDependencyCount = includeDependencies ? scriptDependencyCount : (int?)null,
                packageDependencyCount = includeDependencies ? packageDependencyCount : (int?)null,
                likelyScriptReload,
                likelyReimportCascade,
                sourceControlledImporter,
                severity
            };
        }

        static object CountByString(List<object> rows, string propertyName)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (object row in rows)
            {
                string value = JObject.FromObject(row).Value<string>(propertyName);
                if (string.IsNullOrWhiteSpace(value))
                    value = "(none)";

                counts[value] = counts.TryGetValue(value, out int count) ? count + 1 : 1;
            }

            return counts
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new { value = pair.Key, count = pair.Value })
                .ToArray();
        }

        static object ShapePayload(string toolName, string summary, object data, object compactData, object detailRefMeta)
        {
            return ToolResultCompactor.ShapeStructuredPayload(
                toolName,
                data,
                compactData,
                detailRefMeta: detailRefMeta,
                payloadClass: "project_diagnostics",
                detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes);
        }

        static int GetUtf8ByteCount(string text)
        {
            return PayloadBudgeting.GetUtf8ByteCount(text ?? string.Empty);
        }

        static void CollectMissingScripts(Transform transform, string ownerPath, List<object> findings, int remainingBudget)
        {
            if (transform == null || remainingBudget <= 0)
            {
                return;
            }

            int missingCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
            if (missingCount > 0)
            {
                findings.Add(new
                {
                    ownerPath,
                    hierarchyPath = UiDiagnosticsHelper.GetHierarchyPath(transform),
                    missingScriptCount = missingCount
                });
            }

            for (int i = 0; i < transform.childCount; i++)
            {
                if (findings.Count >= remainingBudget)
                    break;

                CollectMissingScripts(transform.GetChild(i), ownerPath, findings, remainingBudget);
            }
        }

        static bool TryResolveValidationTarget(ValidateReferencesParams parameters, out UnityEngine.Object targetObject, out string targetLabel, out string error)
        {
            targetObject = null;
            targetLabel = parameters.Target;
            error = null;

            string assetPath = parameters.Target.Replace('\\', '/');
            if (assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                targetObject = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (targetObject != null)
                {
                    targetLabel = assetPath;
                    return true;
                }

                error = $"Asset target '{assetPath}' could not be loaded.";
                return false;
            }

            var findParams = new JObject
            {
                ["search_inactive"] = parameters.IncludeInactive
            };
            GameObject targetGo = ObjectsHelper.FindObject(parameters.Target, parameters.SearchMethod, findParams);
            if (targetGo == null)
            {
                error = $"Target '{parameters.Target}' could not be resolved.";
                return false;
            }

            targetObject = targetGo;
            targetLabel = UiDiagnosticsHelper.GetHierarchyPath(targetGo.transform);
            return true;
        }

        static List<UnityEngine.Object> ResolveAuditTargets(UnityEngine.Object targetObject, string componentName)
        {
            var results = new List<UnityEngine.Object>();
            if (targetObject == null)
            {
                return results;
            }

            if (targetObject is GameObject go)
            {
                if (string.IsNullOrWhiteSpace(componentName))
                {
                    results.AddRange(go.GetComponents<Component>().Where(component => component != null).Cast<UnityEngine.Object>());
                    return results;
                }

                if (UnityComponentResolver.TryResolve(componentName, out Type componentType, out _))
                {
                    Component resolved = go.GetComponent(componentType);
                    if (resolved != null)
                    {
                        results.Add(resolved);
                    }
                }
                else
                {
                    results.AddRange(go.GetComponents<Component>()
                        .Where(component => component != null &&
                                            (component.GetType().Name.Equals(componentName, StringComparison.OrdinalIgnoreCase) ||
                                             component.GetType().FullName.Equals(componentName, StringComparison.OrdinalIgnoreCase)))
                        .Cast<UnityEngine.Object>());
                }

                return results;
            }

            results.Add(targetObject);
            return results;
        }

    }
}
