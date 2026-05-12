#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public sealed class ObjectResolveStablePathParams
    {
        [McpDescription("Hierarchy path, indexed hierarchy path, object id, or object name to resolve.", Required = true)]
        public string Path { get; set; }

        [McpDescription("Alias for Path. Used when callers pass a target field from another Lens tool.", Required = false)]
        public string Target { get; set; }

        [McpDescription("Resolution mode: scene, runtime, ui, or any. Defaults to scene.", Required = false, Default = "scene")]
        public string Mode { get; set; } = "scene";

        [McpDescription("Include inactive scene objects while resolving.", Required = false, Default = false)]
        public bool IncludeInactive { get; set; } = false;

        [McpDescription("Maximum candidate rows to return inline.", Required = false, Default = 20)]
        public int MaxCandidates { get; set; } = 20;
    }

    public static class ObjectResolveStablePathTools
    {
        const string ToolName = "Unity.Object.ResolveStablePath";

        public const string ResolveStablePathDescription = @"Resolves a hierarchy path emitted by Lens scene, runtime, or UI tools into stable object identity and canonical path rows.

Args:
    Path: Hierarchy path, indexed hierarchy path, object id, or object name to resolve.
    Target: Alias for Path when passing a target field from another tool.
    Mode: scene, runtime, ui, or any. UI mode filters to RectTransform-backed objects.
    IncludeInactive: Include inactive scene objects.
    MaxCandidates: Maximum candidate rows returned inline.

Returns:
    Dictionary with success/message/data. Data contains selected stable id, canonical hierarchy path, scene, components, ambiguity count, and candidate rows. Duplicate sibling names are disambiguated with indexedPath segments such as Button[1].";

        [McpSchema(ToolName)]
        public static object GetSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    path = new { type = "string", description = "Hierarchy path, indexed hierarchy path, object id, or object name to resolve." },
                    target = new { type = "string", description = "Alias for path." },
                    mode = new { type = "string", description = "Resolution mode: scene, runtime, ui, or any. Defaults to scene." },
                    includeInactive = new { type = "boolean", description = "Include inactive scene objects while resolving. Defaults to false." },
                    maxCandidates = new { type = "integer", description = "Maximum candidate rows returned inline. Defaults to 20." }
                }
            };
        }

        [McpOutputSchema(ToolName)]
        public static object GetOutputSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    success = new { type = "boolean", description = "Whether at least one candidate resolved." },
                    message = new { type = "string", description = "Human-readable resolution summary." },
                    data = new
                    {
                        type = "object",
                        properties = new
                        {
                            requestedPath = new { type = "string", description = "Normalized requested path or target." },
                            mode = new { type = "string", description = "Normalized resolution mode." },
                            resolved = new { type = "boolean", description = "Whether any candidate matched." },
                            ambiguityCount = new { type = "integer", description = "Number of additional candidates beyond the selected row." },
                            selected = new { type = "object", description = "First deterministic match with stable id, canonical path, scene, and component list." },
                            candidates = new { type = "array", description = "Candidate rows ordered by match quality and canonical path." },
                            detailRef = new { type = "object", description = "Detail ref for all candidates when compacted." }
                        }
                    }
                },
                required = new[] { "success", "message" }
            };
        }

        [McpTool(ToolName, ResolveStablePathDescription, Groups = new[] { "scene", "runtime", "ui" }, EnabledByDefault = true)]
        public static object ResolveStablePath(ObjectResolveStablePathParams parameters)
        {
            parameters ??= new ObjectResolveStablePathParams();
            var timing = new ToolOperationTiming(ToolName, "resolve_stable_path", 0);
            object data;
            bool success = true;
            string errorKind = null;

            try
            {
                string requestedPath;
                string mode;
                int maxCandidates;
                using (timing.Measure("normalization"))
                {
                    requestedPath = NormalizePath(string.IsNullOrWhiteSpace(parameters.Path) ? parameters.Target : parameters.Path);
                    mode = NormalizeMode(parameters.Mode);
                    maxCandidates = Math.Clamp(parameters.MaxCandidates <= 0 ? 20 : parameters.MaxCandidates, 1, 200);
                }

                using (timing.Measure("service"))
                {
                    if (string.IsNullOrWhiteSpace(requestedPath))
                    {
                        success = false;
                        errorKind = "missing_path";
                        data = new
                        {
                            requestedPath,
                            mode,
                            parameters.IncludeInactive,
                            resolved = false,
                            ambiguityCount = 0,
                            selected = (object)null,
                            candidates = Array.Empty<object>(),
                            warning = "Path or target is required."
                        };
                    }
                    else
                    {
                        List<ResolvedObjectCandidate> candidates = ResolveCandidates(
                            requestedPath,
                            mode,
                            parameters.IncludeInactive);
                        success = candidates.Count > 0;
                        errorKind = success ? null : "object_path_not_found";
                        data = BuildPayload(
                            requestedPath,
                            mode,
                            parameters.IncludeInactive,
                            maxCandidates,
                            candidates);
                    }
                }

                using (timing.Measure("adapter"))
                {
                }
            }
            catch (Exception ex)
            {
                success = false;
                errorKind = ex.GetType().Name;
                data = new
                {
                    errorKind,
                    error = ex.Message,
                    finalState = EditorToolStateHelpers.BuildEditorState()
                };
            }

            object response;
            using (timing.Measure("result_shaping"))
            {
                string json = JsonConvert.SerializeObject(data, Formatting.None);
                response = success
                    ? Response.Success("Resolved object path.", data)
                    : Response.Error("Object path could not be resolved.", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, errorKind);
            return response;
        }

        static object BuildPayload(
            string requestedPath,
            string mode,
            bool includeInactive,
            int maxCandidates,
            List<ResolvedObjectCandidate> candidates)
        {
            object[] allCandidateRows = candidates
                .Select(candidate => candidate.Row)
                .ToArray();
            object[] inlineCandidates = allCandidateRows
                .Take(maxCandidates)
                .ToArray();
            var rawPayload = new
            {
                requestedPath,
                mode,
                includeInactive,
                resolved = candidates.Count > 0,
                candidateCount = candidates.Count,
                returnedCandidateCount = inlineCandidates.Length,
                ambiguityCount = Math.Max(0, candidates.Count - 1),
                selected = candidates.FirstOrDefault()?.Row,
                candidates = allCandidateRows,
                warnings = BuildWarnings(candidates)
            };
            var compactPayload = new
            {
                rawPayload.requestedPath,
                rawPayload.mode,
                rawPayload.includeInactive,
                rawPayload.resolved,
                rawPayload.candidateCount,
                rawPayload.returnedCandidateCount,
                rawPayload.ambiguityCount,
                rawPayload.selected,
                candidates = inlineCandidates,
                rawPayload.warnings
            };

            return ToolResultCompactor.ShapeStructuredPayload(
                ToolName,
                rawPayload,
                compactPayload,
                new
                {
                    kind = "object_path_resolution",
                    requestedPath,
                    mode,
                    candidateCount = candidates.Count,
                    ambiguityCount = Math.Max(0, candidates.Count - 1)
                },
                "object_path_resolution_result",
                detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes);
        }

        static object[] BuildWarnings(List<ResolvedObjectCandidate> candidates)
        {
            var warnings = new List<object>();
            if (candidates.Count > 1)
            {
                warnings.Add(new
                {
                    kind = "ambiguous_path",
                    message = $"Resolved {candidates.Count} candidates. Use selected.indexedPath or selected.stableId to disambiguate follow-up calls."
                });
            }

            if (candidates.Any(candidate => candidate.DuplicateSiblingNameCount > 1))
            {
                warnings.Add(new
                {
                    kind = "duplicate_sibling_names",
                    message = "At least one resolved candidate has same-name siblings; indexedPath includes sibling-name indexes for stable diagnostics."
                });
            }

            return warnings.ToArray();
        }

        static List<ResolvedObjectCandidate> ResolveCandidates(string requestedPath, string mode, bool includeInactive)
        {
            string deindexedRequestedPath = StripSegmentIndexes(requestedPath);
            FindObjectsInactive inactiveMode = includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
            return UnityApiAdapter.FindObjectsByType<GameObject>(inactiveMode)
                .Where(gameObject => gameObject != null && gameObject.scene.IsValid())
                .Where(gameObject => MatchesMode(gameObject, mode))
                .Select(gameObject => BuildCandidate(requestedPath, deindexedRequestedPath, mode, gameObject))
                .Where(candidate => candidate.MatchRank < int.MaxValue)
                .OrderBy(candidate => candidate.MatchRank)
                .ThenBy(candidate => candidate.IndexedPath, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.StableId, StringComparer.Ordinal)
                .ToList();
        }

        static ResolvedObjectCandidate BuildCandidate(string requestedPath, string deindexedRequestedPath, string mode, GameObject gameObject)
        {
            string path = UiDiagnosticsHelper.GetHierarchyPath(gameObject.transform);
            string indexedPath = GetIndexedHierarchyPath(gameObject.transform);
            string sceneQualifiedPath = string.IsNullOrWhiteSpace(gameObject.scene.name) ? path : $"{gameObject.scene.name}/{path}";
            string sceneQualifiedIndexedPath = string.IsNullOrWhiteSpace(gameObject.scene.name) ? indexedPath : $"{gameObject.scene.name}/{indexedPath}";
#pragma warning disable CS0618
            string stableId = gameObject.GetInstanceID().ToString(CultureInfo.InvariantCulture);
#pragma warning restore CS0618
            int matchRank = GetMatchRank(
                requestedPath,
                deindexedRequestedPath,
                stableId,
                gameObject.name,
                path,
                indexedPath,
                sceneQualifiedPath,
                sceneQualifiedIndexedPath);
            int siblingNameIndex = GetSiblingNameIndex(gameObject.transform, out int duplicateSiblingNameCount);
            object row = new
            {
                name = gameObject.name,
                stableId,
                objectId = stableId,
                path,
                canonicalPath = indexedPath,
                indexedPath,
                sceneQualifiedPath,
                sceneQualifiedIndexedPath,
                mode = DetermineObjectMode(gameObject),
                requestedMode = mode,
                matchKind = DescribeMatchKind(matchRank),
                matchRank,
                activeSelf = gameObject.activeSelf,
                activeInHierarchy = gameObject.activeInHierarchy,
                scene = new
                {
                    name = gameObject.scene.name,
                    path = gameObject.scene.path,
                    isLoaded = gameObject.scene.isLoaded
                },
                parentPath = gameObject.transform.parent != null ? UiDiagnosticsHelper.GetHierarchyPath(gameObject.transform.parent) : string.Empty,
                siblingIndex = gameObject.transform.GetSiblingIndex(),
                siblingNameIndex,
                duplicateSiblingNameCount,
                hasRectTransform = gameObject.transform is RectTransform,
                canvasPath = gameObject.GetComponentInParent<Canvas>(true) != null
                    ? UiDiagnosticsHelper.GetHierarchyPath(gameObject.GetComponentInParent<Canvas>(true).transform)
                    : string.Empty,
                componentCount = gameObject.GetComponents<Component>().Length,
                componentTypes = gameObject.GetComponents<Component>()
                    .Select(component => component == null ? "<missing>" : component.GetType().FullName)
                    .ToArray()
            };

            return new ResolvedObjectCandidate
            {
                Row = row,
                StableId = stableId,
                IndexedPath = indexedPath,
                MatchRank = matchRank,
                DuplicateSiblingNameCount = duplicateSiblingNameCount
            };
        }

        static int GetMatchRank(
            string requestedPath,
            string deindexedRequestedPath,
            string stableId,
            string objectName,
            string path,
            string indexedPath,
            string sceneQualifiedPath,
            string sceneQualifiedIndexedPath)
        {
            if (string.Equals(requestedPath, stableId, StringComparison.Ordinal))
                return 0;
            if (string.Equals(requestedPath, indexedPath, StringComparison.Ordinal) ||
                string.Equals(requestedPath, sceneQualifiedIndexedPath, StringComparison.Ordinal))
            {
                return 1;
            }

            if (string.Equals(requestedPath, path, StringComparison.Ordinal) ||
                string.Equals(requestedPath, sceneQualifiedPath, StringComparison.Ordinal))
            {
                return 2;
            }

            if (string.Equals(deindexedRequestedPath, path, StringComparison.Ordinal) ||
                string.Equals(deindexedRequestedPath, sceneQualifiedPath, StringComparison.Ordinal))
            {
                return 3;
            }

            if (string.Equals(requestedPath, objectName, StringComparison.Ordinal))
                return 4;

            if (string.Equals(requestedPath, indexedPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(requestedPath, sceneQualifiedIndexedPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(requestedPath, path, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(requestedPath, sceneQualifiedPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(deindexedRequestedPath, path, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(deindexedRequestedPath, sceneQualifiedPath, StringComparison.OrdinalIgnoreCase))
            {
                return 5;
            }

            return int.MaxValue;
        }

        static string DescribeMatchKind(int matchRank)
        {
            return matchRank switch
            {
                0 => "stable_id",
                1 => "indexed_path",
                2 => "hierarchy_path",
                3 => "deindexed_path",
                4 => "name",
                5 => "case_insensitive_path",
                _ => "none"
            };
        }

        static bool MatchesMode(GameObject gameObject, string mode)
        {
            return mode switch
            {
                "ui" => gameObject.transform is RectTransform || gameObject.GetComponent<Canvas>() != null || gameObject.GetComponent<Graphic>() != null,
                "runtime" => true,
                "scene" => true,
                "any" => true,
                _ => true
            };
        }

        static string DetermineObjectMode(GameObject gameObject)
        {
            if (gameObject == null)
                return string.Empty;
            if (gameObject.transform is RectTransform || gameObject.GetComponent<Canvas>() != null || gameObject.GetComponent<Graphic>() != null)
                return "ui";
            return EditorApplication.isPlaying ? "runtime" : "scene";
        }

        static string NormalizeMode(string mode)
        {
            string normalized = string.IsNullOrWhiteSpace(mode) ? "scene" : mode.Trim().ToLowerInvariant();
            return normalized is "scene" or "runtime" or "ui" or "any" ? normalized : "scene";
        }

        static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return path.Replace('\\', '/').Trim().Trim('/');
        }

        static string StripSegmentIndexes(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            string[] segments = path.Split('/');
            for (int i = 0; i < segments.Length; i++)
                segments[i] = StripSegmentIndex(segments[i]);
            return string.Join("/", segments);
        }

        static string StripSegmentIndex(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment) || !segment.EndsWith("]", StringComparison.Ordinal))
                return segment;

            int open = segment.LastIndexOf('[');
            if (open <= 0)
                return segment;

            string indexText = segment.Substring(open + 1, segment.Length - open - 2);
            return int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                ? segment.Substring(0, open)
                : segment;
        }

        static string GetIndexedHierarchyPath(Transform transform)
        {
            if (transform == null)
                return string.Empty;

            var segments = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                string segment = current.name;
                int siblingNameIndex = GetSiblingNameIndex(current, out int duplicateSiblingNameCount);
                if (duplicateSiblingNameCount > 1)
                    segment += $"[{siblingNameIndex}]";
                segments.Push(segment);
                current = current.parent;
            }

            return string.Join("/", segments);
        }

        static int GetSiblingNameIndex(Transform transform, out int duplicateSiblingNameCount)
        {
            duplicateSiblingNameCount = 1;
            if (transform == null)
                return 0;

            IEnumerable<Transform> siblings = transform.parent != null
                ? Enumerable.Range(0, transform.parent.childCount).Select(index => transform.parent.GetChild(index))
                : UnityApiAdapter.FindObjectsByType<GameObject>(FindObjectsInactive.Include)
                    .Where(gameObject => gameObject != null && gameObject.scene == transform.gameObject.scene && gameObject.transform.parent == null)
                    .Select(gameObject => gameObject.transform);

            var sameNameSiblings = siblings
                .Where(sibling => sibling != null && string.Equals(sibling.name, transform.name, StringComparison.Ordinal))
                .OrderBy(sibling => sibling.GetSiblingIndex())
                .ToList();
            duplicateSiblingNameCount = sameNameSiblings.Count;
            return Math.Max(0, sameNameSiblings.IndexOf(transform));
        }

        sealed class ResolvedObjectCandidate
        {
            public object Row { get; init; }
            public string StableId { get; init; }
            public string IndexedPath { get; init; }
            public int MatchRank { get; init; }
            public int DuplicateSiblingNameCount { get; init; }
        }
    }
}
