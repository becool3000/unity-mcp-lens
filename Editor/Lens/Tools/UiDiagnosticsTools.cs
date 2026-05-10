using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Tools.Parameters;
using Becool.UnityMcpLens.Editor.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class UiDiagnosticsTools
    {
        public const string GetLayoutSnapshotDescription = @"Returns a read-only UI layout snapshot for a target GameObject, canvas, or subtree.

Args:
    Target: Optional target GameObject, path, or canvas root. When omitted, all root canvases are used.
    SearchMethod: How to find the target ('by_name', 'by_id', 'by_path').
    IncludeChildren: Include children of the target.
    IncludeInactive: Include inactive UI elements.

Returns:
    Dictionary with success/message/data. Data contains layout entries including anchors, pivots, authored rect data, active state, canvas path, world corners, and computed screen rect.";

        public const string UiRaycastDescription = @"Returns UI raycast information for a screen-space point using authored UI geometry.

Args:
    ScreenX: Screen-space X coordinate in pixels.
    ScreenY: Screen-space Y coordinate in pixels.
    Target: Optional target GameObject, path, or canvas root used to scope the raycast.
    SearchMethod: How to find the optional target ('by_name', 'by_id', 'by_path').
    IncludeInactive: Include inactive UI elements.
    MaxResults: Maximum number of hits to return.

Returns:
    Dictionary with success/message/data. Data contains sorted hits, topmost blocker, draw order hints, and overlap diagnostics.";

        public const string GetInteractiveRegionsDescription = @"Returns stable screen-space interactive regions for authored UI elements.

Args:
    Target: Optional target GameObject, path, or canvas root. When omitted, all root canvases are scanned.
    SearchMethod: How to find the optional target ('by_name', 'by_id', 'by_path').
    IncludeChildren: Include children of the target.
    IncludeInactive: Include inactive UI elements.

Returns:
    Dictionary with success/message/data. Data contains labeled screen-space regions suitable for click diagnostics and overlays.";

        public const string QueryRuntimeLayoutDescription = @"Queries runtime UI layout and control state without requiring custom C# snippets.

Args:
    Target: Optional target GameObject, path, or canvas root. When omitted, all root canvases are scanned.
    SearchMethod: How to find the optional target ('by_name', 'by_id', 'by_path').
    IncludeChildren: Include children of the target.
    IncludeInactive: Include inactive UI elements.
    ElementTypes: Optional filters such as text, image, button, slider, toggle, selectable, graphic, or canvas.
    TextFilter: Optional case-insensitive substring filter applied to visible text values.
    MaxElements: Maximum number of matching elements returned inline.
    IncludeScreenBounds: Include screen-space bounds for returned elements.

Returns:
    Compact counts plus matching elements with path, active state, screen rect, text, interactable state, sprite names, and layout warnings. Full element detail is available through detailRef when compacted.";

        public const string InvokeControlDescription = @"Invokes a runtime UI control through Unity UI events or component value APIs.

Args:
    Target: Target UI GameObject path, name, or id.
    SearchMethod: How to find the target ('by_name', 'by_id', 'by_path').
    IncludeInactive: Include inactive UI objects while resolving the target.
    Action: click, setSlider, or toggle.
    Value: Value used by setSlider and toggle. Toggle treats values >= 0.5 as true.
    WaitFrames: Frames to wait after sending the action.
    CaptureConsoleDelta: Include console error count before/after the action.
    AllowEditMode: Explicitly allow edit-mode invocation. Defaults to false.

Returns:
    Target resolution, event/control action result, selected object, changed values, and console error delta.";

        public const string CaptureGameViewDescription = @"Captures the current Game view to a relative path under the Unity project.

Args:
    SceneName: Optional scene name for logging only.
    OutputPath: Relative output path under the Unity project, for example Temp/UiCapture/shot.png.
    WarmupMs: Optional warmup delay in milliseconds before capture.
    PausePlayMode: Pause play mode before capture when Unity is already playing.
    StepFrames: Advance this many paused play-mode frames before capture.
    WaitForFileTimeoutMs: Timeout while waiting for the PNG to appear on disk.

Returns:
    Dictionary with success/message/data. Data contains the relative and absolute output paths plus capture state.";

        [McpOutputSchema("Unity.UI.QueryRuntimeLayout")]
        public static object GetQueryRuntimeLayoutOutputSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    success = new { type = "boolean", description = "Whether the runtime UI query succeeded." },
                    message = new { type = "string", description = "Human-readable query summary." },
                    data = new
                    {
                        type = "object",
                        description = "Compact runtime UI layout and control-state query result.",
                        properties = new
                        {
                            rootCount = new { type = "integer", description = "Number of UI roots scanned." },
                            screen = new { type = "object", description = "Current Game view screen width and height." },
                            filters = new { type = "object", description = "Normalized query filters used for this result." },
                            totalElementCount = new { type = "integer", description = "Total matching UI elements found before inline truncation." },
                            returnedElementCount = new { type = "integer", description = "Number of matching UI elements returned inline." },
                            warningCount = new { type = "integer", description = "Total layout warning count." },
                            warnings = new { type = "array", description = "First compact layout warnings such as offscreen or clipped elements." },
                            elements = new { type = "array", description = "Matching elements with name, path, active state, rect, text, interactable state, sprite, control values, and warnings." },
                            detailAvailable = new { type = "boolean", description = "Whether full element detail is available through detailRef." },
                            detailRef = new { type = "object", description = "Detail ref for the full result when compacted." },
                            rawBytes = new { type = "integer", description = "UTF-8 byte count of the full unshaped result payload." },
                            shapedBytes = new { type = "integer", description = "UTF-8 byte count of the compact inline result payload." }
                        }
                    }
                },
                required = new[] { "success", "message" }
            };
        }

        [McpOutputSchema("Unity.UI.InvokeControl")]
        public static object GetInvokeControlOutputSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    success = new { type = "boolean", description = "Whether the UI control action succeeded." },
                    message = new { type = "string", description = "Human-readable action result." },
                    data = new
                    {
                        type = "object",
                        description = "Runtime UI control action result.",
                        properties = new
                        {
                            target = new { type = "object", description = "Resolved target snapshot captured before dispatch." },
                            targetDestroyedAfterAction = new { type = "boolean", description = "Whether the original target was destroyed or replaced by the action." },
                            action = new { type = "string", description = "Normalized action: click, setslider, or toggle." },
                            eventSystemPresent = new { type = "boolean", description = "Whether an EventSystem was present while invoking the control." },
                            selectedObject = new { type = "object", description = "Selected object snapshot after dispatch, when available." },
                            waitFrames = new { type = "integer", description = "Number of post-action frames requested by the caller." },
                            actionResult = new { type = "object", description = "Action-specific dispatch result, changed values, event trace, or failure reason." },
                            consoleDelta = new { type = "object", description = "Console error counts before and after the action when requested." }
                        }
                    }
                },
                required = new[] { "success", "message" }
            };
        }

        [McpTool("Unity.UI.GetLayoutSnapshot", GetLayoutSnapshotDescription, Groups = new[] { "ui", "diagnostics" }, EnabledByDefault = true)]
        public static object GetLayoutSnapshot(UiLayoutSnapshotParams parameters)
        {
            parameters ??= new UiLayoutSnapshotParams();
            var roots = UiDiagnosticsHelper.ResolveUiRoots(parameters.Target, parameters.SearchMethod, parameters.IncludeInactive).ToList();
            if (roots.Count == 0)
            {
                return Response.Error("UI target not found.");
            }

            var entries = new List<object>();
            int maxEntries = Math.Max(1, parameters.MaxEntries);
            bool includeChildren = parameters.IncludeChildren && !string.IsNullOrWhiteSpace(parameters.Target);
            foreach (GameObject root in roots)
            {
                foreach (RectTransform rectTransform in UiDiagnosticsHelper.EnumerateRectTransforms(root, includeChildren, parameters.IncludeInactive))
                {
                    if (entries.Count >= maxEntries)
                    {
                        break;
                    }

                    if (rectTransform == null)
                    {
                        continue;
                    }

                    UiDiagnosticsHelper.TryGetScreenRect(rectTransform, out Rect screenRect, out Vector3[] worldCorners, out Vector2[] screenCorners);
                    Canvas canvas = rectTransform.GetComponentInParent<Canvas>(true);
                    Graphic graphic = rectTransform.GetComponent<Graphic>();
                    entries.Add(new
                    {
                        path = UiDiagnosticsHelper.GetHierarchyPath(rectTransform),
                        name = rectTransform.name,
                        canvasPath = canvas != null ? UiDiagnosticsHelper.GetHierarchyPath(canvas.transform) : string.Empty,
                        activeSelf = rectTransform.gameObject.activeSelf,
                        activeInHierarchy = rectTransform.gameObject.activeInHierarchy,
                        siblingIndex = rectTransform.GetSiblingIndex(),
                        anchorMin = ToVector2Object(rectTransform.anchorMin),
                        anchorMax = ToVector2Object(rectTransform.anchorMax),
                        pivot = ToVector2Object(rectTransform.pivot),
                        sizeDelta = ToVector2Object(rectTransform.sizeDelta),
                        anchoredPosition = ToVector2Object(rectTransform.anchoredPosition),
                        localScale = ToVector3Object(rectTransform.localScale),
                        screenRect = ToRectObject(screenRect),
                        worldCorners = parameters.IncludeGeometry ? worldCorners?.Select(ToVector3Object).ToArray() ?? Array.Empty<object>() : Array.Empty<object>(),
                        screenCorners = parameters.IncludeGeometry ? screenCorners?.Select(ToVector2Object).ToArray() ?? Array.Empty<object>() : Array.Empty<object>(),
                        graphic = graphic == null ? null : new
                        {
                            typeName = graphic.GetType().FullName,
                            enabled = graphic.enabled,
                            raycastTarget = graphic.raycastTarget,
                            depth = graphic.depth
                        }
                    });
                }
            }

            var payload = new
            {
                rootCount = roots.Count,
                entries
            };

            return Response.Success(
                $"Captured {entries.Count} UI layout entries.",
                ShapePayload(
                    "Unity.UI.GetLayoutSnapshot",
                    $"Captured {entries.Count} UI layout entries.",
                    payload,
                    new
                    {
                        tool = "Unity.UI.GetLayoutSnapshot",
                        args = new
                        {
                            parameters.Target,
                            parameters.SearchMethod,
                            parameters.IncludeChildren,
                            parameters.IncludeInactive,
                            parameters.MaxEntries,
                            parameters.IncludeGeometry
                        }
                    }));
        }

        [McpTool("Unity.UI.Raycast", UiRaycastDescription, Groups = new[] { "ui", "diagnostics" }, EnabledByDefault = true)]
        public static object Raycast(UiRaycastParams parameters)
        {
            parameters ??= new UiRaycastParams();
            var roots = UiDiagnosticsHelper.ResolveUiRoots(parameters.Target, parameters.SearchMethod, parameters.IncludeInactive).ToList();
            if (roots.Count == 0)
            {
                return Response.Error("UI target not found.");
            }

            Vector2 point = new(parameters.ScreenX, parameters.ScreenY);
            var hits = new List<UiDiagnosticsHelper.UiElementHitInfo>();
            foreach (GameObject root in roots)
            {
                hits.AddRange(UiDiagnosticsHelper.EnumerateGraphics(root, true, parameters.IncludeInactive)
                    .Where(info => info.ScreenRect.Contains(point)));
            }

            var ordered = hits
                .OrderByDescending(info => info.Active)
                .ThenByDescending(info => info.BlocksRaycasts)
                .ThenByDescending(info => info.RaycastTarget)
                .ThenByDescending(info => info.SortingOrder)
                .ThenByDescending(info => info.Depth)
                .Take(Math.Max(1, parameters.MaxResults))
                .ToList();

            object topHit = ordered
                .Where(info => info.Active && info.BlocksRaycasts)
                .Select(BuildHitResult)
                .FirstOrDefault();

            var payload = new
            {
                point = ToVector2Object(point),
                hitCount = ordered.Count,
                topHit,
                hits = ordered.Select(BuildHitResult).ToArray()
            };

            return Response.Success(
                $"Found {ordered.Count} UI hits at the requested point.",
                ShapePayload(
                    "Unity.UI.Raycast",
                    $"Found {ordered.Count} UI hits at the requested point.",
                    payload,
                    new
                    {
                        tool = "Unity.UI.Raycast",
                        args = new
                        {
                            parameters.ScreenX,
                            parameters.ScreenY,
                            parameters.Target,
                            parameters.SearchMethod,
                            parameters.IncludeInactive,
                            parameters.MaxResults
                        }
                    }));
        }

        [McpTool("Unity.UI.GetInteractiveRegions", GetInteractiveRegionsDescription, Groups = new[] { "ui", "diagnostics" }, EnabledByDefault = true)]
        public static object GetInteractiveRegions(UiInteractiveRegionsParams parameters)
        {
            parameters ??= new UiInteractiveRegionsParams();
            var roots = UiDiagnosticsHelper.ResolveUiRoots(parameters.Target, parameters.SearchMethod, parameters.IncludeInactive).ToList();
            if (roots.Count == 0)
            {
                return Response.Error("UI target not found.");
            }

            var regions = new List<object>();
            foreach (GameObject root in roots)
            {
                var grouped = UiDiagnosticsHelper.EnumerateGraphics(root, parameters.IncludeChildren, parameters.IncludeInactive)
                    .Where(info => info.Active && info.RaycastTarget && info.BlocksRaycasts)
                    .GroupBy(info => info.Path)
                    .Select(group => group
                        .OrderByDescending(info => info.SortingOrder)
                        .ThenByDescending(info => info.Depth)
                        .First());

                regions.AddRange(grouped.Select(info => new
                {
                    id = info.Path,
                    label = info.RectTransform != null ? info.RectTransform.name : info.Path,
                    path = info.Path,
                    canvasPath = info.CanvasPath,
                    screenRect = ToRectObject(info.ScreenRect),
                    sortingOrder = info.SortingOrder,
                    depth = info.Depth,
                    graphicType = info.Graphic != null ? info.Graphic.GetType().FullName : string.Empty
                }));
            }

            var payload = new
            {
                rootCount = roots.Count,
                regions
            };

            return Response.Success(
                $"Collected {regions.Count} interactive UI regions.",
                ShapePayload(
                    "Unity.UI.GetInteractiveRegions",
                    $"Collected {regions.Count} interactive UI regions.",
                    payload,
                    new
                    {
                        tool = "Unity.UI.GetInteractiveRegions",
                        args = new
                        {
                            parameters.Target,
                            parameters.SearchMethod,
                            parameters.IncludeChildren,
                            parameters.IncludeInactive
                        }
                    }));
        }

        [McpTool("Unity.UI.QueryRuntimeLayout", QueryRuntimeLayoutDescription, Groups = new[] { "ui", "diagnostics" }, EnabledByDefault = true)]
        public static object QueryRuntimeLayout(UiRuntimeLayoutQueryParams parameters)
        {
            parameters ??= new UiRuntimeLayoutQueryParams();
            var roots = UiDiagnosticsHelper.ResolveUiRoots(parameters.Target, parameters.SearchMethod, parameters.IncludeInactive).ToList();
            if (roots.Count == 0)
            {
                return Response.Error("UI target not found.");
            }

            int maxElements = Math.Max(1, parameters.MaxElements);
            var typeFilters = BuildTypeFilter(parameters.ElementTypes);
            string textFilter = string.IsNullOrWhiteSpace(parameters.TextFilter) ? null : parameters.TextFilter.Trim();
            int screenWidth = Math.Max(0, Screen.width);
            int screenHeight = Math.Max(0, Screen.height);
            var elements = new List<object>();
            var warnings = new List<object>();

            foreach (GameObject root in roots)
            {
                foreach (RectTransform rectTransform in UiDiagnosticsHelper.EnumerateRectTransforms(root, parameters.IncludeChildren, parameters.IncludeInactive))
                {
                    if (rectTransform == null)
                    {
                        continue;
                    }

                    string text = GetUiText(rectTransform);
                    string[] elementTypes = GetRuntimeElementTypes(rectTransform);
                    if (!MatchesTypeFilter(elementTypes, typeFilters))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(textFilter) &&
                        (text == null || text.IndexOf(textFilter, StringComparison.OrdinalIgnoreCase) < 0))
                    {
                        continue;
                    }

                    object element = BuildRuntimeLayoutElement(
                        rectTransform,
                        text,
                        elementTypes,
                        parameters.IncludeScreenBounds,
                        screenWidth,
                        screenHeight,
                        out string[] elementWarnings);
                    elements.Add(element);

                    foreach (string warning in elementWarnings)
                    {
                        warnings.Add(new
                        {
                            path = UiDiagnosticsHelper.GetHierarchyPath(rectTransform),
                            warning
                        });
                    }
                }
            }

            var rawPayload = new
            {
                rootCount = roots.Count,
                screen = new { width = screenWidth, height = screenHeight },
                filters = new
                {
                    target = parameters.Target,
                    parameters.SearchMethod,
                    parameters.IncludeChildren,
                    parameters.IncludeInactive,
                    elementTypes = typeFilters.ToArray(),
                    textFilter
                },
                totalElementCount = elements.Count,
                warningCount = warnings.Count,
                warnings,
                elements
            };
            var compactPayload = new
            {
                rawPayload.rootCount,
                rawPayload.screen,
                rawPayload.filters,
                rawPayload.totalElementCount,
                returnedElementCount = Math.Min(maxElements, elements.Count),
                rawPayload.warningCount,
                warnings = warnings.Take(10).ToArray(),
                elements = elements.Take(maxElements).ToArray()
            };

            return Response.Success(
                $"Collected {elements.Count} runtime UI layout element(s).",
                ToolResultCompactor.ShapeStructuredPayload(
                    "Unity.UI.QueryRuntimeLayout",
                    rawPayload,
                    compactPayload,
                    new
                    {
                        kind = "ui_runtime_layout",
                        target = parameters.Target,
                        rootCount = roots.Count,
                        elementCount = elements.Count,
                        warningCount = warnings.Count
                    },
                    "ui_runtime_layout_result",
                    detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes));
        }

        [McpTool("Unity.UI.InvokeControl", InvokeControlDescription, Groups = new[] { "ui" }, EnabledByDefault = true)]
        public static async Task<object> InvokeControl(UiInvokeControlParams parameters)
        {
            parameters ??= new UiInvokeControlParams();
            if (string.IsNullOrWhiteSpace(parameters.Target))
            {
                return Response.Error("Target is required.");
            }

            if (!EditorApplication.isPlaying && !parameters.AllowEditMode)
            {
                return Response.Error("Unity.UI.InvokeControl is play-mode only by default. Pass allowEditMode=true to invoke edit-mode UI.");
            }

            GameObject target = UiDiagnosticsHelper.ResolveUiRoots(parameters.Target, parameters.SearchMethod, parameters.IncludeInactive).FirstOrDefault();
            if (target == null)
            {
                return Response.Error("UI target not found.");
            }

            object targetSnapshot = BuildGameObjectSnapshot(target);
            string action = NormalizeControlAction(parameters.Action);
            int beforeConsoleErrors = parameters.CaptureConsoleDelta ? EditorToolStateHelpers.CountConsoleErrors() : 0;
            object actionResult;
            switch (action)
            {
                case "click":
                    actionResult = InvokeClick(target);
                    break;
                case "setslider":
                    actionResult = SetSliderValue(target, parameters.Value);
                    break;
                case "toggle":
                    actionResult = SetToggleValue(target, parameters.Value >= 0.5f);
                    break;
                default:
                    return Response.Error($"Unsupported UI control action '{parameters.Action}'. Use click, setSlider, or toggle.");
            }

            int waitFrames = Math.Max(0, parameters.WaitFrames);
            for (int i = 0; i < waitFrames; i++)
            {
                await Task.Delay(20);
            }

            int afterConsoleErrors = parameters.CaptureConsoleDelta ? EditorToolStateHelpers.CountConsoleErrors() : beforeConsoleErrors;
            bool actionSucceeded = GetActionSucceeded(actionResult);
            object selectedObject = EventSystem.current != null
                ? BuildGameObjectSnapshot(EventSystem.current.currentSelectedGameObject)
                : null;
            var payload = new
            {
                target = targetSnapshot,
                targetDestroyedAfterAction = target == null,
                action,
                eventSystemPresent = EventSystem.current != null,
                selectedObject,
                waitFrames,
                actionResult,
                consoleDelta = parameters.CaptureConsoleDelta
                    ? new
                    {
                        beforeErrors = beforeConsoleErrors,
                        afterErrors = afterConsoleErrors,
                        newErrorCount = Math.Max(0, afterConsoleErrors - beforeConsoleErrors)
                    }
                    : null
            };

            return actionSucceeded
                ? Response.Success($"UI control action '{action}' completed.", payload)
                : Response.Error($"UI control action '{action}' failed.", payload);
        }

        [McpTool("Unity.UI.CaptureGameView", CaptureGameViewDescription, Groups = new[] { "ui", "diagnostics" }, EnabledByDefault = true)]
        public static async Task<object> CaptureGameView(CaptureGameViewParams parameters)
        {
            parameters ??= new CaptureGameViewParams();
            if (string.IsNullOrWhiteSpace(parameters.OutputPath))
            {
                return Response.Error("OutputPath is required.");
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return Response.Error("Could not determine the Unity project root.");
            }

            if (!TryNormalizeRelativeProjectPath(projectRoot, parameters.OutputPath, out string relativeOutputPath, out string absoluteOutputPath))
            {
                return Response.Error("OutputPath must be relative to the Unity project root.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutputPath) ?? projectRoot);
            if (File.Exists(absoluteOutputPath))
            {
                File.Delete(absoluteOutputPath);
            }

            bool wasPlaying = EditorApplication.isPlaying;
            bool wasPaused = EditorApplication.isPaused;

            try
            {
                if (parameters.WarmupMs > 0)
                {
                    await Task.Delay(Math.Max(0, parameters.WarmupMs));
                }

                if (parameters.PausePlayMode && wasPlaying && !EditorApplication.isPaused)
                {
                    EditorApplication.isPaused = true;
                    await Task.Delay(100);
                }

                int stepFrames = Math.Max(0, parameters.StepFrames);
                for (int i = 0; i < stepFrames && EditorApplication.isPlaying && EditorApplication.isPaused; i++)
                {
                    EditorApplication.Step();
                    await Task.Delay(50);
                }

                if (!TryFocusGameView(out string focusError))
                {
                    return Response.Error("GAME_VIEW_UNAVAILABLE", new
                    {
                        relativeOutputPath,
                        absoluteOutputPath,
                        error = focusError
                    });
                }

                await Task.Delay(100);

                ScreenCapture.CaptureScreenshot(absoluteOutputPath);

                FileInfo writtenInfo = new(absoluteOutputPath);
                if (writtenInfo.Exists && writtenInfo.Length > 0)
                {
                    return Response.Success("Game view captured successfully.", new
                    {
                        relativeOutputPath,
                        absoluteOutputPath,
                        fileSize = writtenInfo.Length,
                        wasPlaying,
                        wasPaused,
                        pauseApplied = parameters.PausePlayMode && wasPlaying && !wasPaused,
                        stepFrames
                    });
                }

                DateTime deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(250, parameters.WaitForFileTimeoutMs));
                while (DateTime.UtcNow < deadline)
                {
                    if (File.Exists(absoluteOutputPath))
                    {
                        FileInfo info = new(absoluteOutputPath);
                        if (info.Length > 0)
                        {
                            return Response.Success("Game view captured successfully.", new
                            {
                                relativeOutputPath,
                                absoluteOutputPath,
                                fileSize = info.Length,
                                wasPlaying,
                                wasPaused,
                                pauseApplied = parameters.PausePlayMode && wasPlaying && !wasPaused,
                                stepFrames
                            });
                        }
                    }

                    await Task.Delay(100);
                }

                return Response.Error("CAPTURE_TIMEOUT", new
                {
                    relativeOutputPath,
                    absoluteOutputPath,
                    wasPlaying,
                    wasPaused
                });
            }
            finally
            {
                if (parameters.PausePlayMode && wasPlaying && !wasPaused && EditorApplication.isPaused)
                {
                    EditorApplication.isPaused = false;
                }
            }
        }

        static object BuildRuntimeLayoutElement(
            RectTransform rectTransform,
            string text,
            string[] elementTypes,
            bool includeScreenBounds,
            int screenWidth,
            int screenHeight,
            out string[] warnings)
        {
            UiDiagnosticsHelper.TryGetScreenRect(rectTransform, out Rect screenRect, out _, out _);
            Canvas canvas = rectTransform.GetComponentInParent<Canvas>(true);
            Graphic graphic = rectTransform.GetComponent<Graphic>();
            Image image = rectTransform.GetComponent<Image>();
            Selectable selectable = rectTransform.GetComponent<Selectable>();
            Button button = rectTransform.GetComponent<Button>();
            Slider slider = rectTransform.GetComponent<Slider>();
            Toggle toggle = rectTransform.GetComponent<Toggle>();
            warnings = GetRuntimeLayoutWarnings(rectTransform, screenRect, screenWidth, screenHeight);

            return new
            {
                name = rectTransform.name,
                path = UiDiagnosticsHelper.GetHierarchyPath(rectTransform),
                activeSelf = rectTransform.gameObject.activeSelf,
                activeInHierarchy = rectTransform.gameObject.activeInHierarchy,
                canvasPath = canvas != null ? UiDiagnosticsHelper.GetHierarchyPath(canvas.transform) : string.Empty,
                renderMode = canvas != null ? canvas.rootCanvas.renderMode.ToString() : null,
                elementTypes,
                rect = includeScreenBounds ? ToRectObject(screenRect) : null,
                text,
                interactable = selectable != null ? selectable.interactable : (bool?)null,
                spriteName = image != null && image.sprite != null ? image.sprite.name : null,
                raycastTarget = graphic != null ? graphic.raycastTarget : (bool?)null,
                blocksRaycasts = graphic != null && graphic.raycastTarget,
                hasButton = button != null,
                sliderValue = slider != null ? slider.value : (float?)null,
                sliderMinValue = slider != null ? slider.minValue : (float?)null,
                sliderMaxValue = slider != null ? slider.maxValue : (float?)null,
                toggleIsOn = toggle != null ? toggle.isOn : (bool?)null,
                warnings
            };
        }

        static string[] GetRuntimeLayoutWarnings(RectTransform rectTransform, Rect screenRect, int screenWidth, int screenHeight)
        {
            var warnings = new List<string>();
            if (screenRect.width <= 0f || screenRect.height <= 0f)
            {
                warnings.Add("empty_screen_rect");
            }

            if (screenWidth > 0 && screenHeight > 0 &&
                (screenRect.xMax < 0f || screenRect.xMin > screenWidth || screenRect.yMax < 0f || screenRect.yMin > screenHeight))
            {
                warnings.Add("offscreen");
            }

            RectMask2D mask = rectTransform.GetComponentInParent<RectMask2D>(true);
            if (mask != null && mask.transform is RectTransform maskRectTransform &&
                UiDiagnosticsHelper.TryGetScreenRect(maskRectTransform, out Rect maskRect, out _, out _))
            {
                if (!maskRect.Overlaps(screenRect))
                {
                    warnings.Add("outside_parent_rect_mask");
                }
                else if (!ContainsRect(maskRect, screenRect))
                {
                    warnings.Add("partially_clipped_by_parent_rect_mask");
                }
            }

            return warnings.ToArray();
        }

        static object BuildGameObjectSnapshot(GameObject gameObject)
        {
            try
            {
                if (gameObject == null)
                {
                    return null;
                }

                return new
                {
                    name = gameObject.name,
                    path = UiDiagnosticsHelper.GetHierarchyPath(gameObject.transform),
                    activeSelf = gameObject.activeSelf,
                    activeInHierarchy = gameObject.activeInHierarchy
                };
            }
            catch (MissingReferenceException)
            {
                return new
                {
                    destroyed = true
                };
            }
        }

        static object InvokeClick(GameObject target)
        {
            Button button = GetComponentOnTargetOrParent<Button>(target);
            GameObject dispatchTarget = button != null ? button.gameObject : target;
            RectTransform rectTransform = dispatchTarget.transform as RectTransform;
            Vector2 position = Vector2.zero;
            Rect screenRect = default;
            bool hasRect = rectTransform != null &&
                UiDiagnosticsHelper.TryGetScreenRect(rectTransform, out screenRect, out _, out _);
            if (hasRect)
            {
                position = screenRect.center;
            }

            var eventData = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                clickCount = 1,
                clickTime = Time.unscaledTime,
                position = position,
                pointerPressRaycast = new RaycastResult
                {
                    gameObject = dispatchTarget,
                    screenPosition = position
                }
            };

            bool pointerDown = ExecuteEvents.Execute(dispatchTarget, eventData, ExecuteEvents.pointerDownHandler);
            bool pointerUp = ExecuteEvents.Execute(dispatchTarget, eventData, ExecuteEvents.pointerUpHandler);
            bool pointerClick = ExecuteEvents.Execute(dispatchTarget, eventData, ExecuteEvents.pointerClickHandler);
            bool onClickFallback = false;
            if (!pointerClick && button != null && button.interactable)
            {
                button.onClick.Invoke();
                onClickFallback = true;
            }

            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(dispatchTarget, eventData);
            }

            return new
            {
                dispatchTarget = UiDiagnosticsHelper.GetHierarchyPath(dispatchTarget.transform),
                hasScreenRect = hasRect,
                screenPosition = ToVector2Object(position),
                buttonInteractable = button != null ? button.interactable : (bool?)null,
                pointerDown,
                pointerUp,
                pointerClick,
                onClickFallback,
                eventSent = pointerClick || onClickFallback || pointerDown || pointerUp
            };
        }

        static object SetSliderValue(GameObject target, float value)
        {
            Slider slider = GetComponentOnTargetOrParent<Slider>(target);
            if (slider == null)
            {
                return new
                {
                    success = false,
                    error = "Target does not have a Slider component."
                };
            }

            float before = slider.value;
            slider.value = Mathf.Clamp(value, slider.minValue, slider.maxValue);
            return new
            {
                success = true,
                control = UiDiagnosticsHelper.GetHierarchyPath(slider.transform),
                before,
                after = slider.value,
                slider.minValue,
                slider.maxValue,
                changed = !Mathf.Approximately(before, slider.value)
            };
        }

        static object SetToggleValue(GameObject target, bool value)
        {
            Toggle toggle = GetComponentOnTargetOrParent<Toggle>(target);
            if (toggle == null)
            {
                return new
                {
                    success = false,
                    error = "Target does not have a Toggle component."
                };
            }

            bool before = toggle.isOn;
            toggle.isOn = value;
            return new
            {
                success = true,
                control = UiDiagnosticsHelper.GetHierarchyPath(toggle.transform),
                before,
                after = toggle.isOn,
                changed = before != toggle.isOn
            };
        }

        static T GetComponentOnTargetOrParent<T>(GameObject target) where T : Component
        {
            if (target == null)
            {
                return null;
            }

            return target.GetComponent<T>() ?? target.GetComponentsInParent<T>(true).FirstOrDefault();
        }

        static bool GetActionSucceeded(object actionResult)
        {
            if (actionResult == null)
            {
                return false;
            }

            var successProperty = actionResult.GetType().GetProperty("success") ??
                actionResult.GetType().GetProperty("Success");
            return successProperty == null ||
                successProperty.PropertyType != typeof(bool) ||
                (bool)successProperty.GetValue(actionResult);
        }

        static HashSet<string> BuildTypeFilter(IEnumerable<string> elementTypes)
        {
            var filters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (elementTypes == null)
            {
                return filters;
            }

            foreach (string elementType in elementTypes)
            {
                if (string.IsNullOrWhiteSpace(elementType))
                {
                    continue;
                }

                string normalized = elementType.Trim();
                if (string.Equals(normalized, "all", StringComparison.OrdinalIgnoreCase))
                {
                    filters.Clear();
                    return filters;
                }

                filters.Add(normalized);
            }

            return filters;
        }

        static bool MatchesTypeFilter(IEnumerable<string> elementTypes, HashSet<string> filters)
        {
            return filters == null ||
                filters.Count == 0 ||
                elementTypes.Any(type => filters.Contains(type));
        }

        static string[] GetRuntimeElementTypes(RectTransform rectTransform)
        {
            var types = new List<string> { "rectTransform" };
            if (rectTransform.GetComponent<Canvas>() != null)
                types.Add("canvas");
            if (rectTransform.GetComponent<Graphic>() != null)
                types.Add("graphic");
            if (rectTransform.GetComponent<Image>() != null)
                types.Add("image");
            if (!string.IsNullOrEmpty(GetUiText(rectTransform)))
                types.Add("text");
            if (rectTransform.GetComponent<Selectable>() != null)
                types.Add("selectable");
            if (rectTransform.GetComponent<Button>() != null)
                types.Add("button");
            if (rectTransform.GetComponent<Slider>() != null)
                types.Add("slider");
            if (rectTransform.GetComponent<Toggle>() != null)
                types.Add("toggle");
            return types.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        static string GetUiText(RectTransform rectTransform)
        {
            if (rectTransform == null)
            {
                return null;
            }

            Text text = rectTransform.GetComponent<Text>();
            if (text != null)
            {
                return text.text;
            }

            foreach (Component component in rectTransform.GetComponents<Component>())
            {
                if (component == null)
                {
                    continue;
                }

                Type type = component.GetType();
                if (type.FullName == null || type.FullName.IndexOf("TMPro", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var textProperty = type.GetProperty("text");
                if (textProperty != null && textProperty.PropertyType == typeof(string))
                {
                    return textProperty.GetValue(component)?.ToString();
                }
            }

            return null;
        }

        static bool ContainsRect(Rect outer, Rect inner)
        {
            return outer.Contains(new Vector2(inner.xMin, inner.yMin)) &&
                outer.Contains(new Vector2(inner.xMin, inner.yMax)) &&
                outer.Contains(new Vector2(inner.xMax, inner.yMin)) &&
                outer.Contains(new Vector2(inner.xMax, inner.yMax));
        }

        static string NormalizeControlAction(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return "click";
            }

            return action.Trim().Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        }

        static object BuildHitResult(UiDiagnosticsHelper.UiElementHitInfo info)
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

        static bool TryNormalizeRelativeProjectPath(string projectRoot, string outputPath, out string relativeOutputPath, out string absoluteOutputPath)
        {
            relativeOutputPath = null;
            absoluteOutputPath = null;
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return false;
            }

            if (Path.IsPathRooted(outputPath))
            {
                string fullPath = Path.GetFullPath(outputPath);
                string normalizedRoot = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                relativeOutputPath = fullPath.Substring(normalizedRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                absoluteOutputPath = fullPath;
                return true;
            }

            relativeOutputPath = outputPath.Replace('\\', '/');
            absoluteOutputPath = Path.GetFullPath(Path.Combine(projectRoot, relativeOutputPath));
            return true;
        }

        static bool TryFocusGameView(out string error)
        {
            error = null;
            Type gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
            if (gameViewType == null)
            {
                error = "UnityEditor.GameView type could not be resolved.";
                return false;
            }

            EditorWindow gameView = EditorWindow.GetWindow(gameViewType, false, "Game", false);
            if (gameView == null)
            {
                error = "Game view window could not be created or resolved.";
                return false;
            }

            gameView.Focus();
            gameView.Repaint();
            return true;
        }

        static object ToVector2Object(Vector2 value) => new { x = value.x, y = value.y };

        static object ToVector3Object(Vector3 value) => new { x = value.x, y = value.y, z = value.z };

        static object ToRectObject(Rect value) => new { x = value.x, y = value.y, width = value.width, height = value.height };

        static object ShapePayload(string toolName, string summary, object data, object detailRef)
        {
            return ToolResultCompactor.ShapeJsonPayload(toolName, summary, data, detailRef);
        }
    }
}
