using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Assistant.Editor.Context;
using Unity.AI.Assistant.Utils;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry;
using Unity.AI.MCP.Editor.Tools.Parameters;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.AI.MCP.Editor.Tools
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

                var screenContext = ScreenContextUtility.CaptureScreenContext(includeScreenshots: true, saveToFile: false);
                if (screenContext.Screenshot == null || screenContext.Screenshot.Length == 0)
                {
                    return Response.Error("CAPTURE_FAILED", new
                    {
                        relativeOutputPath,
                        absoluteOutputPath,
                        error = "Focused Game view capture returned no image data."
                    });
                }

                File.WriteAllBytes(absoluteOutputPath, screenContext.Screenshot);

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
            var serialized = Newtonsoft.Json.JsonConvert.SerializeObject(data, Newtonsoft.Json.Formatting.None);
            var rawBytes = PayloadBudgeting.GetUtf8ByteCount(serialized);
            if (rawBytes <= PayloadBudgetPolicy.MaxToolResultBytes)
            {
                PayloadStats.Record("tool_result", toolName, rawBytes, rawBytes, PayloadBudgeting.EstimateTokensFromBytes(rawBytes), PayloadBudgeting.ComputeSha256(serialized));
                return data;
            }

            var budgeted = PayloadBudgeting.CreateTextResult(summary, new { rawBytes }, serialized, detailRef, maxPreviewLines: 40, maxPreviewBytes: PayloadBudgetPolicy.MaxToolResultBytes);
            var previewBytes = PayloadBudgeting.GetUtf8ByteCount(budgeted.Preview);
            PayloadStats.Record("tool_result", toolName, rawBytes, previewBytes, PayloadBudgeting.EstimateTokensFromBytes(previewBytes), budgeted.Sha256);
            return budgeted;
        }
    }
}
