#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Models.UI;
using Becool.UnityMcpLens.Editor.Tools;
using Becool.UnityMcpLens.Editor.Tools.Parameters;
using UnityEngine;

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
                        _ => false
                    };

                    if (relation is not ("right_of" or "left_of" or "above" or "below"))
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

        static object ToVector2Object(Vector2 value) => new { x = value.x, y = value.y };
        static object ToVector3Object(Vector3 value) => new { x = value.x, y = value.y, z = value.z };
        static object ToRectObject(Rect value) => new { x = value.x, y = value.y, width = value.width, height = value.height };
    }
}
