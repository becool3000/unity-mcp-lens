#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class UiPrefabLayoutMatrixTools
    {
        const string ToolName = "Unity.UI.VerifyPrefabLayoutMatrix";
        const int DefaultMaxElements = 1000;
        const int MaxElementLimit = 5000;
        const int DefaultMaxFindings = 200;
        const int MaxFindingLimit = 2000;
        const float BoundsTolerance = 0.5f;

        sealed class Request
        {
            public string PrefabPath;
            public ResolutionRequest[] Resolutions = Array.Empty<ResolutionRequest>();
            public StateRequest[] States = Array.Empty<StateRequest>();
            public bool IncludeInactive = true;
            public int MaxElements = DefaultMaxElements;
            public int MaxFindings = DefaultMaxFindings;
            public CheckOptions Checks = new();
        }

        sealed class ResolutionRequest
        {
            public string key;
            public int width;
            public int height;
        }

        sealed class StateRequest
        {
            public string name;
            public TemporaryActivationRequest[] temporaryActivations = Array.Empty<TemporaryActivationRequest>();
        }

        sealed class TemporaryActivationRequest
        {
            public string target;
            public string targetPath;
            public string searchMethod = "by_name";
            public bool includeInactive = true;
            public bool active = true;
        }

        sealed class CheckOptions
        {
            public bool BoundsWithinCanvas = true;
            public bool TextOverflow = true;
            public bool ZeroOrNegativeSize = true;
        }

        sealed class FindingRow
        {
            public int index;
            public string severity;
            public string kind;
            public string message;
            public string prefabPath;
            public string state;
            public string resolutionKey;
            public object requestedResolution;
            public string hierarchyPath;
            public string componentType;
            public string target;
            public string targetPath;
            public object rect;
            public object canvasRect;
            public object overflow;
            public string detail;
        }

        sealed class ElementRow
        {
            public int index;
            public string state;
            public string resolutionKey;
            public string hierarchyPath;
            public string name;
            public bool activeSelf;
            public bool activeInHierarchy;
            public bool measured;
            public string[] componentTypes;
            public object rect;
            public object bounds;
            public string textPreview;
        }

        sealed class MatrixRow
        {
            public string state;
            public string resolutionKey;
            public object requestedResolution;
            public bool passed;
            public int elementCount;
            public int measuredElementCount;
            public int findingCount;
            public int activationFailureCount;
            public bool elementTruncated;
            public object canvas;
            public List<object> temporaryActivations = new();
        }

        sealed class ExecutionContext
        {
            public Request Request;
            public string PrefabGuid;
            public Transform RootTransform;
            public List<FindingRow> Findings = new();
            public List<ElementRow> Elements = new();
            public List<MatrixRow> Matrix = new();
            public Dictionary<string, int> SeverityCounts = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, int> KindCounts = new(StringComparer.OrdinalIgnoreCase);
            public int TotalFindingCount;
            public bool Truncated;
        }

        sealed class ActivationRestore
        {
            public TemporaryActivationRequest Request;
            public GameObject TargetObject;
            public bool OriginalActive;
            public bool Found => TargetObject != null;
        }

        [McpSchema(ToolName)]
        public static object GetSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    prefabPath = new { type = "string", description = "Prefab asset path under Assets, ending in .prefab." },
                    resolutions = new
                    {
                        type = "array",
                        description = "Preview canvas resolutions. Defaults to 1920x1080 and 1366x768.",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                key = new { type = "string", description = "Stable key for this resolution." },
                                width = new { type = "integer", description = "Preview canvas width in pixels." },
                                height = new { type = "integer", description = "Preview canvas height in pixels." }
                            },
                            required = new[] { "width", "height" }
                        }
                    },
                    states = new
                    {
                        type = "array",
                        description = "Named prefab UI states to evaluate.",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                name = new { type = "string", description = "Stable state name." },
                                temporaryActivations = BuildTemporaryActivationsSchema()
                            }
                        }
                    },
                    temporaryActivations = BuildTemporaryActivationsSchema(),
                    includeInactive = new { type = "boolean", description = "Include inactive RectTransforms in element rows. Active layout checks still measure active hierarchy only. Defaults to true." },
                    maxElements = new { type = "integer", description = "Maximum element rows to keep. Defaults to 1000 and is clamped to 1..5000." },
                    maxFindings = new { type = "integer", description = "Maximum finding rows to keep inline/full-result payload. Defaults to 200 and is clamped to 1..2000." },
                    checks = new
                    {
                        type = "object",
                        description = "Layout checks to run. All default to true.",
                        properties = new
                        {
                            boundsWithinCanvas = new { type = "boolean" },
                            textOverflow = new { type = "boolean" },
                            zeroOrNegativeSize = new { type = "boolean" }
                        }
                    }
                },
                required = new[] { "prefabPath" }
            };
        }

        [McpTool(ToolName,
            "Loads a UI prefab into an isolated preview canvas and verifies layout across named UI states and resolutions without saving or mutating assets.",
            "Verify UI Prefab Layout Matrix",
            Groups = new[] { "ui", "assets" },
            EnabledByDefault = true)]
        public static object VerifyPrefabLayoutMatrix(JObject @params)
        {
            @params ??= new JObject();
            var timing = new ToolOperationTiming(ToolName, "ui_verify_prefab_layout_matrix", PayloadBudgeting.GetUtf8ByteCount(@params.ToString(Formatting.None)));
            bool success = false;
            string errorKind = null;
            object data = null;
            string message = null;

            try
            {
                Request request;
                using (timing.Measure("normalization"))
                {
                    request = Normalize(@params);
                    if (!TryValidatePrefabPath(request.PrefabPath, out string errorMessage, out object errorData))
                    {
                        errorKind = "INVALID_PREFAB_PATH";
                        data = errorData;
                        message = errorMessage;
                        return Response.Error(errorMessage, errorData);
                    }
                }

                using (timing.Measure("service"))
                {
                    data = Execute(request);
                    var shaped = JObject.FromObject(data);
                    int findingCount = shaped.Value<int?>("findingCount") ?? 0;
                    int resolutionCount = shaped.Value<int?>("resolutionCount") ?? 0;
                    int stateCount = shaped.Value<int?>("stateCount") ?? 0;
                    success = true;
                    message = findingCount == 0
                        ? $"UI prefab layout matrix passed for {stateCount} state(s) across {resolutionCount} resolution(s)."
                        : $"UI prefab layout matrix found {findingCount} issue(s) across {stateCount} state(s) and {resolutionCount} resolution(s).";
                }
            }
            catch (Exception ex)
            {
                success = false;
                errorKind = ex.GetType().Name;
                message = $"UI prefab layout matrix failed: {ex.Message}";
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
                        new { kind = "ui_prefab_layout_matrix_full_result" },
                        "ui_prefab_layout_matrix",
                        detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes))
                    : Response.Error(message ?? "UI prefab layout matrix failed.", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            return response;
        }

        static object Execute(Request request)
        {
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(request.PrefabPath);
            bool prefabDirtyBefore = prefabAsset != null && EditorUtility.IsDirty(prefabAsset);
            object sceneDirtyBefore = SceneDirtyStateUtility.CaptureLoadedScenes();
            var context = new ExecutionContext
            {
                Request = request,
                PrefabGuid = AssetDatabase.AssetPathToGUID(request.PrefabPath)
            };

            GameObject root = null;
            GameObject wrapper = null;
            Transform originalParent = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(request.PrefabPath);
                originalParent = root != null ? root.transform.parent : null;
                context.RootTransform = root != null ? root.transform : null;

                foreach (ResolutionRequest resolution in request.Resolutions)
                {
                    if (resolution == null || resolution.width <= 0 || resolution.height <= 0)
                    {
                        AddFinding(context, null, "error", "measurement_failed", "Each resolution requires positive width and height.", null, resolution);
                        continue;
                    }

                    string resolutionKey = string.IsNullOrWhiteSpace(resolution.key)
                        ? $"{resolution.width}x{resolution.height}"
                        : resolution.key.Trim();
                    if (!TryPrepareCanvas(root, resolution, ref wrapper, out RectTransform canvasRect, out object canvasData, out string canvasError))
                    {
                        foreach (StateRequest state in request.States)
                        {
                            var row = CreateMatrixRow(state, resolution, canvasData);
                            row.passed = false;
                            AddFinding(context, row, "error", "measurement_failed", canvasError, null, resolution);
                            context.Matrix.Add(row);
                        }

                        continue;
                    }

                    foreach (StateRequest state in request.States)
                    {
                        var row = CreateMatrixRow(state, resolution, canvasData);
                        var restores = ApplyTemporaryActivations(context, root, state, row, resolution);
                        try
                        {
                            ForceLayout(root, canvasRect);
                            MeasureState(context, root, canvasRect, state, resolution, row);
                        }
                        catch (Exception ex)
                        {
                            AddFinding(context, row, "error", "measurement_failed", ex.Message, null, resolution);
                        }
                        finally
                        {
                            RestoreTemporaryActivations(restores, row);
                        }

                        row.passed = row.findingCount == 0;
                        context.Matrix.Add(row);
                    }
                }
            }
            finally
            {
                if (root != null)
                {
                    if (root.transform.parent != originalParent)
                        root.transform.SetParent(originalParent, false);

                    if (wrapper != null)
                        Object.DestroyImmediate(wrapper);

                    PrefabUtility.UnloadPrefabContents(root);
                }
                else if (wrapper != null)
                {
                    Object.DestroyImmediate(wrapper);
                }
            }

            bool prefabDirtyAfter = prefabAsset != null && EditorUtility.IsDirty(prefabAsset);
            object sceneDirtyAfter = SceneDirtyStateUtility.CaptureLoadedScenes();
            bool passed = context.TotalFindingCount == 0;
            return new
            {
                status = passed ? "passed" : "findings",
                passed,
                readOnly = true,
                prefabPath = request.PrefabPath,
                prefabGuid = context.PrefabGuid,
                resolutionCount = request.Resolutions.Length,
                stateCount = request.States.Length,
                elementCount = context.Matrix.Sum(row => row.elementCount),
                returnedElementCount = context.Elements.Count,
                findingCount = context.TotalFindingCount,
                returnedFindingCount = context.Findings.Count,
                severityCounts = context.SeverityCounts,
                kindCounts = context.KindCounts,
                truncated = context.Truncated,
                policy = new
                {
                    includeInactive = request.IncludeInactive,
                    maxElements = request.MaxElements,
                    maxFindings = request.MaxFindings,
                    checks = new
                    {
                        boundsWithinCanvas = request.Checks.BoundsWithinCanvas,
                        textOverflow = request.Checks.TextOverflow,
                        zeroOrNegativeSize = request.Checks.ZeroOrNegativeSize
                    }
                },
                matrix = context.Matrix.ToArray(),
                findings = context.Findings.ToArray(),
                elements = context.Elements.ToArray(),
                saveState = BuildReadOnlySaveState(),
                dirtyEvidence = new
                {
                    prefabAssetDirtyBefore = prefabDirtyBefore,
                    prefabAssetDirtyAfter = prefabDirtyAfter,
                    sceneDirtyBefore,
                    sceneDirtyAfter
                }
            };
        }

        static void MeasureState(ExecutionContext context, GameObject root, RectTransform canvasRect, StateRequest state, ResolutionRequest resolution, MatrixRow row)
        {
            Rect canvasLocalRect = canvasRect.rect;
            RectTransform[] rectTransforms = root.GetComponentsInChildren<RectTransform>(context.Request.IncludeInactive);
            int elementIndex = 0;
            foreach (RectTransform rectTransform in rectTransforms)
            {
                if (rectTransform == null)
                    continue;

                row.elementCount++;
                bool active = rectTransform.gameObject.activeInHierarchy;
                bool measured = active;
                Bounds bounds = default;
                Rect rect = rectTransform.rect;
                if (measured)
                    bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(canvasRect, rectTransform);

                if (context.Elements.Count < context.Request.MaxElements)
                {
                    context.Elements.Add(new ElementRow
                    {
                        index = elementIndex,
                        state = state.name,
                        resolutionKey = row.resolutionKey,
                        hierarchyPath = GetRelativePath(root.transform, rectTransform),
                        name = rectTransform.name,
                        activeSelf = rectTransform.gameObject.activeSelf,
                        activeInHierarchy = active,
                        measured = measured,
                        componentTypes = GetComponentTypeNames(rectTransform.gameObject),
                        rect = ToRectObject(rect),
                        bounds = measured ? ToBoundsObject(bounds) : null,
                        textPreview = GetTextPreview(rectTransform)
                    });
                }
                else
                {
                    context.Truncated = true;
                    row.elementTruncated = true;
                }

                elementIndex++;
                if (!measured)
                    continue;

                row.measuredElementCount++;
                if (context.Request.Checks.ZeroOrNegativeSize && (rect.width <= 0 || rect.height <= 0))
                {
                    AddFinding(
                        context,
                        row,
                        "warning",
                        "zero_or_negative_size",
                        $"RectTransform '{GetRelativePath(root.transform, rectTransform)}' has non-positive size.",
                        rectTransform,
                        resolution,
                        rect: ToRectObject(rect));
                }

                if (context.Request.Checks.BoundsWithinCanvas && !BoundsInsideRect(bounds, canvasLocalRect))
                {
                    AddFinding(
                        context,
                        row,
                        "error",
                        "out_of_bounds",
                        $"RectTransform '{GetRelativePath(root.transform, rectTransform)}' extends outside the preview canvas.",
                        rectTransform,
                        resolution,
                        rect: ToBoundsObject(bounds),
                        canvasRect: ToRectObject(canvasLocalRect),
                        overflow: BuildOverflowObject(bounds, canvasLocalRect));
                }

                if (context.Request.Checks.TextOverflow)
                    CheckTextOverflow(context, row, root, rectTransform, rect, resolution);
            }
        }

        static void CheckTextOverflow(ExecutionContext context, MatrixRow row, GameObject root, RectTransform rectTransform, Rect rect, ResolutionRequest resolution)
        {
            Text text = rectTransform.GetComponent<Text>();
            if (text != null && text.enabled)
            {
                AddTextOverflowFindingIfNeeded(
                    context,
                    row,
                    root,
                    rectTransform,
                    typeof(Text).FullName,
                    text.text,
                    text.preferredWidth,
                    text.preferredHeight,
                    rect,
                    resolution);
            }

            foreach (Component component in rectTransform.GetComponents<Component>())
            {
                if (component == null)
                    continue;

                Type type = component.GetType();
                if (type.FullName == null || !type.FullName.StartsWith("TMPro.", StringComparison.Ordinal))
                    continue;

                PropertyInfo preferredWidthProperty = type.GetProperty("preferredWidth", BindingFlags.Public | BindingFlags.Instance);
                PropertyInfo preferredHeightProperty = type.GetProperty("preferredHeight", BindingFlags.Public | BindingFlags.Instance);
                if (preferredWidthProperty == null || preferredHeightProperty == null)
                    continue;

                if (component is Behaviour behaviour && !behaviour.enabled)
                    continue;

                float preferredWidth = ConvertToFloat(preferredWidthProperty.GetValue(component));
                float preferredHeight = ConvertToFloat(preferredHeightProperty.GetValue(component));
                string textValue = type.GetProperty("text", BindingFlags.Public | BindingFlags.Instance)?.GetValue(component)?.ToString();
                AddTextOverflowFindingIfNeeded(
                    context,
                    row,
                    root,
                    rectTransform,
                    type.FullName,
                    textValue,
                    preferredWidth,
                    preferredHeight,
                    rect,
                    resolution);
            }
        }

        static void AddTextOverflowFindingIfNeeded(
            ExecutionContext context,
            MatrixRow row,
            GameObject root,
            RectTransform rectTransform,
            string componentType,
            string text,
            float preferredWidth,
            float preferredHeight,
            Rect rect,
            ResolutionRequest resolution)
        {
            if (string.IsNullOrEmpty(text))
                return;

            bool horizontalOverflow = preferredWidth > rect.width + BoundsTolerance;
            bool verticalOverflow = preferredHeight > rect.height + BoundsTolerance;
            if (!horizontalOverflow && !verticalOverflow)
                return;

            AddFinding(
                context,
                row,
                "warning",
                "text_overflow",
                $"Text on '{GetRelativePath(root.transform, rectTransform)}' prefers {preferredWidth:0.##}x{preferredHeight:0.##} inside {rect.width:0.##}x{rect.height:0.##}.",
                rectTransform,
                resolution,
                componentType: componentType,
                rect: ToRectObject(rect),
                overflow: new
                {
                    preferredWidth,
                    preferredHeight,
                    rectWidth = rect.width,
                    rectHeight = rect.height,
                    horizontalOverflow,
                    verticalOverflow
                },
                detail: Truncate(text, 160));
        }

        static List<ActivationRestore> ApplyTemporaryActivations(ExecutionContext context, GameObject root, StateRequest state, MatrixRow row, ResolutionRequest resolution)
        {
            var restores = new List<ActivationRestore>();
            foreach (TemporaryActivationRequest activation in state.temporaryActivations ?? Array.Empty<TemporaryActivationRequest>())
            {
                if (!TryResolveActivationTarget(root, activation, out GameObject targetObject))
                {
                    row.activationFailureCount++;
                    row.temporaryActivations.Add(new
                    {
                        target = activation.target,
                        targetPath = activation.targetPath,
                        requestedActive = activation.active,
                        found = false,
                        restored = (bool?)null
                    });
                    AddFinding(
                        context,
                        row,
                        "error",
                        "activation_target_not_found",
                        "Temporary activation target was not found in the loaded prefab contents.",
                        null,
                        resolution,
                        target: activation.target,
                        targetPath: activation.targetPath);
                    restores.Add(new ActivationRestore { Request = activation });
                    continue;
                }

                var restore = new ActivationRestore
                {
                    Request = activation,
                    TargetObject = targetObject,
                    OriginalActive = targetObject.activeSelf
                };
                targetObject.SetActive(activation.active);
                row.temporaryActivations.Add(new
                {
                    target = activation.target,
                    targetPath = activation.targetPath,
                    requestedActive = activation.active,
                    found = true,
                    hierarchyPath = GetRelativePath(root.transform, targetObject.transform),
                    originalActive = restore.OriginalActive,
                    applied = targetObject.activeSelf == activation.active
                });
                restores.Add(restore);
            }

            return restores;
        }

        static void RestoreTemporaryActivations(List<ActivationRestore> restores, MatrixRow row)
        {
            foreach (ActivationRestore restore in restores)
            {
                if (!restore.Found)
                    continue;

                bool restored = false;
                string restoreError = null;
                try
                {
                    restore.TargetObject.SetActive(restore.OriginalActive);
                    restored = restore.TargetObject.activeSelf == restore.OriginalActive;
                }
                catch (Exception ex)
                {
                    restoreError = ex.Message;
                }

                row.temporaryActivations.Add(new
                {
                    target = restore.Request.target,
                    targetPath = restore.Request.targetPath,
                    restore = true,
                    found = true,
                    restored,
                    restoreError
                });
            }
        }

        static bool TryPrepareCanvas(GameObject root, ResolutionRequest resolution, ref GameObject wrapper, out RectTransform canvasRect, out object canvasData, out string error)
        {
            canvasRect = null;
            canvasData = null;
            error = null;
            if (root == null)
            {
                error = "Loaded prefab root is null.";
                return false;
            }

            Canvas rootCanvas = root.GetComponent<Canvas>();
            if (rootCanvas != null && root.transform is RectTransform rootRect)
            {
                rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                SetRectTransformSize(rootRect, resolution.width, resolution.height);
                canvasRect = rootRect;
                canvasData = BuildCanvasData(rootCanvas, canvasRect, resolution, createdWrapper: false);
                return true;
            }

            if (wrapper == null)
            {
                wrapper = new GameObject("LensPrefabLayoutPreviewCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                SceneManager.MoveGameObjectToScene(wrapper, root.scene);
                Canvas wrapperCanvas = wrapper.GetComponent<Canvas>();
                wrapperCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                wrapper.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            }

            canvasRect = wrapper.transform as RectTransform;
            SetRectTransformSize(canvasRect, resolution.width, resolution.height);
            if (root.transform.parent != canvasRect)
                root.transform.SetParent(canvasRect, false);

            canvasData = BuildCanvasData(wrapper.GetComponent<Canvas>(), canvasRect, resolution, createdWrapper: true);
            return true;
        }

        static void ForceLayout(GameObject root, RectTransform canvasRect)
        {
            Canvas.ForceUpdateCanvases();
            if (canvasRect != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(canvasRect);

            foreach (RectTransform rectTransform in root.GetComponentsInChildren<RectTransform>(true))
            {
                if (rectTransform != null && rectTransform.gameObject.activeInHierarchy)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
            }

            Canvas.ForceUpdateCanvases();
        }

        static MatrixRow CreateMatrixRow(StateRequest state, ResolutionRequest resolution, object canvasData)
        {
            string key = string.IsNullOrWhiteSpace(resolution?.key)
                ? $"{resolution?.width ?? 0}x{resolution?.height ?? 0}"
                : resolution.key.Trim();
            return new MatrixRow
            {
                state = state?.name ?? "base",
                resolutionKey = key,
                requestedResolution = BuildResolutionObject(resolution),
                canvas = canvasData,
                passed = true
            };
        }

        static void AddFinding(
            ExecutionContext context,
            MatrixRow row,
            string severity,
            string kind,
            string message,
            RectTransform rectTransform,
            ResolutionRequest resolution,
            string componentType = null,
            string target = null,
            string targetPath = null,
            object rect = null,
            object canvasRect = null,
            object overflow = null,
            string detail = null)
        {
            context.TotalFindingCount++;
            Increment(context.SeverityCounts, severity);
            Increment(context.KindCounts, kind);
            if (row != null)
                row.findingCount++;

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
                prefabPath = context.Request.PrefabPath,
                state = row?.state,
                resolutionKey = row?.resolutionKey,
                requestedResolution = row?.requestedResolution ?? BuildResolutionObject(resolution),
                hierarchyPath = rectTransform != null ? GetRelativePath(context.RootTransform, rectTransform) : null,
                componentType = componentType ?? rectTransform?.GetType().FullName,
                target = target,
                targetPath = targetPath,
                rect = rect,
                canvasRect = canvasRect,
                overflow = overflow,
                detail = detail
            });
        }

        static bool TryResolveActivationTarget(GameObject root, TemporaryActivationRequest activation, out GameObject targetObject)
        {
            targetObject = null;
            if (root == null || activation == null)
                return false;

            string path = !string.IsNullOrWhiteSpace(activation.targetPath) ? activation.targetPath : null;
            string searchMethod = activation.searchMethod ?? "by_name";
            if (!string.IsNullOrWhiteSpace(path) ||
                string.Equals(searchMethod, "by_path", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(searchMethod, "path", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(searchMethod, "hierarchy_path", StringComparison.OrdinalIgnoreCase))
            {
                targetObject = ResolveRelativePath(root.transform, path ?? activation.target)?.gameObject;
                return targetObject != null;
            }

            string target = activation.target;
            if (string.IsNullOrWhiteSpace(target))
                return false;

            Transform[] transforms = root.GetComponentsInChildren<Transform>(activation.includeInactive);
            if (string.Equals(searchMethod, "contains", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(searchMethod, "by_name_contains", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(searchMethod, "name_contains", StringComparison.OrdinalIgnoreCase))
            {
                targetObject = transforms
                    .FirstOrDefault(transform => transform != null && transform.name.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0)
                    ?.gameObject;
                return targetObject != null;
            }

            targetObject = transforms
                .FirstOrDefault(transform => transform != null && string.Equals(transform.name, target, StringComparison.OrdinalIgnoreCase))
                ?.gameObject;
            return targetObject != null;
        }

        static Transform ResolveRelativePath(Transform root, string path)
        {
            if (root == null || string.IsNullOrWhiteSpace(path))
                return null;

            string normalized = path.Trim().Replace('\\', '/').Trim('/');
            if (normalized == "." || string.Equals(normalized, root.name, StringComparison.OrdinalIgnoreCase))
                return root;

            if (normalized.StartsWith(root.name + "/", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(root.name.Length + 1);

            Transform current = root;
            foreach (string segment in normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Transform next = null;
                for (int i = 0; i < current.childCount; i++)
                {
                    Transform child = current.GetChild(i);
                    if (string.Equals(child.name, segment, StringComparison.OrdinalIgnoreCase))
                    {
                        next = child;
                        break;
                    }
                }

                if (next == null)
                    return null;
                current = next;
            }

            return current;
        }

        static Request Normalize(JObject parameters)
        {
            var request = new Request
            {
                PrefabPath = NormalizeAssetPath(GetString(parameters, "prefabPath", "PrefabPath")),
                IncludeInactive = GetBool(parameters, true, "includeInactive", "IncludeInactive"),
                MaxElements = Clamp(GetInt(parameters, DefaultMaxElements, "maxElements", "MaxElements"), 1, MaxElementLimit),
                MaxFindings = Clamp(GetInt(parameters, DefaultMaxFindings, "maxFindings", "MaxFindings"), 1, MaxFindingLimit),
                Checks = NormalizeChecks(GetToken(parameters, "checks", "Checks") as JObject)
            };

            request.Resolutions = NormalizeResolutions(GetToken(parameters, "resolutions", "Resolutions"));
            request.States = NormalizeStates(GetToken(parameters, "states", "States"), GetToken(parameters, "temporaryActivations", "TemporaryActivations"));
            return request;
        }

        static ResolutionRequest[] NormalizeResolutions(JToken token)
        {
            if (token is not JArray array || array.Count == 0)
            {
                return new[]
                {
                    new ResolutionRequest { key = "1920x1080", width = 1920, height = 1080 },
                    new ResolutionRequest { key = "1366x768", width = 1366, height = 768 }
                };
            }

            return array
                .OfType<JObject>()
                .Select((item, index) => new ResolutionRequest
                {
                    key = GetString(item, "key", "Key") ?? $"resolution_{index}",
                    width = GetInt(item, 0, "width", "Width"),
                    height = GetInt(item, 0, "height", "Height")
                })
                .ToArray();
        }

        static StateRequest[] NormalizeStates(JToken statesToken, JToken topLevelActivationsToken)
        {
            if (statesToken is not JArray statesArray || statesArray.Count == 0)
            {
                return new[]
                {
                    new StateRequest
                    {
                        name = "base",
                        temporaryActivations = NormalizeTemporaryActivations(topLevelActivationsToken)
                    }
                };
            }

            return statesArray
                .OfType<JObject>()
                .Select((item, index) => new StateRequest
                {
                    name = GetString(item, "name", "Name") ?? $"state_{index}",
                    temporaryActivations = NormalizeTemporaryActivations(GetToken(item, "temporaryActivations", "TemporaryActivations"))
                })
                .ToArray();
        }

        static TemporaryActivationRequest[] NormalizeTemporaryActivations(JToken token)
        {
            if (token is not JArray array || array.Count == 0)
                return Array.Empty<TemporaryActivationRequest>();

            return array
                .OfType<JObject>()
                .Select(item => new TemporaryActivationRequest
                {
                    target = GetString(item, "target", "Target"),
                    targetPath = GetString(item, "targetPath", "TargetPath"),
                    searchMethod = GetString(item, "searchMethod", "SearchMethod") ?? "by_name",
                    includeInactive = GetBool(item, true, "includeInactive", "IncludeInactive"),
                    active = GetBool(item, true, "active", "Active")
                })
                .ToArray();
        }

        static CheckOptions NormalizeChecks(JObject checks)
        {
            return new CheckOptions
            {
                BoundsWithinCanvas = GetBool(checks, true, "boundsWithinCanvas", "BoundsWithinCanvas"),
                TextOverflow = GetBool(checks, true, "textOverflow", "TextOverflow"),
                ZeroOrNegativeSize = GetBool(checks, true, "zeroOrNegativeSize", "ZeroOrNegativeSize")
            };
        }

        static bool TryValidatePrefabPath(string prefabPath, out string errorMessage, out object errorData)
        {
            errorMessage = null;
            errorData = null;
            if (!IsPrefabAssetPath(prefabPath) || AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                errorMessage = "prefabPath must be a valid .prefab asset path under Assets.";
                errorData = new
                {
                    status = "invalid_prefab_path",
                    prefabPath,
                    saveState = BuildReadOnlySaveState()
                };
                return false;
            }

            return true;
        }

        static object BuildCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            TruncateArray(root, "matrix", 80);
            TruncateArray(root, "findings", 80);
            TruncateArray(root, "elements", 60);
            if (root["dirtyEvidence"] is JObject dirtyEvidence)
            {
                dirtyEvidence.Remove("sceneDirtyBefore");
                dirtyEvidence.Remove("sceneDirtyAfter");
            }

            return root;
        }

        static object BuildTemporaryActivationsSchema()
        {
            return new
            {
                type = "array",
                description = "Temporary activeSelf changes applied to the loaded prefab copy for a review state.",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        target = new { type = "string", description = "Target object name or path." },
                        targetPath = new { type = "string", description = "Path relative to the prefab root." },
                        searchMethod = new { type = "string", description = "by_name, by_path, or contains." },
                        includeInactive = new { type = "boolean", description = "Include inactive objects when resolving the target. Defaults to true." },
                        active = new { type = "boolean", description = "Temporary activeSelf value. Defaults to true." }
                    }
                }
            };
        }

        static object BuildReadOnlySaveState()
        {
            return new
            {
                requested = false,
                attempted = false,
                saved = false,
                message = "not_requested_read_only_layout_matrix"
            };
        }

        static object BuildCanvasData(Canvas canvas, RectTransform rectTransform, ResolutionRequest resolution, bool createdWrapper)
        {
            return new
            {
                createdWrapper,
                canvasPath = rectTransform != null ? UiDiagnosticsHelper.GetHierarchyPath(rectTransform) : null,
                renderMode = canvas != null ? canvas.renderMode.ToString() : null,
                requestedResolution = BuildResolutionObject(resolution),
                rect = rectTransform != null ? ToRectObject(rectTransform.rect) : null
            };
        }

        static object BuildResolutionObject(ResolutionRequest resolution)
        {
            return new
            {
                key = string.IsNullOrWhiteSpace(resolution?.key) ? $"{resolution?.width ?? 0}x{resolution?.height ?? 0}" : resolution.key,
                width = resolution?.width ?? 0,
                height = resolution?.height ?? 0
            };
        }

        static object BuildOverflowObject(Bounds bounds, Rect canvasRect)
        {
            return new
            {
                left = Math.Max(0, canvasRect.xMin - bounds.min.x),
                right = Math.Max(0, bounds.max.x - canvasRect.xMax),
                bottom = Math.Max(0, canvasRect.yMin - bounds.min.y),
                top = Math.Max(0, bounds.max.y - canvasRect.yMax)
            };
        }

        static bool BoundsInsideRect(Bounds bounds, Rect rect)
        {
            return bounds.min.x >= rect.xMin - BoundsTolerance &&
                bounds.max.x <= rect.xMax + BoundsTolerance &&
                bounds.min.y >= rect.yMin - BoundsTolerance &&
                bounds.max.y <= rect.yMax + BoundsTolerance;
        }

        static void SetRectTransformSize(RectTransform rectTransform, int width, int height)
        {
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }

        static string[] GetComponentTypeNames(GameObject gameObject)
        {
            return gameObject.GetComponents<Component>()
                .Where(component => component != null)
                .Select(component => component.GetType().FullName)
                .Where(typeName => !string.IsNullOrWhiteSpace(typeName))
                .ToArray();
        }

        static string GetTextPreview(RectTransform rectTransform)
        {
            Text text = rectTransform.GetComponent<Text>();
            if (text != null && !string.IsNullOrEmpty(text.text))
                return Truncate(text.text, 120);

            foreach (Component component in rectTransform.GetComponents<Component>())
            {
                if (component == null)
                    continue;

                Type type = component.GetType();
                if (type.FullName == null || !type.FullName.StartsWith("TMPro.", StringComparison.Ordinal))
                    continue;

                string value = type.GetProperty("text", BindingFlags.Public | BindingFlags.Instance)?.GetValue(component)?.ToString();
                if (!string.IsNullOrEmpty(value))
                    return Truncate(value, 120);
            }

            return null;
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

            return current == root ? string.Join("/", parts) : UiDiagnosticsHelper.GetHierarchyPath(target);
        }

        static object ToRectObject(Rect rect)
        {
            return new { x = rect.x, y = rect.y, width = rect.width, height = rect.height, xMin = rect.xMin, xMax = rect.xMax, yMin = rect.yMin, yMax = rect.yMax };
        }

        static object ToBoundsObject(Bounds bounds)
        {
            return new
            {
                center = ToVector3Object(bounds.center),
                size = ToVector3Object(bounds.size),
                min = ToVector3Object(bounds.min),
                max = ToVector3Object(bounds.max)
            };
        }

        static object ToVector3Object(Vector3 value)
        {
            return new { x = value.x, y = value.y, z = value.z };
        }

        static float ConvertToFloat(object value)
        {
            return value switch
            {
                float floatValue => floatValue,
                double doubleValue => (float)doubleValue,
                int intValue => intValue,
                long longValue => longValue,
                _ => 0f
            };
        }

        static bool IsPrefabAssetPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
        }

        static string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            string normalized = path.Trim().Replace('\\', '/');
            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return normalized;

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

        static int Clamp(int value, int min, int max)
        {
            return Math.Min(max, Math.Max(min, value));
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

        static string ToPascalCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value;

            return value.Substring(0, maxLength) + "...";
        }
    }
}
