#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils;
using Becool.UnityMcpLens.Editor.Utils.Scene;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class PrefabSerializedReferenceAuditTools
    {
        const string ToolName = "Unity.Prefab.AuditSerializedReferences";
        const int DefaultMaxPrefabs = 50;
        const int DefaultMaxFindings = 200;
        const int MaxPrefabLimit = 500;
        const int MaxFindingLimit = 2000;

        static readonly string[] k_LikelyRequiredReferenceTerms =
        {
            "sprite",
            "material",
            "texture",
            "prefab",
            "clip",
            "font",
            "targetgraphic",
            "graphic",
            "icon",
            "avatar",
            "mesh",
            "atlas",
            "audio",
            "controller",
            "template",
            "image",
            "renderer"
        };

        sealed class Request
        {
            public string PrefabPath;
            public string[] PrefabPaths = Array.Empty<string>();
            public string Under = "Assets";
            public string NameFilter;
            public int MaxPrefabs = DefaultMaxPrefabs;
            public int MaxFindings = DefaultMaxFindings;
            public string ReferenceNullPolicy = "likely_required";
            public bool IncludeNestedPrefabInstances = true;
            public bool IncludeRuntimeLoadPatterns = true;
        }

        sealed class FindingRow
        {
            public int index;
            public string severity;
            public string kind;
            public string message;
            public string prefabPath;
            public string prefabGuid;
            public string hierarchyPath;
            public string componentType;
            public int? componentIndex;
            public string propertyPath;
            public string propertyDisplayName;
            public int? objectReferenceInstanceId;
            public string scriptPath;
            public int? lineNumber;
            public string linePreview;
            public string nestedPrefabStatus;
            public string nestedPrefabAssetPath;
        }

        sealed class PrefabSummary
        {
            public string prefabPath;
            public string guid;
            public bool dirtyBefore;
            public bool dirtyAfter;
            public int objectCount;
            public int componentCount;
            public int missingScriptCount;
            public int findingCount;
            public Dictionary<string, int> severityCounts = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, int> kindCounts = new(StringComparer.OrdinalIgnoreCase);
        }

        sealed class AuditContext
        {
            public Request Request;
            public List<FindingRow> Findings = new();
            public List<PrefabSummary> Summaries = new();
            public Dictionary<string, string[]> ScriptPatternCache = new(StringComparer.OrdinalIgnoreCase);
            public int TotalFindingCount;
            public bool Truncated;
        }

        [McpSchema(ToolName)]
        public static object GetSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    prefabPath = new { type = "string", description = "Optional single prefab asset path under Assets/." },
                    prefabPaths = new { type = "array", description = "Optional prefab asset paths under Assets/.", items = new { type = "string" } },
                    under = new { type = "string", description = "Folder scan root used when no explicit prefab path is supplied. Defaults to Assets." },
                    nameFilter = new { type = "string", description = "Optional prefab filename substring filter for folder scans." },
                    maxPrefabs = new { type = "integer", description = "Maximum prefab assets to scan. Defaults to 50 and is clamped to 1..500." },
                    maxFindings = new { type = "integer", description = "Maximum finding rows to keep inline/full-result payload. Defaults to 200 and is clamped to 1..2000." },
                    referenceNullPolicy = new { type = "string", description = "Null object-reference reporting policy. Defaults to likely_required.", @enum = new[] { "broken_only", "likely_required", "all" } },
                    includeNestedPrefabInstances = new { type = "boolean", description = "Report missing/disconnected nested prefab instance status. Defaults to true." },
                    includeRuntimeLoadPatterns = new { type = "boolean", description = "Report component scripts that use runtime asset-load patterns. Defaults to true." }
                }
            };
        }

        [McpTool(ToolName,
            "Audits prefab assets for missing scripts, broken serialized references, unassigned UI/visual assets, runtime asset-load patterns, and broken nested prefab instances without saving or mutating assets.",
            "Audit Prefab Serialized References",
            Groups = new[] { "assets" },
            EnabledByDefault = true)]
        public static object AuditSerializedReferences(JObject @params)
        {
            @params ??= new JObject();
            var timing = new ToolOperationTiming(ToolName, "prefab_audit_serialized_references", PayloadBudgeting.GetUtf8ByteCount(@params.ToString(Formatting.None)));
            bool success = false;
            string errorKind = null;
            object data = null;
            string message = null;

            try
            {
                Request request;
                string[] prefabPaths;
                using (timing.Measure("normalization"))
                {
                    request = Normalize(@params);
                    if (!TryResolvePrefabPaths(request, out prefabPaths, out object errorData, out string errorMessage))
                    {
                        errorKind = "INVALID_PREFAB_PATH";
                        data = errorData;
                        message = errorMessage;
                        return Response.Error(errorMessage, errorData);
                    }
                }

                using (timing.Measure("service"))
                {
                    data = Execute(request, prefabPaths);
                    var shaped = JObject.FromObject(data);
                    int findingCount = shaped.Value<int?>("findingCount") ?? 0;
                    int scannedCount = shaped.Value<int?>("scannedPrefabCount") ?? 0;
                    success = true;
                    message = findingCount == 0
                        ? $"Prefab serialized-reference audit passed for {scannedCount} prefab(s)."
                        : $"Prefab serialized-reference audit found {findingCount} issue(s) across {scannedCount} prefab(s).";
                }
            }
            catch (Exception ex)
            {
                success = false;
                errorKind = ex.GetType().Name;
                message = $"Prefab serialized-reference audit failed: {ex.Message}";
                data = new
                {
                    status = "failed",
                    errorKind,
                    error = ex.Message,
                    saveState = BuildReadOnlySaveState()
                };
            }
            finally
            {
                timing.Record(success, success ? null : errorKind);
            }

            object response;
            using (timing.Measure("result_shaping"))
            {
                response = success
                    ? Response.Success(message, ToolResultCompactor.ShapeStructuredPayload(
                        ToolName,
                        data,
                        BuildCompactData(data),
                        new { kind = "prefab_serialized_reference_audit_full_result" },
                        "prefab_serialized_reference_audit",
                        detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes))
                    : Response.Error(message ?? "Prefab serialized-reference audit failed.", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            return response;
        }

        static object Execute(Request request, string[] prefabPaths)
        {
            var context = new AuditContext { Request = request };
            object sceneDirtyBefore = SceneDirtyStateUtility.CaptureLoadedScenes();
            foreach (string prefabPath in prefabPaths)
            {
                AuditPrefab(context, prefabPath);
            }

            object sceneDirtyAfter = SceneDirtyStateUtility.CaptureLoadedScenes();
            var severityCounts = CountFromSummaries(context.Summaries, summary => summary.severityCounts);
            var kindCounts = CountFromSummaries(context.Summaries, summary => summary.kindCounts);
            bool passed = context.TotalFindingCount == 0;

            return new
            {
                status = passed ? "passed" : "findings",
                passed,
                readOnly = true,
                scannedPrefabCount = context.Summaries.Count,
                requestedPrefabCount = prefabPaths.Length,
                findingCount = context.TotalFindingCount,
                returnedFindingCount = context.Findings.Count,
                severityCounts,
                kindCounts,
                truncated = context.Truncated,
                policy = new
                {
                    referenceNullPolicy = request.ReferenceNullPolicy,
                    includeNestedPrefabInstances = request.IncludeNestedPrefabInstances,
                    includeRuntimeLoadPatterns = request.IncludeRuntimeLoadPatterns,
                    maxPrefabs = request.MaxPrefabs,
                    maxFindings = request.MaxFindings
                },
                scan = new
                {
                    under = request.Under,
                    nameFilter = request.NameFilter,
                    explicitPaths = BuildExplicitPathRows(request),
                    prefabPaths
                },
                summaries = context.Summaries,
                findings = context.Findings,
                saveState = BuildReadOnlySaveState(),
                dirtyEvidence = new
                {
                    prefabAssetsDirtyBeforeCount = context.Summaries.Count(summary => summary.dirtyBefore),
                    prefabAssetsDirtyAfterCount = context.Summaries.Count(summary => summary.dirtyAfter),
                    prefabAssetsDirtiedByAudit = context.Summaries
                        .Where(summary => !summary.dirtyBefore && summary.dirtyAfter)
                        .Select(summary => summary.prefabPath)
                        .ToArray(),
                    sceneDirtyBefore,
                    sceneDirtyAfter
                }
            };
        }

        static void AuditPrefab(AuditContext context, string prefabPath)
        {
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            bool dirtyBefore = prefabAsset != null && EditorUtility.IsDirty(prefabAsset);
            string guid = AssetDatabase.AssetPathToGUID(prefabPath);
            var summary = new PrefabSummary
            {
                prefabPath = prefabPath,
                guid = guid,
                dirtyBefore = dirtyBefore
            };

            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                if (root == null)
                {
                    AddFinding(context, summary, "error", "broken_nested_prefab_instance", $"Prefab '{prefabPath}' could not be loaded.", prefabPath, guid, null, null);
                    return;
                }

                var objects = root.GetComponentsInChildren<Transform>(true)
                    .Select(transform => transform.gameObject)
                    .ToArray();
                summary.objectCount = objects.Length;

                foreach (GameObject gameObject in objects)
                {
                    AuditGameObject(context, summary, prefabPath, guid, root, gameObject);
                }
            }
            finally
            {
                if (root != null)
                    PrefabUtility.UnloadPrefabContents(root);

                GameObject afterAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                summary.dirtyAfter = afterAsset != null && EditorUtility.IsDirty(afterAsset);
                context.Summaries.Add(summary);
            }
        }

        static void AuditGameObject(AuditContext context, PrefabSummary summary, string prefabPath, string guid, GameObject root, GameObject gameObject)
        {
            string hierarchyPath = GetRelativePath(root.transform, gameObject.transform);
            int missingScriptCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
            summary.missingScriptCount += missingScriptCount;
            for (int index = 0; index < missingScriptCount; index++)
            {
                AddFinding(context, summary, "error", "missing_script", "GameObject has a missing MonoBehaviour/script component.", prefabPath, guid, hierarchyPath, null);
            }

            Component[] components = gameObject.GetComponents<Component>();
            summary.componentCount += components.Count(component => component != null);
            for (int index = 0; index < components.Length; index++)
            {
                Component component = components[index];
                if (component == null)
                    continue;

                string componentType = component.GetType().FullName;
                AuditVisualComponent(context, summary, prefabPath, guid, hierarchyPath, component, componentType, index);
                AuditSerializedObjectReferences(context, summary, prefabPath, guid, hierarchyPath, component, componentType, index);

                if (context.Request.IncludeRuntimeLoadPatterns && component is MonoBehaviour monoBehaviour)
                    AuditRuntimeLoadPatterns(context, summary, prefabPath, guid, hierarchyPath, monoBehaviour, componentType, index);
            }

            if (context.Request.IncludeNestedPrefabInstances)
                AuditNestedPrefabInstance(context, summary, prefabPath, guid, root, gameObject, hierarchyPath);
        }

        static void AuditVisualComponent(
            AuditContext context,
            PrefabSummary summary,
            string prefabPath,
            string guid,
            string hierarchyPath,
            Component component,
            string componentType,
            int componentIndex)
        {
            if (component is Image image && image.sprite == null)
            {
                AddFinding(context, summary, "warning", "image_without_sprite", "UnityEngine.UI.Image has no sprite assigned.", prefabPath, guid, hierarchyPath, componentType, componentIndex: componentIndex);
            }

            if (component is SpriteRenderer spriteRenderer && spriteRenderer.sprite == null)
            {
                AddFinding(context, summary, "warning", "visual_component_missing_asset", "SpriteRenderer has no sprite assigned.", prefabPath, guid, hierarchyPath, componentType, componentIndex: componentIndex, propertyPath: "m_Sprite");
            }

            if (component is Renderer renderer)
            {
                Material[] materials = renderer.sharedMaterials ?? Array.Empty<Material>();
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    if (materials[materialIndex] == null)
                    {
                        AddFinding(context, summary, "warning", "visual_component_missing_asset", $"Renderer material slot {materialIndex} is null.", prefabPath, guid, hierarchyPath, componentType, componentIndex: componentIndex, propertyPath: $"m_Materials.Array.data[{materialIndex}]");
                    }
                }
            }
        }

        static void AuditSerializedObjectReferences(
            AuditContext context,
            PrefabSummary summary,
            string prefabPath,
            string guid,
            string hierarchyPath,
            Component component,
            string componentType,
            int componentIndex)
        {
            SerializedObject serializedObject = null;
            try
            {
                serializedObject = new SerializedObject(component);
                SerializedProperty iterator = serializedObject.GetIterator();
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (iterator.propertyType != SerializedPropertyType.ObjectReference || IsIgnoredObjectReference(component, iterator.propertyPath))
                        continue;

                    if (iterator.objectReferenceValue == null && iterator.objectReferenceInstanceIDValue != 0)
                    {
                        AddFinding(
                            context,
                            summary,
                            "error",
                            "broken_object_reference",
                            $"Serialized object reference '{iterator.propertyPath}' is broken and points to missing instance id {iterator.objectReferenceInstanceIDValue}.",
                            prefabPath,
                            guid,
                            hierarchyPath,
                            componentType,
                            componentIndex,
                            iterator.propertyPath,
                            iterator.displayName,
                            iterator.objectReferenceInstanceIDValue);
                        continue;
                    }

                    if (iterator.objectReferenceValue != null || string.Equals(context.Request.ReferenceNullPolicy, "broken_only", StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool reportLikelyRequired = string.Equals(context.Request.ReferenceNullPolicy, "all", StringComparison.OrdinalIgnoreCase) ||
                        IsLikelyRequiredReference(componentType, iterator.propertyPath, iterator.displayName);
                    if (!reportLikelyRequired)
                        continue;

                    string severity = string.Equals(context.Request.ReferenceNullPolicy, "all", StringComparison.OrdinalIgnoreCase) ? "info" : "warning";
                    AddFinding(
                        context,
                        summary,
                        severity,
                        "null_likely_required_reference",
                        $"Serialized object reference '{iterator.propertyPath}' is null under policy '{context.Request.ReferenceNullPolicy}'.",
                        prefabPath,
                        guid,
                        hierarchyPath,
                        componentType,
                        componentIndex,
                        iterator.propertyPath,
                        iterator.displayName,
                        iterator.objectReferenceInstanceIDValue);
                }
            }
            catch (Exception ex)
            {
                AddFinding(context, summary, "warning", "serialized_object_scan_failed", $"SerializedObject scan failed for component '{componentType}': {ex.Message}", prefabPath, guid, hierarchyPath, componentType, componentIndex: componentIndex);
            }
        }

        static void AuditRuntimeLoadPatterns(
            AuditContext context,
            PrefabSummary summary,
            string prefabPath,
            string guid,
            string hierarchyPath,
            MonoBehaviour monoBehaviour,
            string componentType,
            int componentIndex)
        {
            MonoScript script = MonoScript.FromMonoBehaviour(monoBehaviour);
            if (script == null)
                return;

            string scriptPath = AssetDatabase.GetAssetPath(script);
            if (string.IsNullOrWhiteSpace(scriptPath))
                return;

            if (!scriptPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return;

            if (!context.ScriptPatternCache.TryGetValue(scriptPath, out string[] matches))
            {
                matches = FindRuntimeLoadPatternLines(script.text);
                context.ScriptPatternCache[scriptPath] = matches;
            }

            foreach (string match in matches)
            {
                int separator = match.IndexOf(':');
                int lineNumber = separator > 0 && int.TryParse(match.Substring(0, separator), out int parsed) ? parsed : 0;
                string preview = separator > 0 ? match.Substring(separator + 1).Trim() : match;
                AddFinding(
                    context,
                    summary,
                    "warning",
                    "runtime_asset_load_pattern",
                    "Component script contains a runtime asset-load pattern that can hide sprite/material/texture dependencies from prefab serialization.",
                    prefabPath,
                    guid,
                    hierarchyPath,
                    componentType,
                    componentIndex,
                    scriptPath: scriptPath,
                    lineNumber: lineNumber > 0 ? lineNumber : null,
                    linePreview: preview);
            }
        }

        static void AuditNestedPrefabInstance(AuditContext context, PrefabSummary summary, string prefabPath, string guid, GameObject root, GameObject gameObject, string hierarchyPath)
        {
            if (gameObject == root || !PrefabUtility.IsPartOfPrefabInstance(gameObject))
                return;

            GameObject nearestRoot = PrefabUtility.GetNearestPrefabInstanceRoot(gameObject);
            if (nearestRoot != gameObject)
                return;

            PrefabInstanceStatus status = PrefabUtility.GetPrefabInstanceStatus(gameObject);
            if (status == PrefabInstanceStatus.NotAPrefab || status == PrefabInstanceStatus.Connected)
                return;

            string sourcePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
            AddFinding(
                context,
                summary,
                "error",
                "broken_nested_prefab_instance",
                $"Nested prefab instance is {status}.",
                prefabPath,
                guid,
                hierarchyPath,
                gameObject.GetType().FullName,
                nestedPrefabStatus: status.ToString(),
                nestedPrefabAssetPath: sourcePath);
        }

        static void AddFinding(
            AuditContext context,
            PrefabSummary summary,
            string severity,
            string kind,
            string message,
            string prefabPath,
            string guid,
            string hierarchyPath,
            string componentType,
            int? componentIndex = null,
            string propertyPath = null,
            string propertyDisplayName = null,
            int? objectReferenceInstanceId = null,
            string scriptPath = null,
            int? lineNumber = null,
            string linePreview = null,
            string nestedPrefabStatus = null,
            string nestedPrefabAssetPath = null)
        {
            context.TotalFindingCount++;
            summary.findingCount++;
            Increment(summary.severityCounts, severity);
            Increment(summary.kindCounts, kind);

            if (context.Findings.Count >= context.Request.MaxFindings)
            {
                context.Truncated = true;
                return;
            }

            context.Findings.Add(new FindingRow
            {
                index = context.TotalFindingCount - 1,
                severity = severity,
                kind = kind,
                message = message,
                prefabPath = prefabPath,
                prefabGuid = guid,
                hierarchyPath = hierarchyPath,
                componentType = componentType,
                componentIndex = componentIndex,
                propertyPath = propertyPath,
                propertyDisplayName = propertyDisplayName,
                objectReferenceInstanceId = objectReferenceInstanceId,
                scriptPath = scriptPath,
                lineNumber = lineNumber,
                linePreview = linePreview,
                nestedPrefabStatus = nestedPrefabStatus,
                nestedPrefabAssetPath = nestedPrefabAssetPath
            });
        }

        static Request Normalize(JObject parameters)
        {
            string nullPolicy = GetString(parameters, "referenceNullPolicy", "ReferenceNullPolicy");
            if (!string.Equals(nullPolicy, "broken_only", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(nullPolicy, "likely_required", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(nullPolicy, "all", StringComparison.OrdinalIgnoreCase))
            {
                nullPolicy = "likely_required";
            }

            return new Request
            {
                PrefabPath = NormalizeAssetPath(GetString(parameters, "prefabPath", "PrefabPath")),
                PrefabPaths = GetStringArray(parameters, "prefabPaths", "PrefabPaths").Select(NormalizeAssetPath).ToArray(),
                Under = NormalizeFolderPath(GetString(parameters, "under", "Under") ?? "Assets"),
                NameFilter = GetString(parameters, "nameFilter", "NameFilter"),
                MaxPrefabs = Clamp(GetInt(parameters, DefaultMaxPrefabs, "maxPrefabs", "MaxPrefabs"), 1, MaxPrefabLimit),
                MaxFindings = Clamp(GetInt(parameters, DefaultMaxFindings, "maxFindings", "MaxFindings"), 1, MaxFindingLimit),
                ReferenceNullPolicy = nullPolicy,
                IncludeNestedPrefabInstances = GetBool(parameters, true, "includeNestedPrefabInstances", "IncludeNestedPrefabInstances"),
                IncludeRuntimeLoadPatterns = GetBool(parameters, true, "includeRuntimeLoadPatterns", "IncludeRuntimeLoadPatterns")
            };
        }

        static bool TryResolvePrefabPaths(Request request, out string[] prefabPaths, out object errorData, out string errorMessage)
        {
            errorData = null;
            errorMessage = null;
            var explicitPaths = new List<string>();
            if (!string.IsNullOrWhiteSpace(request.PrefabPath))
                explicitPaths.Add(request.PrefabPath);
            explicitPaths.AddRange(request.PrefabPaths.Where(path => !string.IsNullOrWhiteSpace(path)));

            if (explicitPaths.Count > 0)
            {
                prefabPaths = explicitPaths
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(request.MaxPrefabs)
                    .ToArray();
                var invalid = prefabPaths
                    .Where(path => !IsPrefabAssetPath(path) || AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                    .ToArray();
                if (invalid.Length > 0)
                {
                    errorMessage = "One or more explicit prefab paths are invalid or could not be loaded.";
                    errorData = new
                    {
                        status = "invalid_prefab_paths",
                        invalidPaths = invalid,
                        requestedPaths = explicitPaths,
                        saveState = BuildReadOnlySaveState()
                    };
                    return false;
                }

                return true;
            }

            string folder = string.IsNullOrWhiteSpace(request.Under) ? "Assets" : request.Under;
            if (!folder.StartsWith("Assets", StringComparison.OrdinalIgnoreCase) && !folder.StartsWith("Packages", StringComparison.OrdinalIgnoreCase))
            {
                prefabPaths = Array.Empty<string>();
                errorMessage = "under must be a project folder path under Assets/ or Packages/.";
                errorData = new
                {
                    status = "invalid_scan_root",
                    under = request.Under,
                    saveState = BuildReadOnlySaveState()
                };
                return false;
            }

            prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => IsPrefabAssetPath(path))
                .Where(path => string.IsNullOrWhiteSpace(request.NameFilter) || System.IO.Path.GetFileNameWithoutExtension(path).IndexOf(request.NameFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Take(request.MaxPrefabs)
                .ToArray();
            return true;
        }

        static object BuildCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            TruncateArray(root, "summaries", 30);
            TruncateArray(root, "findings", 80);
            if (root["dirtyEvidence"] is JObject dirtyEvidence)
            {
                dirtyEvidence.Remove("sceneDirtyBefore");
                dirtyEvidence.Remove("sceneDirtyAfter");
            }

            return root;
        }

        static string[] FindRuntimeLoadPatternLines(string scriptText)
        {
            if (string.IsNullOrWhiteSpace(scriptText))
                return Array.Empty<string>();

            string[] lines = scriptText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var matches = new List<string>();
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                    trimmed.StartsWith("/*", StringComparison.Ordinal) ||
                    trimmed.StartsWith("*", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.IndexOf("Resources.Load", StringComparison.Ordinal) < 0 &&
                    line.IndexOf("Resources.LoadAsync", StringComparison.Ordinal) < 0 &&
                    line.IndexOf("Addressables.LoadAssetAsync", StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                string lower = line.ToLowerInvariant();
                bool assetTyped =
                    lower.Contains("sprite") ||
                    lower.Contains("material") ||
                    lower.Contains("texture") ||
                    lower.Contains("<object>") ||
                    lower.Contains("loadassetasync");
                if (assetTyped)
                    matches.Add($"{index + 1}:{line.Trim()}");
            }

            return matches.ToArray();
        }

        static bool IsIgnoredObjectReference(Component component, string propertyPath)
        {
            if (string.IsNullOrWhiteSpace(propertyPath))
                return true;

            if (propertyPath == "m_Material" && component is Graphic)
                return true;

            if (propertyPath == "m_Sprite" && component is Image)
                return true;

            return propertyPath == "m_Script" ||
                propertyPath == "m_GameObject" ||
                propertyPath == "m_PrefabAsset" ||
                propertyPath == "m_PrefabInstance" ||
                propertyPath == "m_Father" ||
                propertyPath.StartsWith("m_Children", StringComparison.Ordinal);
        }

        static bool IsLikelyRequiredReference(string componentType, string propertyPath, string displayName)
        {
            string haystack = $"{componentType} {propertyPath} {displayName}".ToLowerInvariant();
            return k_LikelyRequiredReferenceTerms.Any(term => haystack.Contains(term));
        }

        static object BuildReadOnlySaveState()
        {
            return new
            {
                requested = false,
                attempted = false,
                saved = false,
                message = "not_requested_read_only_audit"
            };
        }

        static object BuildExplicitPathRows(Request request)
        {
            var rows = new List<string>();
            if (!string.IsNullOrWhiteSpace(request.PrefabPath))
                rows.Add(request.PrefabPath);
            rows.AddRange(request.PrefabPaths ?? Array.Empty<string>());
            return rows.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        static Dictionary<string, int> CountBy(IEnumerable<FindingRow> rows, Func<FindingRow, string> selector)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (FindingRow row in rows)
                Increment(result, selector(row));
            return result;
        }

        static Dictionary<string, int> CountFromSummaries(IEnumerable<PrefabSummary> summaries, Func<PrefabSummary, Dictionary<string, int>> selector)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (PrefabSummary summary in summaries)
            {
                Dictionary<string, int> source = selector(summary);
                if (source == null)
                    continue;

                foreach (var pair in source)
                {
                    result.TryGetValue(pair.Key, out int current);
                    result[pair.Key] = current + pair.Value;
                }
            }

            return result;
        }

        static void Increment(Dictionary<string, int> counts, string key)
        {
            key ??= "unknown";
            counts.TryGetValue(key, out int current);
            counts[key] = current + 1;
        }

        static void TruncateArray(JObject root, string propertyName, int limit)
        {
            if (root[propertyName] is not JArray array || array.Count <= limit)
                return;

            root[propertyName] = new JArray(array.Take(limit));
            root[$"omitted{ToPascalCase(propertyName)}Count"] = array.Count - limit;
        }

        static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null || root == target)
                return ".";

            var parts = new Stack<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                parts.Push(current.name);
                current = current.parent;
            }

            return current == root ? string.Join("/", parts) : target.name;
        }

        static bool IsPrefabAssetPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) &&
                path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
        }

        static string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            string normalized = path.Trim().Replace('\\', '/');
            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            return "Assets/" + normalized.TrimStart('/');
        }

        static string NormalizeFolderPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "Assets";

            string normalized = path.Trim().Replace('\\', '/').TrimEnd('/');
            if (normalized.StartsWith("Assets", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Packages", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            return "Assets/" + normalized.TrimStart('/');
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
            return token?.Type == JTokenType.Null ? null : token?.ToString();
        }

        static bool GetBool(JObject obj, bool fallback, params string[] names)
        {
            JToken token = GetToken(obj, names);
            if (token == null || token.Type == JTokenType.Null)
                return fallback;

            return token.Type switch
            {
                JTokenType.Boolean => token.Value<bool>(),
                JTokenType.String when bool.TryParse(token.Value<string>(), out bool value) => value,
                _ => fallback
            };
        }

        static int GetInt(JObject obj, int fallback, params string[] names)
        {
            JToken token = GetToken(obj, names);
            if (token == null || token.Type == JTokenType.Null)
                return fallback;

            return token.Type switch
            {
                JTokenType.Integer => token.Value<int>(),
                JTokenType.String when int.TryParse(token.Value<string>(), out int parsed) => parsed,
                _ => fallback
            };
        }

        static string[] GetStringArray(JObject obj, params string[] names)
        {
            JToken token = GetToken(obj, names);
            if (token == null || token.Type == JTokenType.Null)
                return Array.Empty<string>();

            if (token is JArray array)
            {
                return array
                    .Select(item => item?.ToString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();
            }

            string single = token.ToString();
            return string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : new[] { single };
        }

        static int Clamp(int value, int min, int max)
        {
            return Math.Min(max, Math.Max(min, value));
        }

        static string ToPascalCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }
    }
}
