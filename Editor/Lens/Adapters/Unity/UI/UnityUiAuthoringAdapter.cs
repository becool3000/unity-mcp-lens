#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Models.UI;
using Becool.UnityMcpLens.Editor.Tools;
using Becool.UnityMcpLens.Editor.Tools.Parameters;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Becool.UnityMcpLens.Editor.Adapters.Unity.UI
{
    sealed class UnityUiAuthoringAdapter
    {
        sealed class MeasuredUiTarget
        {
            public string Key { get; init; }
            public Transform Transform { get; init; }
            public RectTransform RectTransform { get; init; }
            public Rect ScreenRect { get; init; }
            public Vector3[] WorldCorners { get; init; }
            public Vector2[] ScreenCorners { get; init; }
            public string Path { get; init; }
            public string CanvasPath { get; init; }
        }

        sealed class GameViewSelectionSnapshot
        {
            public int SelectedSizeIndex { get; init; }
            public int ScreenWidth { get; init; }
            public int ScreenHeight { get; init; }
        }

        public bool TryEnsureHierarchy(
            UiEnsureHierarchyRequest request,
            bool previewOnly,
            out GameObject targetRoot,
            out List<object> nodes,
            out bool applied,
            out string error)
        {
            nodes = new List<object>();
            applied = false;
            if (!UiAuthoringTools.TryResolveRoot(request?.Target, request?.SearchMethod, request?.IncludeInactive ?? true, out targetRoot, out error))
                return false;

            return UiAuthoringTools.TryEnsureNamedHierarchy(
                targetRoot,
                request?.Nodes ?? Array.Empty<UiNamedHierarchyNodeSpec>(),
                previewOnly,
                out nodes,
                out applied,
                out error);
        }

        public bool TryApplyLayoutProperties(
            UiLayoutPropertiesRequest request,
            bool previewOnly,
            out GameObject targetRoot,
            out Transform targetTransform,
            out List<object> changes,
            out bool applied,
            out string error)
        {
            changes = new List<object>();
            applied = false;
            if (!UiAuthoringTools.TryResolveLayoutTarget(
                    request?.Target,
                    request?.SearchMethod,
                    request?.TargetPath,
                    request?.IncludeInactive ?? true,
                    out targetRoot,
                    out targetTransform,
                    out error))
            {
                return false;
            }

            SetUiLayoutPropertiesParams parameters = UiAuthoringTools.CreateLayoutParams(request.Layout, previewOnly) ?? new SetUiLayoutPropertiesParams { PreviewOnly = previewOnly };
            return UiAuthoringTools.TryApplyLayout(targetTransform.gameObject, parameters, out changes, out applied, out error);
        }

        public bool TryVerifyScreenLayout(UiVerifyScreenLayoutRequest request, out object data, out string error)
        {
            error = null;
            data = null;
            if (request?.Targets == null || request.Targets.Length == 0)
            {
                error = "At least one verify target is required.";
                return false;
            }

            var measuredTargets = new Dictionary<string, MeasuredUiTarget>(StringComparer.OrdinalIgnoreCase);
            foreach (UiVerifyTargetRequest targetRequest in request.Targets)
            {
                if (string.IsNullOrWhiteSpace(targetRequest?.key))
                {
                    error = "Each verify target requires a key.";
                    return false;
                }

                if (!UiAuthoringTools.TryResolveLayoutTarget(
                        targetRequest.target,
                        targetRequest.searchMethod,
                        targetRequest.targetPath,
                        targetRequest.includeInactive,
                        out _,
                        out Transform targetTransform,
                        out error))
                {
                    error = $"Failed to resolve verify target '{targetRequest.key}': {error}";
                    return false;
                }

                if (targetTransform is not RectTransform rectTransform)
                {
                    error = $"Verify target '{targetRequest.key}' resolved to '{UiDiagnosticsHelper.GetHierarchyPath(targetTransform)}', which is not a RectTransform.";
                    return false;
                }

                if (!UiDiagnosticsHelper.TryGetScreenRect(rectTransform, out Rect screenRect, out Vector3[] worldCorners, out Vector2[] screenCorners))
                {
                    error = $"Could not measure screen rect for verify target '{targetRequest.key}'.";
                    return false;
                }

                Canvas canvas = rectTransform.GetComponentInParent<Canvas>(true);
                measuredTargets[targetRequest.key] = new MeasuredUiTarget
                {
                    Key = targetRequest.key,
                    Transform = rectTransform,
                    RectTransform = rectTransform,
                    ScreenRect = screenRect,
                    WorldCorners = worldCorners,
                    ScreenCorners = screenCorners,
                    Path = UiDiagnosticsHelper.GetHierarchyPath(rectTransform),
                    CanvasPath = canvas != null ? UiDiagnosticsHelper.GetHierarchyPath(canvas.transform) : string.Empty
                };
            }

            var assertionRows = new List<object>();
            bool passed = true;
            foreach (UiVerifyAssertionRequest assertion in request.Assertions ?? Array.Empty<UiVerifyAssertionRequest>())
            {
                if (!TryEvaluateAssertion(measuredTargets, assertion, out object row, out error))
                    return false;

                assertionRows.Add(row);
                if (row is { } rowObject && rowObject.GetType().GetProperty("passed")?.GetValue(rowObject) is bool rowPassed && !rowPassed)
                    passed = false;
            }

            data = new
            {
                passed,
                screen = new
                {
                    width = Screen.width,
                    height = Screen.height
                },
                targets = measuredTargets.Values.Select(target => new
                {
                    key = target.Key,
                    path = target.Path,
                    canvasPath = target.CanvasPath,
                    activeSelf = target.Transform.gameObject.activeSelf,
                    activeInHierarchy = target.Transform.gameObject.activeInHierarchy,
                    screenRect = ToRectObject(target.ScreenRect),
                    screenCorners = target.ScreenCorners.Select(ToVector2Object).ToArray(),
                    worldCorners = target.WorldCorners.Select(ToVector3Object).ToArray()
                }).ToArray(),
                assertions = assertionRows.ToArray()
            };
            return true;
        }

        public bool TryVerifyScreenLayoutMatrix(UiVerifyScreenLayoutMatrixRequest request, out object data, out string error)
        {
            data = null;
            error = null;
            if (request?.Resolutions == null || request.Resolutions.Length == 0)
            {
                error = "At least one resolution is required.";
                return false;
            }

            if (request.Targets == null || request.Targets.Length == 0)
            {
                error = "At least one verify target is required.";
                return false;
            }

            if (!TryCaptureGameViewSelection(out GameViewSelectionSnapshot original, out error))
                return false;

            var rows = new List<object>();
            bool passed = true;
            object restoreData = null;
            int warmupMs = Math.Max(0, request.WarmupMs);

            try
            {
                foreach (UiScreenResolutionRequest resolution in request.Resolutions)
                {
                    if (resolution == null || resolution.width <= 0 || resolution.height <= 0)
                    {
                        error = "Each resolution requires positive width and height.";
                        return false;
                    }

                    string key = string.IsNullOrWhiteSpace(resolution.key)
                        ? $"{resolution.width}x{resolution.height}"
                        : resolution.key.Trim();
                    bool setSucceeded = TrySetGameViewResolution(resolution.width, resolution.height, $"Lens {resolution.width}x{resolution.height}", out object selection, out string setError);
                    if (warmupMs > 0)
                        Thread.Sleep(warmupMs);

                    Vector2 gameViewSize = Handles.GetMainGameViewSize();
                    object layoutData = null;
                    bool layoutPassed = false;
                    string layoutError = null;
                    if (setSucceeded)
                    {
                        bool verified = TryVerifyScreenLayout(
                            new UiVerifyScreenLayoutRequest
                            {
                                Targets = request.Targets ?? Array.Empty<UiVerifyTargetRequest>(),
                                Assertions = request.Assertions ?? Array.Empty<UiVerifyAssertionRequest>()
                            },
                            out layoutData,
                            out layoutError);
                        if (!verified)
                        {
                            layoutError ??= "Layout verification failed.";
                        }
                        else
                        {
                            layoutPassed = JObject.FromObject(layoutData ?? new { })["passed"]?.Value<bool>() == true;
                        }
                    }

                    bool rowPassed = setSucceeded && layoutPassed;
                    passed &= rowPassed;
                    rows.Add(new
                    {
                        key,
                        requested = new { width = resolution.width, height = resolution.height },
                        setSucceeded,
                        setError,
                        selection,
                        screen = new { width = Screen.width, height = Screen.height },
                        gameViewSize = ToVector2Object(gameViewSize),
                        passed = rowPassed,
                        layoutError,
                        layout = layoutData
                    });
                }
            }
            finally
            {
                if (request.RestoreOriginal)
                {
                    bool restored = TrySetGameViewSizeIndex(original.SelectedSizeIndex, out string restoreError);
                    if (warmupMs > 0)
                        Thread.Sleep(warmupMs);

                    restoreData = new
                    {
                        requested = true,
                        succeeded = restored,
                        error = restoreError,
                        original = original,
                        screen = new { width = Screen.width, height = Screen.height },
                        gameViewSize = ToVector2Object(Handles.GetMainGameViewSize())
                    };
                    passed &= restored;
                }
                else
                {
                    restoreData = new { requested = false, succeeded = (bool?)null };
                }
            }

            data = new
            {
                passed,
                original,
                restore = restoreData,
                resolutionCount = rows.Count,
                resolutions = rows.ToArray()
            };
            return true;
        }

        public bool TryCreateCanvasPrefab(UiCanvasPrefabRequest request, bool previewOnly, out object data, out bool applied, out string error)
        {
            data = null;
            applied = false;
            error = null;

            string prefabPath = NormalizeAssetPath(request?.PrefabPath);
            if (string.IsNullOrWhiteSpace(prefabPath) || !prefabPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || !prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                error = "prefabPath must be a .prefab path under Assets.";
                return false;
            }

            string rootName = string.IsNullOrWhiteSpace(request.RootName)
                ? Path.GetFileNameWithoutExtension(prefabPath)
                : request.RootName.Trim();
            GameObject existingAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            bool exists = existingAsset != null;
            var rootChanges = new List<object>();
            var nodeRows = new List<object>();

            if (previewOnly)
            {
                bool rootWouldModify = !exists;
                if (exists)
                {
                    GameObject contents = null;
                    try
                    {
                        contents = PrefabUtility.LoadPrefabContents(prefabPath);
                        if (contents == null)
                        {
                            error = $"Prefab '{prefabPath}' could not be loaded.";
                            return false;
                        }

                        if (!TryConfigureCanvasRoot(contents, request, previewOnly: true, rootName, out rootChanges, out rootWouldModify, out error))
                            return false;

                        if (!UiAuthoringTools.TryEnsureNamedHierarchy(contents, request.Nodes ?? Array.Empty<UiNamedHierarchyNodeSpec>(), previewOnly: true, out nodeRows, out bool nodesWouldModify, out error))
                            return false;

                        rootWouldModify |= nodesWouldModify;
                    }
                    finally
                    {
                        if (contents != null)
                            PrefabUtility.UnloadPrefabContents(contents);
                    }
                }
                else
                {
                    rootChanges.Add(new { property = "prefab", previousValue = (string)null, newValue = prefabPath });
                    rootChanges.Add(new { property = "root.name", previousValue = (string)null, newValue = rootName });
                    AppendCreateRows(rootName, request.Nodes ?? Array.Empty<UiNamedHierarchyNodeSpec>(), nodeRows);
                }

                data = new
                {
                    prefabPath,
                    exists,
                    rootName,
                    applied = false,
                    willModify = rootWouldModify,
                    rootChanges = rootChanges.ToArray(),
                    nodes = nodeRows.ToArray()
                };
                return true;
            }

            GameObject root = null;
            bool loadedPrefabContents = false;
            try
            {
                if (exists)
                {
                    root = PrefabUtility.LoadPrefabContents(prefabPath);
                    loadedPrefabContents = true;
                    if (root == null)
                    {
                        error = $"Prefab '{prefabPath}' could not be loaded.";
                        return false;
                    }
                }
                else
                {
                    string directory = Path.GetDirectoryName(prefabPath)?.Replace('\\', '/');
                    if (!string.IsNullOrWhiteSpace(directory))
                        Directory.CreateDirectory(directory);
                    root = new GameObject(rootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                }

                if (!TryConfigureCanvasRoot(root, request, previewOnly: false, rootName, out rootChanges, out bool rootChanged, out error))
                    return false;

                if (!UiAuthoringTools.TryEnsureNamedHierarchy(root, request.Nodes ?? Array.Empty<UiNamedHierarchyNodeSpec>(), previewOnly: false, out nodeRows, out bool nodesChanged, out error))
                    return false;

                applied = !exists || rootChanged || nodesChanged;
                if (applied)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }

                data = new
                {
                    prefabPath,
                    exists,
                    rootName = root.name,
                    applied,
                    willModify = applied,
                    rootChanges = rootChanges.ToArray(),
                    nodes = nodeRows.ToArray()
                };
                return true;
            }
            finally
            {
                if (root != null)
                {
                    if (loadedPrefabContents)
                        PrefabUtility.UnloadPrefabContents(root);
                    else
                        UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        public bool TryVerifyRaycastAndLayout(UiVerifyRaycastAndLayoutRequest request, out object data, out string error)
        {
            error = null;
            data = null;
            var pointRows = new List<object>();
            bool passed = true;

            foreach (UiRaycastPointRequest point in request?.Points ?? Array.Empty<UiRaycastPointRequest>())
            {
                if (point == null || string.IsNullOrWhiteSpace(point.key))
                {
                    error = "Each raycast point requires a key.";
                    return false;
                }

                var roots = UiDiagnosticsHelper.ResolveUiRoots(point.target, point.searchMethod, point.includeInactive).ToList();
                if (roots.Count == 0)
                {
                    error = $"No UI roots resolved for point '{point.key}'.";
                    return false;
                }

                Vector2 screenPoint = new(point.screenX, point.screenY);
                var hits = new List<UiDiagnosticsHelper.UiElementHitInfo>();
                foreach (GameObject root in roots)
                {
                    hits.AddRange(UiDiagnosticsHelper.EnumerateGraphics(root, true, point.includeInactive)
                        .Where(info => info.ScreenRect.Contains(screenPoint)));
                }

                var ordered = hits
                    .OrderByDescending(info => info.Active)
                    .ThenByDescending(info => info.BlocksRaycasts)
                    .ThenByDescending(info => info.RaycastTarget)
                    .ThenByDescending(info => info.SortingOrder)
                    .ThenByDescending(info => info.Depth)
                    .Take(Math.Max(1, point.maxResults <= 0 ? 10 : point.maxResults))
                    .ToList();
                var topBlocker = ordered.FirstOrDefault(info => info.Active && info.BlocksRaycasts);
                bool blocked = topBlocker != null;
                bool pointPassed = true;
                var assertionRows = new List<object>();

                if (point.expectBlocked.HasValue)
                {
                    bool assertionPassed = blocked == point.expectBlocked.Value;
                    pointPassed &= assertionPassed;
                    assertionRows.Add(new
                    {
                        type = "expect_blocked",
                        expected = point.expectBlocked.Value,
                        actual = blocked,
                        passed = assertionPassed
                    });
                }

                if (!string.IsNullOrWhiteSpace(point.expectTopPathContains))
                {
                    string topPath = topBlocker?.Path ?? string.Empty;
                    bool assertionPassed = topPath.IndexOf(point.expectTopPathContains, StringComparison.OrdinalIgnoreCase) >= 0;
                    pointPassed &= assertionPassed;
                    assertionRows.Add(new
                    {
                        type = "expect_top_path_contains",
                        expected = point.expectTopPathContains,
                        actual = topPath,
                        passed = assertionPassed
                    });
                }

                passed &= pointPassed;
                pointRows.Add(new
                {
                    key = point.key,
                    point = ToVector2Object(screenPoint),
                    passed = pointPassed,
                    rootCount = roots.Count,
                    hitCount = ordered.Count,
                    blocked,
                    topHit = topBlocker == null ? null : BuildHitRow(topBlocker),
                    hits = ordered.Select(BuildHitRow).ToArray(),
                    assertions = assertionRows.ToArray()
                });
            }

            object layoutData = null;
            if ((request?.Targets?.Length ?? 0) > 0 || (request?.Assertions?.Length ?? 0) > 0)
            {
                if (!TryVerifyScreenLayout(
                        new UiVerifyScreenLayoutRequest
                        {
                            Targets = request.Targets ?? Array.Empty<UiVerifyTargetRequest>(),
                            Assertions = request.Assertions ?? Array.Empty<UiVerifyAssertionRequest>()
                        },
                        out layoutData,
                        out error))
                {
                    return false;
                }

                if (layoutData != null)
                {
                    JToken token = JToken.FromObject(layoutData);
                    if (token["passed"]?.Value<bool>() == false)
                        passed = false;
                }
            }

            data = new
            {
                passed,
                screen = new { width = Screen.width, height = Screen.height },
                pointCount = pointRows.Count,
                points = pointRows.ToArray(),
                layout = layoutData
            };
            return true;
        }

        static bool TryEvaluateAssertion(
            IReadOnlyDictionary<string, MeasuredUiTarget> measuredTargets,
            UiVerifyAssertionRequest assertion,
            out object row,
            out string error)
        {
            error = null;
            row = null;
            if (assertion == null || string.IsNullOrWhiteSpace(assertion.type))
            {
                error = "Each verify assertion requires a type.";
                return false;
            }

            string type = assertion.type.Trim().ToLowerInvariant();
            switch (type)
            {
                case "inside_screen":
                    if (!TryGetTarget(measuredTargets, assertion.targetKey, out MeasuredUiTarget insideTarget, out error))
                        return false;

                    float margin = Math.Max(0f, assertion.margin);
                    bool inside = insideTarget.ScreenRect.xMin >= margin &&
                                  insideTarget.ScreenRect.yMin >= margin &&
                                  insideTarget.ScreenRect.xMax <= Screen.width - margin &&
                                  insideTarget.ScreenRect.yMax <= Screen.height - margin;
                    row = new
                    {
                        type,
                        targetKey = assertion.targetKey,
                        passed = inside,
                        actual = new
                        {
                            rect = ToRectObject(insideTarget.ScreenRect),
                            margin,
                            screenWidth = Screen.width,
                            screenHeight = Screen.height
                        },
                        message = inside
                            ? $"'{assertion.targetKey}' is inside the screen."
                            : $"'{assertion.targetKey}' extends outside the screen."
                    };
                    return true;

                case "relative_position":
                    if (!TryGetTarget(measuredTargets, assertion.targetKey, out MeasuredUiTarget target, out error) ||
                        !TryGetTarget(measuredTargets, assertion.otherTargetKey, out MeasuredUiTarget otherTarget, out error))
                    {
                        return false;
                    }

                    string relation = (assertion.relation ?? string.Empty).Trim().ToLowerInvariant();
                    float tolerance = Math.Max(0f, assertion.tolerance);
                    bool relationPassed = relation switch
                    {
                        "right_of" => target.ScreenRect.xMin >= otherTarget.ScreenRect.xMax - tolerance,
                        "left_of" => target.ScreenRect.xMax <= otherTarget.ScreenRect.xMin + tolerance,
                        "above" => target.ScreenRect.yMin >= otherTarget.ScreenRect.yMax - tolerance,
                        "below" => target.ScreenRect.yMax <= otherTarget.ScreenRect.yMin + tolerance,
                        "right_of_center" => target.ScreenRect.center.x >= otherTarget.ScreenRect.center.x - tolerance,
                        "left_of_center" => target.ScreenRect.center.x <= otherTarget.ScreenRect.center.x + tolerance,
                        "above_center" => target.ScreenRect.center.y >= otherTarget.ScreenRect.center.y - tolerance,
                        "below_center" => target.ScreenRect.center.y <= otherTarget.ScreenRect.center.y + tolerance,
                        _ => false
                    };

                    if (relation is not ("right_of" or "left_of" or "above" or "below" or "right_of_center" or "left_of_center" or "above_center" or "below_center"))
                    {
                        error = $"Unsupported relative_position relation '{assertion.relation}'.";
                        return false;
                    }

                    row = new
                    {
                        type,
                        relation,
                        targetKey = assertion.targetKey,
                        otherTargetKey = assertion.otherTargetKey,
                        passed = relationPassed,
                        actual = new
                        {
                            targetRect = ToRectObject(target.ScreenRect),
                            otherRect = ToRectObject(otherTarget.ScreenRect),
                            targetCenter = ToVector2Object(target.ScreenRect.center),
                            otherCenter = ToVector2Object(otherTarget.ScreenRect.center),
                            tolerance
                        },
                        message = relationPassed
                            ? $"'{assertion.targetKey}' satisfied '{relation}' relative to '{assertion.otherTargetKey}'."
                            : $"'{assertion.targetKey}' did not satisfy '{relation}' relative to '{assertion.otherTargetKey}'."
                    };
                    return true;

                case "axis_alignment":
                    if (!TryGetTarget(measuredTargets, assertion.targetKey, out MeasuredUiTarget alignedTarget, out error) ||
                        !TryGetTarget(measuredTargets, assertion.otherTargetKey, out MeasuredUiTarget alignedOther, out error))
                    {
                        return false;
                    }

                    string axis = (assertion.axis ?? assertion.edge ?? string.Empty).Trim().ToLowerInvariant();
                    float delta = axis switch
                    {
                        "horizontal_center" => Mathf.Abs(alignedTarget.ScreenRect.center.x - alignedOther.ScreenRect.center.x),
                        "vertical_center" => Mathf.Abs(alignedTarget.ScreenRect.center.y - alignedOther.ScreenRect.center.y),
                        "left" => Mathf.Abs(alignedTarget.ScreenRect.xMin - alignedOther.ScreenRect.xMin),
                        "right" => Mathf.Abs(alignedTarget.ScreenRect.xMax - alignedOther.ScreenRect.xMax),
                        "top" => Mathf.Abs(alignedTarget.ScreenRect.yMax - alignedOther.ScreenRect.yMax),
                        "bottom" => Mathf.Abs(alignedTarget.ScreenRect.yMin - alignedOther.ScreenRect.yMin),
                        _ => -1f
                    };

                    if (delta < 0f)
                    {
                        error = $"Unsupported axis_alignment axis '{assertion.axis ?? assertion.edge}'.";
                        return false;
                    }

                    bool aligned = delta <= Math.Max(0f, assertion.tolerance);
                    row = new
                    {
                        type,
                        axis,
                        targetKey = assertion.targetKey,
                        otherTargetKey = assertion.otherTargetKey,
                        passed = aligned,
                        actual = new
                        {
                            delta,
                            tolerance = Math.Max(0f, assertion.tolerance)
                        },
                        message = aligned
                            ? $"'{assertion.targetKey}' aligned with '{assertion.otherTargetKey}' on '{axis}'."
                            : $"'{assertion.targetKey}' is misaligned with '{assertion.otherTargetKey}' on '{axis}'."
                    };
                    return true;

                case "ordered_stack":
                    if (assertion.targetKeys == null || assertion.targetKeys.Length < 2)
                    {
                        error = "ordered_stack requires at least two targetKeys.";
                        return false;
                    }

                    string direction = (assertion.direction ?? string.Empty).Trim().ToLowerInvariant();
                    float orderTolerance = Math.Max(0f, assertion.tolerance);
                    var orderPairs = new List<object>();
                    bool ordered = true;
                    for (int i = 0; i < assertion.targetKeys.Length - 1; i++)
                    {
                        if (!TryGetTarget(measuredTargets, assertion.targetKeys[i], out MeasuredUiTarget first, out error) ||
                            !TryGetTarget(measuredTargets, assertion.targetKeys[i + 1], out MeasuredUiTarget second, out error))
                        {
                            return false;
                        }

                        bool pairPass = direction switch
                        {
                            "top_to_bottom" => first.ScreenRect.center.y >= second.ScreenRect.center.y - orderTolerance,
                            "bottom_to_top" => first.ScreenRect.center.y <= second.ScreenRect.center.y + orderTolerance,
                            "left_to_right" => first.ScreenRect.center.x <= second.ScreenRect.center.x + orderTolerance,
                            "right_to_left" => first.ScreenRect.center.x >= second.ScreenRect.center.x - orderTolerance,
                            _ => false
                        };

                        if (direction is not ("top_to_bottom" or "bottom_to_top" or "left_to_right" or "right_to_left"))
                        {
                            error = $"Unsupported ordered_stack direction '{assertion.direction}'.";
                            return false;
                        }

                        ordered &= pairPass;
                        orderPairs.Add(new
                        {
                            first = assertion.targetKeys[i],
                            second = assertion.targetKeys[i + 1],
                            passed = pairPass
                        });
                    }

                    row = new
                    {
                        type,
                        direction,
                        targetKeys = assertion.targetKeys,
                        passed = ordered,
                        actual = new
                        {
                            tolerance = orderTolerance,
                            pairs = orderPairs.ToArray()
                        },
                        message = ordered
                            ? $"Targets satisfied ordered stack direction '{direction}'."
                            : $"Targets did not satisfy ordered stack direction '{direction}'."
                    };
                    return true;

                default:
                    error = $"Unsupported verify assertion type '{assertion.type}'.";
                    return false;
            }
        }

        static bool TryGetTarget(IReadOnlyDictionary<string, MeasuredUiTarget> measuredTargets, string key, out MeasuredUiTarget target, out string error)
        {
            if (!string.IsNullOrWhiteSpace(key) && measuredTargets.TryGetValue(key, out target))
            {
                error = null;
                return true;
            }

            target = null;
            error = $"Verify target '{key}' was not found.";
            return false;
        }

        static bool TryConfigureCanvasRoot(
            GameObject root,
            UiCanvasPrefabRequest request,
            bool previewOnly,
            string rootName,
            out List<object> changes,
            out bool changed,
            out string error)
        {
            var recordedChanges = new List<object>();
            var hasChanges = false;
            changes = recordedChanges;
            changed = false;
            error = null;
            if (root == null)
            {
                error = "Canvas prefab root is null.";
                return false;
            }

            if (root.transform is not RectTransform)
            {
                error = $"Canvas prefab root '{root.name}' must use RectTransform.";
                return false;
            }

            void RecordChange(string property, object previousValue, object newValue)
            {
                if (Equals(previousValue, newValue))
                    return;

                hasChanges = true;
                recordedChanges.Add(new
                {
                    property,
                    previousValue,
                    newValue
                });
            }

            if (!string.IsNullOrWhiteSpace(rootName) && !string.Equals(root.name, rootName, StringComparison.Ordinal))
            {
                RecordChange("root.name", root.name, rootName);
                if (!previewOnly)
                    root.name = rootName;
            }

            Canvas canvas = root.GetComponent<Canvas>();
            if (canvas == null)
            {
                RecordChange("component", null, typeof(Canvas).FullName);
                if (!previewOnly)
                    canvas = root.AddComponent<Canvas>();
            }

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                RecordChange("component", null, typeof(CanvasScaler).FullName);
                if (!previewOnly)
                    scaler = root.AddComponent<CanvasScaler>();
            }

            GraphicRaycaster raycaster = root.GetComponent<GraphicRaycaster>();
            if (raycaster == null)
            {
                RecordChange("component", null, typeof(GraphicRaycaster).FullName);
                if (!previewOnly)
                    root.AddComponent<GraphicRaycaster>();
            }

            RenderMode renderMode = ParseRenderMode(request?.RenderMode);
            if (canvas != null)
            {
                RecordChange("canvas.renderMode", canvas.renderMode.ToString(), renderMode.ToString());
                if (!previewOnly)
                    canvas.renderMode = renderMode;

                if (request?.SortingOrder.HasValue == true)
                {
                    RecordChange("canvas.sortingOrder", canvas.sortingOrder, request.SortingOrder.Value);
                    if (!previewOnly)
                        canvas.sortingOrder = request.SortingOrder.Value;
                }

                if (request?.PixelPerfect.HasValue == true)
                {
                    RecordChange("canvas.pixelPerfect", canvas.pixelPerfect, request.PixelPerfect.Value);
                    if (!previewOnly)
                        canvas.pixelPerfect = request.PixelPerfect.Value;
                }
            }

            if (scaler != null)
            {
                CanvasScaler.ScaleMode scaleMode = ParseScaleMode(request?.ScaleMode);
                RecordChange("canvasScaler.uiScaleMode", scaler.uiScaleMode.ToString(), scaleMode.ToString());
                if (!previewOnly)
                    scaler.uiScaleMode = scaleMode;

                if (request?.ReferenceResolution != null && request.ReferenceResolution.Type != JTokenType.Null)
                {
                    if (!TryParseVector2(request.ReferenceResolution, out Vector2 resolution))
                    {
                        error = "referenceResolution must be {x,y} or [x,y].";
                        return false;
                    }

                    RecordChange("canvasScaler.referenceResolution", scaler.referenceResolution, resolution);
                    if (!previewOnly)
                        scaler.referenceResolution = resolution;
                }
            }

            changed = hasChanges;
            if (!previewOnly && hasChanges)
                EditorUtility.SetDirty(root);

            return true;
        }

        static void AppendCreateRows(string parentPath, IReadOnlyList<UiNamedHierarchyNodeSpec> nodes, List<object> rows)
        {
            foreach (UiNamedHierarchyNodeSpec node in nodes ?? Array.Empty<UiNamedHierarchyNodeSpec>())
            {
                if (node == null)
                    continue;

                string path = $"{parentPath}/{node.Name}";
                rows.Add(new
                {
                    path,
                    action = "create",
                    existed = false,
                    requestedComponents = (node.ComponentTypes ?? Array.Empty<string>()).ToArray(),
                    requestedLayout = node.Layout != null
                });

                if (node.Children is JArray children)
                {
                    AppendCreateRows(
                        path,
                        children.Select(child => child?.ToObject<UiNamedHierarchyNodeSpec>()).Where(child => child != null).ToArray(),
                        rows);
                }
            }
        }

        static bool TryCaptureGameViewSelection(out GameViewSelectionSnapshot snapshot, out string error)
        {
            snapshot = null;
            if (!TryResolveGameView(out EditorWindow gameView, out _, out _, out error))
                return false;

            if (!TryGetSelectedGameViewSizeIndex(gameView, out int selectedIndex, out error))
                return false;

            snapshot = new GameViewSelectionSnapshot
            {
                SelectedSizeIndex = selectedIndex,
                ScreenWidth = Screen.width,
                ScreenHeight = Screen.height
            };
            return true;
        }

        static bool TrySetGameViewResolution(int width, int height, string label, out object selection, out string error)
        {
            selection = null;
            if (!TryResolveGameView(out EditorWindow gameView, out object group, out Type groupType, out error))
                return false;

            int index = FindGameViewSizeIndex(group, groupType, width, height);
            bool created = false;
            if (index < 0)
            {
                if (!TryAddCustomGameViewSize(group, groupType, width, height, label, out error))
                    return false;

                created = true;
                index = FindGameViewSizeIndex(group, groupType, width, height);
                if (index < 0)
                {
                    error = "Custom Game view size was added but could not be selected.";
                    return false;
                }
            }

            if (!TrySetGameViewSizeIndex(gameView, index, out error))
                return false;

            selection = new
            {
                selectedSizeIndex = index,
                created,
                label,
                width,
                height
            };
            return true;
        }

        static bool TrySetGameViewSizeIndex(int index, out string error)
        {
            if (!TryResolveGameView(out EditorWindow gameView, out object group, out Type groupType, out error))
                return false;

            int count = GetGameViewSizeCount(group, groupType);
            if (index < 0 || index >= count)
            {
                error = $"Game view size index {index} is outside the available range 0..{Math.Max(0, count - 1)}.";
                return false;
            }

            return TrySetGameViewSizeIndex(gameView, index, out error);
        }

        static bool TryResolveGameView(out EditorWindow gameView, out object sizeGroup, out Type sizeGroupType, out string error)
        {
            gameView = null;
            sizeGroup = null;
            sizeGroupType = null;
            error = null;
            Type gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
            if (gameViewType == null)
            {
                error = "UnityEditor.GameView type could not be resolved.";
                return false;
            }

            gameView = EditorWindow.GetWindow(gameViewType, false, "Game", false);
            if (gameView == null)
            {
                error = "Game view window could not be created or resolved.";
                return false;
            }

            Type gameViewSizesType = Type.GetType("UnityEditor.GameViewSizes,UnityEditor");
            if (gameViewSizesType == null)
            {
                error = "UnityEditor.GameViewSizes type could not be resolved.";
                return false;
            }

            Type singletonType = typeof(ScriptableSingleton<>).MakeGenericType(gameViewSizesType);
            object singleton = singletonType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            MethodInfo getGroup = gameViewSizesType.GetMethod("GetGroup", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (singleton == null || getGroup == null)
            {
                error = "GameViewSizes singleton or GetGroup method could not be resolved.";
                return false;
            }

            sizeGroup = getGroup.Invoke(singleton, new object[] { GameViewSizeGroupType.Standalone });
            if (sizeGroup == null)
            {
                error = "Standalone Game view size group could not be resolved.";
                return false;
            }

            sizeGroupType = sizeGroup.GetType();
            gameView.Focus();
            gameView.Repaint();
            return true;
        }

        static bool TryGetSelectedGameViewSizeIndex(EditorWindow gameView, out int selectedIndex, out string error)
        {
            selectedIndex = -1;
            error = null;
            Type gameViewType = gameView.GetType();
            PropertyInfo property = gameViewType.GetProperty("selectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null)
            {
                selectedIndex = Convert.ToInt32(property.GetValue(gameView));
                return true;
            }

            FieldInfo field = gameViewType.GetField("m_SelectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                selectedIndex = Convert.ToInt32(field.GetValue(gameView));
                return true;
            }

            error = "Game view selected size index could not be read.";
            return false;
        }

        static bool TrySetGameViewSizeIndex(EditorWindow gameView, int index, out string error)
        {
            error = null;
            Type gameViewType = gameView.GetType();

            MethodInfo callback = gameViewType.GetMethod("SizeSelectionCallback", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (callback != null)
            {
                ParameterInfo[] parameters = callback.GetParameters();
                object[] args = parameters.Length switch
                {
                    1 => new object[] { index },
                    2 when parameters[0].ParameterType == typeof(int) => new object[] { index, null },
                    2 => new object[] { null, index },
                    3 when parameters[0].ParameterType == typeof(int) => new object[] { index, null, null },
                    3 => new object[] { null, index, null },
                    _ => null
                };

                if (args != null)
                {
                    try
                    {
                        callback.Invoke(gameView, args);
                        gameView.Repaint();
                        EditorApplication.QueuePlayerLoopUpdate();
                        return true;
                    }
                    catch (TargetInvocationException ex)
                    {
                        error = ex.InnerException?.Message ?? ex.Message;
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                    }
                }
            }

            PropertyInfo property = gameViewType.GetProperty("selectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null && property.CanWrite)
            {
                property.SetValue(gameView, index);
                gameView.Repaint();
                EditorApplication.QueuePlayerLoopUpdate();
                return true;
            }

            FieldInfo field = gameViewType.GetField("m_SelectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(gameView, index);
                gameView.Repaint();
                EditorApplication.QueuePlayerLoopUpdate();
                return true;
            }

            error ??= "Game view selected size index could not be set.";
            return false;
        }

        static int FindGameViewSizeIndex(object group, Type groupType, int width, int height)
        {
            int count = GetGameViewSizeCount(group, groupType);
            MethodInfo getSize = groupType.GetMethod("GetGameViewSize", BindingFlags.Public | BindingFlags.Instance);
            if (getSize == null)
                return -1;

            for (int i = 0; i < count; i++)
            {
                object size = getSize.Invoke(group, new object[] { i });
                if (TryReadIntMember(size, "width", out int candidateWidth) &&
                    TryReadIntMember(size, "height", out int candidateHeight) &&
                    candidateWidth == width &&
                    candidateHeight == height)
                {
                    return i;
                }
            }

            return -1;
        }

        static int GetGameViewSizeCount(object group, Type groupType)
        {
            MethodInfo count = groupType.GetMethod("GetTotalCount", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return count != null ? Convert.ToInt32(count.Invoke(group, null)) : 0;
        }

        static bool TryAddCustomGameViewSize(object group, Type groupType, int width, int height, string label, out string error)
        {
            error = null;
            Type sizeType = Type.GetType("UnityEditor.GameViewSize,UnityEditor");
            Type sizeKindType = Type.GetType("UnityEditor.GameViewSizeType,UnityEditor");
            MethodInfo addCustomSize = groupType.GetMethod("AddCustomSize", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (sizeType == null || sizeKindType == null || addCustomSize == null)
            {
                error = "Game view custom-size APIs could not be resolved.";
                return false;
            }

            object fixedResolution = Enum.Parse(sizeKindType, "FixedResolution");
            ConstructorInfo ctor = sizeType.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { sizeKindType, typeof(int), typeof(int), typeof(string) },
                null);
            if (ctor == null)
            {
                error = "GameViewSize fixed-resolution constructor could not be resolved.";
                return false;
            }

            object size = ctor.Invoke(new[] { fixedResolution, width, height, label });
            addCustomSize.Invoke(group, new[] { size });
            return true;
        }

        static bool TryReadIntMember(object target, string name, out int value)
        {
            value = 0;
            if (target == null)
                return false;

            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property != null)
            {
                value = Convert.ToInt32(property.GetValue(target));
                return true;
            }

            FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (field != null)
            {
                value = Convert.ToInt32(field.GetValue(target));
                return true;
            }

            return false;
        }

        static RenderMode ParseRenderMode(string value)
        {
            string normalized = (value ?? string.Empty).Trim().Replace("-", "_").ToLowerInvariant();
            return normalized switch
            {
                "world_space" or "worldspace" => RenderMode.WorldSpace,
                "screen_space_camera" or "screenspacecamera" => RenderMode.ScreenSpaceCamera,
                _ => RenderMode.ScreenSpaceOverlay
            };
        }

        static CanvasScaler.ScaleMode ParseScaleMode(string value)
        {
            string normalized = (value ?? string.Empty).Trim().Replace("-", "_").ToLowerInvariant();
            return normalized switch
            {
                "constant_pixel_size" or "constantpixelsize" => CanvasScaler.ScaleMode.ConstantPixelSize,
                "constant_physical_size" or "constantphysicalsize" => CanvasScaler.ScaleMode.ConstantPhysicalSize,
                _ => CanvasScaler.ScaleMode.ScaleWithScreenSize
            };
        }

        static string NormalizeAssetPath(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Replace('\\', '/');
        }

        static object BuildHitRow(UiDiagnosticsHelper.UiElementHitInfo info)
        {
            return new
            {
                path = info.Path,
                label = info.RectTransform != null ? info.RectTransform.name : info.Path,
                canvasPath = info.CanvasPath,
                screenRect = ToRectObject(info.ScreenRect),
                sortingOrder = info.SortingOrder,
                depth = info.Depth,
                active = info.Active,
                raycastTarget = info.RaycastTarget,
                blocksRaycasts = info.BlocksRaycasts,
                graphicType = info.Graphic != null ? info.Graphic.GetType().FullName : string.Empty
            };
        }

        static bool TryParseVector2(JToken token, out Vector2 value)
        {
            value = default;
            if (token is JArray array && array.Count >= 2)
            {
                value = new Vector2(array[0].Value<float>(), array[1].Value<float>());
                return true;
            }

            if (token is JObject obj)
            {
                value = new Vector2(obj["x"]?.Value<float>() ?? 0f, obj["y"]?.Value<float>() ?? 0f);
                return true;
            }

            return false;
        }

        static object ToVector2Object(Vector2 value) => new { x = value.x, y = value.y };
        static object ToVector3Object(Vector3 value) => new { x = value.x, y = value.y, z = value.z };
        static object ToRectObject(Rect value) => new { x = value.x, y = value.y, width = value.width, height = value.height };
    }
}
