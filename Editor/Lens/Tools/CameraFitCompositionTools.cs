#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Becool.UnityMcpLens.Editor.Adapters.Unity;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class CameraFitCompositionTools
    {
        const string ToolName = "Unity.Camera.FitComposition";

        sealed class CameraFitRequest
        {
            public string Target;
            public string SearchMethod = "by_id_or_name_or_path";
            public string NamePrefix;
            public string NameExact;
            public string[] ComponentTypes = Array.Empty<string>();
            public string ComponentMatch = "all";
            public string Root;
            public string RootSearchMethod = "by_id_or_name_or_path";
            public string Scene;
            public bool IncludeInactive;
            public string CameraTarget;
            public string CameraSearchMethod = "by_id_or_name_or_path";
            public float DesiredCoverageMin = 0.45f;
            public float DesiredCoverageMax = 0.75f;
            public float AspectRatio = 16f / 9f;
            public int ViewportWidth = 1280;
            public int ViewportHeight = 720;
            public bool CaptureScreenshot = true;
            public string OutputPath;
            public int ScreenshotWidth = 1280;
            public int ScreenshotHeight = 720;
            public int MaxRows = 50;
        }

        sealed class BoundsAnalysis
        {
            public bool HasBounds;
            public Bounds Bounds;
            public string BoundsSource;
            public int RendererCount;
            public int VisibleRendererCount;
            public int DisabledRendererCount;
            public int SpriteRendererCount;
            public int MissingSpriteCount;
            public int ColliderCount;
            public int VisibleColliderCount;
            public object[] MissingSprites = Array.Empty<object>();
            public object[] SortingSamples = Array.Empty<object>();
        }

        sealed class CameraPlaneProjection
        {
            public float XMin;
            public float XMax;
            public float YMin;
            public float YMax;
            public float Width;
            public float Height;
            public float DepthMin;
            public float DepthMax;
            public float DepthCenter;
            public float DepthSpan;
        }

        sealed class CoverageMetrics
        {
            public bool HasScreenRect;
            public Rect ScreenRect;
            public float WidthCoverage;
            public float HeightCoverage;
            public float Coverage;
            public bool InDesiredRange;
            public bool IntersectsViewport;
            public bool DepthInClipRange;
            public CameraPlaneProjection Plane;
        }

        [McpSchema(ToolName)]
        public static object GetSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    target = new { type = "string", description = "Optional GameObject name, hierarchy path, or instance id. When omitted, the component/name/root scene query selects targets." },
                    searchMethod = new { type = "string", description = "How to find target: by_name, by_path, by_id, or by_id_or_name_or_path. Defaults to by_id_or_name_or_path." },
                    namePrefix = new { type = "string", description = "Optional GameObject name prefix filter for query mode." },
                    nameExact = new { type = "string", description = "Optional exact GameObject name filter for query mode." },
                    componentTypes = new
                    {
                        type = "array",
                        description = "Optional component type names used to select target objects. Short or fully-qualified names are accepted.",
                        items = new { type = "string" }
                    },
                    componentMatch = new { type = "string", description = "How componentTypes are matched: all or any. Defaults to all." },
                    root = new { type = "string", description = "Optional root GameObject name, hierarchy path, or id used to scope query mode." },
                    rootSearchMethod = new { type = "string", description = "How to find root: by_name, by_path, by_id, or by_id_or_name_or_path. Defaults to by_id_or_name_or_path." },
                    scene = new { type = "string", description = "Optional scene name or path filter." },
                    includeInactive = new { type = "boolean", description = "Include inactive GameObjects and children while resolving targets and bounds. Defaults to false." },
                    cameraTarget = new { type = "string", description = "Optional camera GameObject name, hierarchy path, or id. Defaults to Camera.main or first scene camera." },
                    cameraSearchMethod = new { type = "string", description = "How to find cameraTarget. Defaults to by_id_or_name_or_path." },
                    desiredCoverageMin = new { type = "number", description = "Minimum acceptable limiting screen coverage as a 0..1 fraction. Defaults to 0.45." },
                    desiredCoverageMax = new { type = "number", description = "Maximum acceptable limiting screen coverage as a 0..1 fraction. Defaults to 0.75." },
                    aspectRatio = new { type = "number", description = "Composition aspect ratio used for coverage and suggestions. Defaults to 16:9." },
                    viewportWidth = new { type = "integer", description = "Virtual viewport width in pixels for coverage reporting. Defaults to 1280." },
                    viewportHeight = new { type = "integer", description = "Virtual viewport height in pixels for coverage reporting. Defaults to 720." },
                    captureScreenshot = new { type = "boolean", description = "Render a PNG from the resolved camera and return its path. Defaults to true." },
                    outputPath = new { type = "string", description = "Optional screenshot output path relative to the Unity project root, or unity://path/Assets/... URI. Defaults to Temp/LensCaptures." },
                    screenshotWidth = new { type = "integer", description = "Screenshot width in pixels. Defaults to viewportWidth." },
                    screenshotHeight = new { type = "integer", description = "Screenshot height in pixels. Defaults to viewportHeight." },
                    maxRows = new { type = "integer", description = "Maximum target rows returned. Defaults to 50 and is capped at 500." }
                }
            };
        }

        [McpTool(ToolName,
            "Measures how well target scene objects fill the camera view, suggests camera framing, and optionally captures a screenshot.",
            "Fit Camera Composition",
            Groups = new[] { "scene", "diagnostics" },
            EnabledByDefault = true)]
        public static object FitComposition(JObject @params)
        {
            @params ??= new JObject();
            var timing = new ToolOperationTiming(ToolName, "fit_composition", PayloadBudgeting.GetUtf8ByteCount(@params.ToString(Formatting.None)));
            bool success = true;
            string errorKind = null;
            object data;

            try
            {
                CameraFitRequest request;
                using (timing.Measure("normalization"))
                {
                    request = Normalize(@params);
                }

                using (timing.Measure("service"))
                {
                    data = BuildFitData(request);
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
                    ? Response.Success("Camera fit composition measured.", ToolResultCompactor.ShapeStructuredPayload(
                        ToolName,
                        data,
                        BuildCompactData(data),
                        new { kind = "camera_fit_composition_full_result" },
                        "camera_fit_composition",
                        detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes))
                    : Response.Error("CAMERA_FIT_COMPOSITION_FAILED", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, success ? null : errorKind);
            return response;
        }

        static CameraFitRequest Normalize(JObject parameters)
        {
            var request = new CameraFitRequest
            {
                Target = GetString(parameters, "target", "Target"),
                SearchMethod = GetString(parameters, "searchMethod", "SearchMethod") ?? "by_id_or_name_or_path",
                NamePrefix = GetString(parameters, "namePrefix", "NamePrefix"),
                NameExact = GetString(parameters, "nameExact", "NameExact"),
                ComponentTypes = GetStringArray(parameters, "componentTypes", "ComponentTypes")
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                ComponentMatch = (GetString(parameters, "componentMatch", "ComponentMatch") ?? "all").Trim().ToLowerInvariant(),
                Root = GetString(parameters, "root", "Root"),
                RootSearchMethod = GetString(parameters, "rootSearchMethod", "RootSearchMethod") ?? "by_id_or_name_or_path",
                Scene = GetString(parameters, "scene", "Scene"),
                IncludeInactive = GetBool(parameters, false, "includeInactive", "IncludeInactive"),
                CameraTarget = GetString(parameters, "cameraTarget", "CameraTarget"),
                CameraSearchMethod = GetString(parameters, "cameraSearchMethod", "CameraSearchMethod") ?? "by_id_or_name_or_path",
                DesiredCoverageMin = Clamp01(GetFloat(parameters, 0.45f, "desiredCoverageMin", "DesiredCoverageMin")),
                DesiredCoverageMax = Clamp01(GetFloat(parameters, 0.75f, "desiredCoverageMax", "DesiredCoverageMax")),
                AspectRatio = GetFloat(parameters, 16f / 9f, "aspectRatio", "AspectRatio"),
                ViewportWidth = Math.Clamp(GetInt(parameters, 1280, "viewportWidth", "ViewportWidth"), 64, 8192),
                ViewportHeight = Math.Clamp(GetInt(parameters, 720, "viewportHeight", "ViewportHeight"), 64, 8192),
                CaptureScreenshot = GetBool(parameters, true, "captureScreenshot", "CaptureScreenshot"),
                OutputPath = GetString(parameters, "outputPath", "OutputPath"),
                MaxRows = Math.Clamp(GetInt(parameters, 50, "maxRows", "MaxRows"), 1, 500)
            };

            if (request.DesiredCoverageMax < request.DesiredCoverageMin)
            {
                (request.DesiredCoverageMin, request.DesiredCoverageMax) = (request.DesiredCoverageMax, request.DesiredCoverageMin);
            }

            request.AspectRatio = Mathf.Clamp(request.AspectRatio <= 0f ? request.ViewportWidth / (float)request.ViewportHeight : request.AspectRatio, 0.1f, 10f);
            request.ScreenshotWidth = Math.Clamp(GetInt(parameters, request.ViewportWidth, "screenshotWidth", "ScreenshotWidth"), 64, 8192);
            request.ScreenshotHeight = Math.Clamp(GetInt(parameters, request.ViewportHeight, "screenshotHeight", "ScreenshotHeight"), 64, 8192);
            return request;
        }

        static object BuildFitData(CameraFitRequest request)
        {
            GameObject[] targets = ResolveTargets(request, out string[] missingTypes, out object rootData);
            Camera camera = ResolveCamera(request.CameraTarget, request.CameraSearchMethod, request.IncludeInactive, out string cameraLabel);
            BoundsAnalysis bounds = AnalyzeBounds(targets, request.IncludeInactive);
            object screenshot = null;
            CoverageMetrics metrics = null;
            object suggestion = null;

            if (camera != null && bounds.HasBounds)
            {
                metrics = MeasureCoverage(camera, bounds.Bounds, request.AspectRatio, request.ViewportWidth, request.ViewportHeight, request.DesiredCoverageMin, request.DesiredCoverageMax);
                suggestion = BuildSuggestion(camera, bounds.Bounds, metrics.Plane, request.AspectRatio, (request.DesiredCoverageMin + request.DesiredCoverageMax) * 0.5f);

                if (request.CaptureScreenshot)
                {
                    screenshot = CaptureScreenshot(camera, request);
                }
            }

            return new
            {
                status = camera == null || !bounds.HasBounds || targets.Length == 0 ? "incomplete" : "ready",
                query = new
                {
                    target = request.Target,
                    searchMethod = request.SearchMethod,
                    namePrefix = request.NamePrefix,
                    nameExact = request.NameExact,
                    componentTypes = request.ComponentTypes,
                    componentMatch = request.ComponentMatch == "any" ? "any" : "all",
                    missingTypeCount = missingTypes.Length,
                    missingTypes,
                    scene = request.Scene,
                    root = rootData,
                    includeInactive = request.IncludeInactive
                },
                targetCount = targets.Length,
                targetRows = targets.Take(request.MaxRows).Select(BuildTargetRow).ToArray(),
                omittedTargetCount = Math.Max(0, targets.Length - request.MaxRows),
                bounds = bounds.HasBounds ? new
                {
                    source = bounds.BoundsSource,
                    world = ToBoundsObject(bounds.Bounds),
                    rendererCount = bounds.RendererCount,
                    visibleRendererCount = bounds.VisibleRendererCount,
                    disabledRendererCount = bounds.DisabledRendererCount,
                    spriteRendererCount = bounds.SpriteRendererCount,
                    missingSpriteCount = bounds.MissingSpriteCount,
                    missingSprites = bounds.MissingSprites,
                    colliderCount = bounds.ColliderCount,
                    visibleColliderCount = bounds.VisibleColliderCount,
                    sortingSamples = bounds.SortingSamples
                } : null,
                camera = camera == null ? null : new
                {
                    name = camera.name,
                    path = cameraLabel,
                    active = camera.gameObject.activeInHierarchy && camera.enabled,
                    orthographic = camera.orthographic,
                    orthographicSize = camera.orthographicSize,
                    fieldOfView = camera.fieldOfView,
                    nearClipPlane = camera.nearClipPlane,
                    farClipPlane = camera.farClipPlane,
                    position = ToVector3Object(camera.transform.position),
                    rotationEuler = ToVector3Object(camera.transform.eulerAngles),
                    aspect = request.AspectRatio
                },
                coverage = metrics == null ? null : BuildCoverageObject(metrics, request),
                suggested = suggestion,
                screenshot,
                warnings = BuildWarnings(targets, camera, bounds, missingTypes)
            };
        }

        static object BuildCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            JArray targetRows = root["targetRows"] as JArray ?? new JArray();
            if (targetRows.Count > 20)
            {
                root["targetRows"] = new JArray(targetRows.Take(20));
                root["compactOmittedTargetRowCount"] = targetRows.Count - 20;
            }

            JArray sortingSamples = root["bounds"]?["sortingSamples"] as JArray;
            if (sortingSamples != null && sortingSamples.Count > 20)
            {
                root["bounds"]["sortingSamples"] = new JArray(sortingSamples.Take(20));
                root["bounds"]["compactOmittedSortingSampleCount"] = sortingSamples.Count - 20;
            }

            JArray missingSprites = root["bounds"]?["missingSprites"] as JArray;
            if (missingSprites != null && missingSprites.Count > 20)
            {
                root["bounds"]["missingSprites"] = new JArray(missingSprites.Take(20));
                root["bounds"]["compactOmittedMissingSpriteCount"] = missingSprites.Count - 20;
            }

            return root;
        }

        static GameObject[] ResolveTargets(CameraFitRequest request, out string[] missingTypes, out object rootData)
        {
            var inactiveMode = request.IncludeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
            var allObjects = UnityApiAdapter.FindObjectsByType<GameObject>(inactiveMode);
            var resolvedTypes = ResolveComponentTypes(request.ComponentTypes, out string[] resolvedMissingTypes);
            missingTypes = resolvedMissingTypes;
            bool matchAny = string.Equals(request.ComponentMatch, "any", StringComparison.OrdinalIgnoreCase);
            GameObject rootObject = ResolveRoot(request);
            rootData = rootObject == null ? null : new
            {
                name = rootObject.name,
                path = UiDiagnosticsHelper.GetHierarchyPath(rootObject.transform),
                objectId = UnityApiAdapter.GetObjectIdOrZero(rootObject),
                activeSelf = rootObject.activeSelf,
                activeInHierarchy = rootObject.activeInHierarchy
            };

            IEnumerable<GameObject> candidates;
            if (!string.IsNullOrWhiteSpace(request.Target))
            {
                var findParams = new JObject
                {
                    ["search_inactive"] = request.IncludeInactive
                };
                bool findAll = !IsIdLookup(request.Target, request.SearchMethod);
                candidates = ObjectsHelper.FindObjects(new JValue(request.Target), request.SearchMethod, findAll, findParams);
            }
            else
            {
                candidates = allObjects;
            }

            return candidates
                .Where(go => go != null)
                .Where(go => request.IncludeInactive || go.activeInHierarchy)
                .Where(go => MatchesScene(go, request.Scene))
                .Where(go => MatchesRoot(go, rootObject))
                .Where(go => MatchesName(go, request.NamePrefix, request.NameExact))
                .Where(go => MatchesComponents(go, resolvedTypes, request.ComponentTypes.Length, resolvedMissingTypes.Length, matchAny))
                .GroupBy(go => go.GetInstanceID())
                .Select(group => group.First())
                .OrderBy(go => UiDiagnosticsHelper.GetHierarchyPath(go.transform), StringComparer.Ordinal)
                .ToArray();
        }

        static Dictionary<string, Type> ResolveComponentTypes(string[] componentTypeNames, out string[] missingTypes)
        {
            var resolved = new Dictionary<string, Type>(StringComparer.Ordinal);
            var missing = new List<string>();
            foreach (string componentTypeName in componentTypeNames ?? Array.Empty<string>())
            {
                if (UnityComponentResolver.TryResolve(componentTypeName, out Type type, out _))
                    resolved[componentTypeName] = type;
                else
                    missing.Add(componentTypeName);
            }

            missingTypes = missing.ToArray();
            return resolved;
        }

        static GameObject ResolveRoot(CameraFitRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Root))
                return null;

            var findParams = new JObject
            {
                ["search_inactive"] = request.IncludeInactive
            };
            return ObjectsHelper.FindObject(new JValue(request.Root), request.RootSearchMethod, findParams);
        }

        static bool MatchesScene(GameObject gameObject, string scene)
        {
            return string.IsNullOrWhiteSpace(scene) ||
                string.Equals(gameObject.scene.name, scene, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(gameObject.scene.path, scene, StringComparison.OrdinalIgnoreCase);
        }

        static bool MatchesRoot(GameObject gameObject, GameObject rootObject)
        {
            return rootObject == null ||
                gameObject.transform == rootObject.transform ||
                gameObject.transform.IsChildOf(rootObject.transform);
        }

        static bool MatchesName(GameObject gameObject, string namePrefix, string nameExact)
        {
            if (!string.IsNullOrWhiteSpace(nameExact) &&
                !string.Equals(gameObject.name, nameExact, StringComparison.Ordinal))
                return false;

            return string.IsNullOrWhiteSpace(namePrefix) ||
                gameObject.name.StartsWith(namePrefix, StringComparison.Ordinal);
        }

        static bool MatchesComponents(GameObject gameObject, IReadOnlyDictionary<string, Type> componentTypes, int requestedTypeCount, int missingTypeCount, bool matchAny)
        {
            if (requestedTypeCount <= 0)
                return true;

            if (componentTypes == null || componentTypes.Count == 0)
                return false;

            if (!matchAny && missingTypeCount > 0)
                return false;

            var components = gameObject.GetComponents<Component>();
            return matchAny
                ? componentTypes.Values.Any(type => components.Any(component => component != null && type.IsInstanceOfType(component)))
                : componentTypes.Values.All(type => components.Any(component => component != null && type.IsInstanceOfType(component)));
        }

        static BoundsAnalysis AnalyzeBounds(GameObject[] targets, bool includeInactive)
        {
            var analysis = new BoundsAnalysis();
            var missingSprites = new List<object>();
            var sortingSamples = new List<object>();
            var seenRenderers = new HashSet<int>();
            var seenColliders = new HashSet<int>();
            bool hasVisualBounds = false;
            bool hasColliderBounds = false;
            Bounds visualBounds = default;
            Bounds colliderBounds = default;

            foreach (GameObject target in targets ?? Array.Empty<GameObject>())
            {
                foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(includeInactive))
                {
                    if (renderer == null || !seenRenderers.Add(renderer.GetInstanceID()))
                        continue;

                    analysis.RendererCount++;
                    bool visible = renderer.enabled && (includeInactive || renderer.gameObject.activeInHierarchy);
                    if (visible)
                    {
                        analysis.VisibleRendererCount++;
                        if (!hasVisualBounds)
                        {
                            visualBounds = renderer.bounds;
                            hasVisualBounds = true;
                        }
                        else
                        {
                            visualBounds.Encapsulate(renderer.bounds);
                        }
                    }
                    else
                    {
                        analysis.DisabledRendererCount++;
                    }

                    if (renderer is SpriteRenderer spriteRenderer)
                    {
                        analysis.SpriteRendererCount++;
                        if (spriteRenderer.sprite == null)
                        {
                            analysis.MissingSpriteCount++;
                            if (missingSprites.Count < 50)
                            {
                                missingSprites.Add(new
                                {
                                    path = UiDiagnosticsHelper.GetHierarchyPath(spriteRenderer.transform),
                                    enabled = spriteRenderer.enabled,
                                    activeInHierarchy = spriteRenderer.gameObject.activeInHierarchy
                                });
                            }
                        }

                        if (sortingSamples.Count < 50)
                        {
                            sortingSamples.Add(new
                            {
                                path = UiDiagnosticsHelper.GetHierarchyPath(spriteRenderer.transform),
                                sortingLayerName = spriteRenderer.sortingLayerName,
                                sortingOrder = spriteRenderer.sortingOrder,
                                spriteName = spriteRenderer.sprite != null ? spriteRenderer.sprite.name : null,
                                enabled = spriteRenderer.enabled
                            });
                        }
                    }
                }

                foreach (Collider collider in target.GetComponentsInChildren<Collider>(includeInactive))
                {
                    if (collider == null || !seenColliders.Add(collider.GetInstanceID()))
                        continue;

                    analysis.ColliderCount++;
                    if (!collider.enabled || (!includeInactive && !collider.gameObject.activeInHierarchy))
                        continue;

                    analysis.VisibleColliderCount++;
                    if (!hasColliderBounds)
                    {
                        colliderBounds = collider.bounds;
                        hasColliderBounds = true;
                    }
                    else
                    {
                        colliderBounds.Encapsulate(collider.bounds);
                    }
                }

                foreach (Collider2D collider in target.GetComponentsInChildren<Collider2D>(includeInactive))
                {
                    if (collider == null || !seenColliders.Add(collider.GetInstanceID()))
                        continue;

                    analysis.ColliderCount++;
                    if (!collider.enabled || (!includeInactive && !collider.gameObject.activeInHierarchy))
                        continue;

                    analysis.VisibleColliderCount++;
                    if (!hasColliderBounds)
                    {
                        colliderBounds = collider.bounds;
                        hasColliderBounds = true;
                    }
                    else
                    {
                        colliderBounds.Encapsulate(collider.bounds);
                    }
                }
            }

            if (hasVisualBounds)
            {
                analysis.HasBounds = true;
                analysis.Bounds = visualBounds;
                analysis.BoundsSource = "renderers";
            }
            else if (hasColliderBounds)
            {
                analysis.HasBounds = true;
                analysis.Bounds = colliderBounds;
                analysis.BoundsSource = "colliders";
            }
            else if (targets != null && targets.Length > 0)
            {
                Bounds transformBounds = new(targets[0].transform.position, Vector3.zero);
                for (int i = 1; i < targets.Length; i++)
                    transformBounds.Encapsulate(targets[i].transform.position);

                analysis.HasBounds = true;
                analysis.Bounds = transformBounds;
                analysis.BoundsSource = "transforms";
            }

            analysis.MissingSprites = missingSprites.ToArray();
            analysis.SortingSamples = sortingSamples.ToArray();
            return analysis;
        }

        static Camera ResolveCamera(string cameraTarget, string searchMethod, bool includeInactive, out string label)
        {
            label = string.Empty;
            if (!string.IsNullOrWhiteSpace(cameraTarget))
            {
                var findParams = new JObject
                {
                    ["search_inactive"] = includeInactive
                };
                GameObject cameraObject = ObjectsHelper.FindObject(new JValue(cameraTarget), searchMethod, findParams);
                Camera camera = cameraObject != null ? cameraObject.GetComponentInChildren<Camera>(true) : null;
                if (camera != null)
                {
                    label = UiDiagnosticsHelper.GetHierarchyPath(camera.transform);
                    return camera;
                }
            }

            if (Camera.main != null)
            {
                label = UiDiagnosticsHelper.GetHierarchyPath(Camera.main.transform);
                return Camera.main;
            }

            Camera[] cameras = UnityApiAdapter.FindObjectsByType<Camera>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
            Camera enabled = cameras.FirstOrDefault(camera => camera != null && camera.enabled);
            Camera resolved = enabled ?? cameras.FirstOrDefault(camera => camera != null);
            if (resolved != null)
                label = UiDiagnosticsHelper.GetHierarchyPath(resolved.transform);

            return resolved;
        }

        static CoverageMetrics MeasureCoverage(Camera camera, Bounds bounds, float aspect, int viewportWidth, int viewportHeight, float desiredMin, float desiredMax)
        {
            CameraPlaneProjection plane = ProjectToCameraPlane(camera, bounds);
            Rect screenRect = default;
            bool hasScreenRect = camera.orthographic
                ? TryProjectOrthographic(camera, plane, aspect, viewportWidth, viewportHeight, out screenRect)
                : TryProjectPerspective(camera, bounds, aspect, viewportWidth, viewportHeight, out screenRect);

            float widthCoverage = hasScreenRect ? screenRect.width / viewportWidth : 0f;
            float heightCoverage = hasScreenRect ? screenRect.height / viewportHeight : 0f;
            float coverage = Mathf.Max(widthCoverage, heightCoverage);
            bool intersects = hasScreenRect && screenRect.Overlaps(new Rect(0f, 0f, viewportWidth, viewportHeight));
            bool depthInClip = plane.DepthMax >= camera.nearClipPlane && plane.DepthMin <= camera.farClipPlane;

            return new CoverageMetrics
            {
                HasScreenRect = hasScreenRect,
                ScreenRect = screenRect,
                WidthCoverage = widthCoverage,
                HeightCoverage = heightCoverage,
                Coverage = coverage,
                InDesiredRange = coverage >= desiredMin && coverage <= desiredMax,
                IntersectsViewport = intersects,
                DepthInClipRange = depthInClip,
                Plane = plane
            };
        }

        static CameraPlaneProjection ProjectToCameraPlane(Camera camera, Bounds bounds)
        {
            Vector3[] corners = GetBoundsCorners(bounds);
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float minZ = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            float maxZ = float.MinValue;

            foreach (Vector3 corner in corners)
            {
                Vector3 local = camera.transform.InverseTransformPoint(corner);
                minX = Mathf.Min(minX, local.x);
                maxX = Mathf.Max(maxX, local.x);
                minY = Mathf.Min(minY, local.y);
                maxY = Mathf.Max(maxY, local.y);
                minZ = Mathf.Min(minZ, local.z);
                maxZ = Mathf.Max(maxZ, local.z);
            }

            return new CameraPlaneProjection
            {
                XMin = minX,
                XMax = maxX,
                YMin = minY,
                YMax = maxY,
                Width = Mathf.Max(0f, maxX - minX),
                Height = Mathf.Max(0f, maxY - minY),
                DepthMin = minZ,
                DepthMax = maxZ,
                DepthCenter = (minZ + maxZ) * 0.5f,
                DepthSpan = Mathf.Max(0f, maxZ - minZ)
            };
        }

        static bool TryProjectOrthographic(Camera camera, CameraPlaneProjection plane, float aspect, int viewportWidth, int viewportHeight, out Rect rect)
        {
            float halfHeight = Mathf.Max(0.001f, camera.orthographicSize);
            float halfWidth = halfHeight * aspect;
            float xMin = ((plane.XMin / halfWidth) * 0.5f + 0.5f) * viewportWidth;
            float xMax = ((plane.XMax / halfWidth) * 0.5f + 0.5f) * viewportWidth;
            float yMin = ((plane.YMin / halfHeight) * 0.5f + 0.5f) * viewportHeight;
            float yMax = ((plane.YMax / halfHeight) * 0.5f + 0.5f) * viewportHeight;
            rect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            return true;
        }

        static bool TryProjectPerspective(Camera camera, Bounds bounds, float aspect, int viewportWidth, int viewportHeight, out Rect rect)
        {
            Vector3[] corners = GetBoundsCorners(bounds);
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            bool valid = false;
            float verticalTan = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);

            foreach (Vector3 corner in corners)
            {
                Vector3 local = camera.transform.InverseTransformPoint(corner);
                if (local.z <= Mathf.Max(0.001f, camera.nearClipPlane * 0.1f))
                    continue;

                float xNdc = local.x / (local.z * verticalTan * aspect);
                float yNdc = local.y / (local.z * verticalTan);
                float x = (xNdc * 0.5f + 0.5f) * viewportWidth;
                float y = (yNdc * 0.5f + 0.5f) * viewportHeight;
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
                valid = true;
            }

            rect = valid ? Rect.MinMaxRect(minX, minY, maxX, maxY) : default;
            return valid;
        }

        static object BuildCoverageObject(CoverageMetrics metrics, CameraFitRequest request)
        {
            return new
            {
                currentCoveragePercent = metrics.Coverage * 100f,
                widthCoveragePercent = metrics.WidthCoverage * 100f,
                heightCoveragePercent = metrics.HeightCoverage * 100f,
                desiredCoverageRangePercent = new
                {
                    min = request.DesiredCoverageMin * 100f,
                    max = request.DesiredCoverageMax * 100f
                },
                inDesiredRange = metrics.InDesiredRange,
                viewport = new
                {
                    width = request.ViewportWidth,
                    height = request.ViewportHeight,
                    aspectRatio = request.AspectRatio
                },
                screenRect = metrics.HasScreenRect ? ToRectObject(metrics.ScreenRect) : null,
                intersectsViewport = metrics.IntersectsViewport,
                depthInClipRange = metrics.DepthInClipRange,
                cameraPlaneBounds = new
                {
                    xMin = metrics.Plane.XMin,
                    xMax = metrics.Plane.XMax,
                    yMin = metrics.Plane.YMin,
                    yMax = metrics.Plane.YMax,
                    width = metrics.Plane.Width,
                    height = metrics.Plane.Height,
                    depthMin = metrics.Plane.DepthMin,
                    depthMax = metrics.Plane.DepthMax,
                    depthCenter = metrics.Plane.DepthCenter,
                    depthSpan = metrics.Plane.DepthSpan
                }
            };
        }

        static object BuildSuggestion(Camera camera, Bounds bounds, CameraPlaneProjection plane, float aspect, float desiredCoverage)
        {
            desiredCoverage = Mathf.Clamp(desiredCoverage <= 0.001f ? 0.6f : desiredCoverage, 0.05f, 0.95f);
            float planeWidth = Mathf.Max(0.001f, plane.Width);
            float planeHeight = Mathf.Max(0.001f, plane.Height);
            float orthographicSize = Mathf.Max(
                planeHeight / (2f * desiredCoverage),
                planeWidth / (2f * desiredCoverage * aspect));

            float depth = Vector3.Dot(bounds.center - camera.transform.position, camera.transform.forward);
            if (depth <= camera.nearClipPlane)
            {
                depth = Mathf.Max(camera.nearClipPlane + plane.DepthSpan + 1f, Mathf.Max(bounds.extents.magnitude * 2f, 1f));
            }

            float verticalTan = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
            float perspectiveDistance = Mathf.Max(
                planeHeight / (2f * desiredCoverage * verticalTan),
                planeWidth / (2f * desiredCoverage * aspect * verticalTan));
            perspectiveDistance = Mathf.Max(perspectiveDistance + plane.DepthSpan * 0.5f, camera.nearClipPlane + plane.DepthSpan + 0.01f);

            Vector3 orthographicPosition = bounds.center - camera.transform.forward * depth;
            Vector3 perspectivePosition = bounds.center - camera.transform.forward * perspectiveDistance;
            Vector3 suggestedPosition = camera.orthographic ? orthographicPosition : perspectivePosition;

            return new
            {
                targetCoveragePercent = desiredCoverage * 100f,
                orthographicSize,
                perspectiveDistance,
                cameraPosition = ToVector3Object(suggestedPosition),
                orthographicCameraPosition = ToVector3Object(orthographicPosition),
                perspectiveCameraPosition = ToVector3Object(perspectivePosition),
                lookAt = ToVector3Object(bounds.center)
            };
        }

        static object CaptureScreenshot(Camera camera, CameraFitRequest request)
        {
            string outputPath = ResolveOutputPath(request.OutputPath, "camera_fit");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            var previousTarget = camera.targetTexture;
            float previousAspect = camera.aspect;
            var renderTexture = new RenderTexture(request.ScreenshotWidth, request.ScreenshotHeight, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(request.ScreenshotWidth, request.ScreenshotHeight, TextureFormat.RGB24, mipChain: false);

            try
            {
                camera.targetTexture = renderTexture;
                camera.aspect = request.AspectRatio;
                camera.Render();
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0, 0, request.ScreenshotWidth, request.ScreenshotHeight), 0, 0);
                texture.Apply(updateMipmaps: false);
                File.WriteAllBytes(outputPath, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.aspect = previousAspect;
                RenderTexture.active = null;
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
                UnityEngine.Object.DestroyImmediate(texture);
            }

            string projectRoot = ResourceUriHelper.ResolveProjectRoot(null);
            string relative = ResourceMutationTools.ToProjectRelativePath(projectRoot, outputPath);
            var info = new FileInfo(outputPath);
            return new
            {
                path = relative,
                uri = $"unity://path/{relative}",
                width = request.ScreenshotWidth,
                height = request.ScreenshotHeight,
                bytes = info.Exists ? info.Length : 0
            };
        }

        static string ResolveOutputPath(string requested, string suffix)
        {
            string projectRoot = ResourceUriHelper.ResolveProjectRoot(null);
            string normalizedSuffix = string.IsNullOrWhiteSpace(suffix) ? "camera_fit" : suffix.Replace(' ', '_').ToLowerInvariant();
            string relative = string.IsNullOrWhiteSpace(requested)
                ? $"Temp/LensCaptures/{DateTime.UtcNow:yyyyMMdd-HHmmss}-{normalizedSuffix}.png"
                : requested.Replace('\\', '/');

            if (relative.StartsWith("unity://path/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                string resolved = ResourceMutationTools.ResolveSafePath(relative, projectRoot);
                if (resolved == null)
                    throw new InvalidOperationException("outputPath could not be resolved under the project root.");

                return resolved;
            }

            if (Path.IsPathRooted(relative))
                throw new InvalidOperationException("outputPath must be relative to the Unity project root.");

            if (!relative.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                relative = $"{relative.TrimEnd('/')}/{normalizedSuffix}.png";

            string fullPath = Path.GetFullPath(Path.Combine(projectRoot, relative));
            if (!ResourceUriHelper.IsPathUnderProject(fullPath, projectRoot))
                throw new InvalidOperationException("outputPath must stay under the Unity project root.");

            return fullPath;
        }

        static object[] BuildWarnings(GameObject[] targets, Camera camera, BoundsAnalysis bounds, string[] missingTypes)
        {
            var warnings = new List<object>();
            if (targets == null || targets.Length == 0)
                warnings.Add(new { code = "no_targets", message = "No target GameObjects matched the target/query inputs." });
            if (camera == null)
                warnings.Add(new { code = "no_camera", message = "No camera could be resolved for coverage measurement." });
            if (bounds == null || !bounds.HasBounds)
                warnings.Add(new { code = "no_bounds", message = "Targets did not provide renderer, collider, or transform bounds." });
            if (missingTypes != null && missingTypes.Length > 0)
                warnings.Add(new { code = "missing_component_types", message = "Some component type filters could not be resolved.", types = missingTypes });
            if (bounds != null && bounds.MissingSpriteCount > 0)
                warnings.Add(new { code = "missing_sprites", message = $"{bounds.MissingSpriteCount} SpriteRenderer component(s) have no sprite assigned." });

            return warnings.ToArray();
        }

        static object BuildTargetRow(GameObject gameObject)
        {
            Renderer[] renderers = gameObject.GetComponentsInChildren<Renderer>(true);
            return new
            {
                path = UiDiagnosticsHelper.GetHierarchyPath(gameObject.transform),
                name = gameObject.name,
                objectId = UnityApiAdapter.GetObjectIdOrZero(gameObject),
                activeSelf = gameObject.activeSelf,
                activeInHierarchy = gameObject.activeInHierarchy,
                sceneName = gameObject.scene.name,
                scenePath = gameObject.scene.path,
                rendererCount = renderers.Length,
                spriteRendererCount = renderers.OfType<SpriteRenderer>().Count()
            };
        }

        static bool IsIdLookup(string value, string searchMethod)
        {
            return int.TryParse(value, out _) ||
                string.Equals(searchMethod, "by_id", StringComparison.OrdinalIgnoreCase);
        }

        static Vector3[] GetBoundsCorners(Bounds bounds)
        {
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            return new[]
            {
                center + new Vector3(-extents.x, -extents.y, -extents.z),
                center + new Vector3(-extents.x, -extents.y, extents.z),
                center + new Vector3(-extents.x, extents.y, -extents.z),
                center + new Vector3(-extents.x, extents.y, extents.z),
                center + new Vector3(extents.x, -extents.y, -extents.z),
                center + new Vector3(extents.x, -extents.y, extents.z),
                center + new Vector3(extents.x, extents.y, -extents.z),
                center + new Vector3(extents.x, extents.y, extents.z)
            };
        }

        static JToken GetToken(JObject parameters, params string[] names)
        {
            if (parameters == null)
                return null;

            foreach (string name in names)
            {
                if (parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken value))
                    return value;
            }

            return null;
        }

        static string GetString(JObject parameters, params string[] names) => GetToken(parameters, names)?.Value<string>();

        static string[] GetStringArray(JObject parameters, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            return token is JArray array
                ? array.Select(item => item?.Value<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray()
                : Array.Empty<string>();
        }

        static int GetInt(JObject parameters, int fallback, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<int>();
        }

        static float GetFloat(JObject parameters, float fallback, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<float>();
        }

        static bool GetBool(JObject parameters, bool fallback, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<bool>();
        }

        static float Clamp01(float value) => Mathf.Clamp(value, 0f, 1f);

        static object ToVector3Object(Vector3 value) => new { x = value.x, y = value.y, z = value.z };

        static object ToBoundsObject(Bounds bounds)
        {
            return new
            {
                center = ToVector3Object(bounds.center),
                size = ToVector3Object(bounds.size),
                extents = ToVector3Object(bounds.extents),
                min = ToVector3Object(bounds.min),
                max = ToVector3Object(bounds.max)
            };
        }

        static object ToRectObject(Rect rect)
        {
            return new
            {
                x = rect.x,
                y = rect.y,
                width = rect.width,
                height = rect.height,
                xMin = rect.xMin,
                xMax = rect.xMax,
                yMin = rect.yMin,
                yMax = rect.yMax
            };
        }
    }
}
