using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.AI.MCP.Editor.Helpers
{
    static class UiDiagnosticsHelper
    {
        internal sealed class UiElementHitInfo
        {
            public string Path { get; init; }
            public string CanvasPath { get; init; }
            public RectTransform RectTransform { get; init; }
            public Graphic Graphic { get; init; }
            public Canvas Canvas { get; init; }
            public Rect ScreenRect { get; init; }
            public Vector2[] ScreenCorners { get; init; }
            public Vector3[] WorldCorners { get; init; }
            public int SortingOrder { get; init; }
            public int Depth { get; init; }
            public bool RaycastTarget { get; init; }
            public bool BlocksRaycasts { get; init; }
            public bool Active { get; init; }
        }

        public static IEnumerable<GameObject> ResolveUiRoots(string target, string searchMethod, bool includeInactive)
        {
            if (!string.IsNullOrWhiteSpace(target))
            {
                var findParams = new JObject
                {
                    ["search_inactive"] = includeInactive
                };
                GameObject targetObject = ObjectsHelper.FindObject(target, searchMethod, findParams);
                if (targetObject != null)
                {
                    yield return targetObject;
                }

                yield break;
            }

            FindObjectsInactive inactiveMode = includeInactive
                ? FindObjectsInactive.Include
                : FindObjectsInactive.Exclude;
            foreach (Canvas canvas in UnityEngine.Object.FindObjectsByType<Canvas>(inactiveMode, FindObjectsSortMode.None))
            {
                if (canvas != null && canvas.isRootCanvas)
                {
                    yield return canvas.gameObject;
                }
            }
        }

        public static IEnumerable<RectTransform> EnumerateRectTransforms(GameObject root, bool includeChildren, bool includeInactive)
        {
            if (root == null)
            {
                yield break;
            }

            if (includeChildren)
            {
                foreach (RectTransform rectTransform in root.GetComponentsInChildren<RectTransform>(includeInactive))
                {
                    if (rectTransform != null)
                    {
                        yield return rectTransform;
                    }
                }

                yield break;
            }

            if (root.transform is RectTransform rootRectTransform)
            {
                yield return rootRectTransform;
            }
        }

        public static IEnumerable<UiElementHitInfo> EnumerateGraphics(GameObject root, bool includeChildren, bool includeInactive)
        {
            if (root == null)
            {
                yield break;
            }

            IEnumerable<Graphic> graphics = includeChildren
                ? root.GetComponentsInChildren<Graphic>(includeInactive)
                : root.GetComponents<Graphic>();

            foreach (Graphic graphic in graphics)
            {
                if (graphic == null || graphic.rectTransform == null)
                {
                    continue;
                }

                bool active = graphic.gameObject.activeInHierarchy;
                if (!includeInactive && !active)
                {
                    continue;
                }

                if (!TryGetScreenRect(graphic.rectTransform, out Rect screenRect, out Vector3[] worldCorners, out Vector2[] screenCorners))
                {
                    continue;
                }

                Canvas canvas = graphic.canvas != null ? graphic.canvas.rootCanvas : graphic.GetComponentInParent<Canvas>(true);
                yield return new UiElementHitInfo
                {
                    Path = GetHierarchyPath(graphic.rectTransform),
                    CanvasPath = canvas != null ? GetHierarchyPath(canvas.transform) : string.Empty,
                    RectTransform = graphic.rectTransform,
                    Graphic = graphic,
                    Canvas = canvas,
                    ScreenRect = screenRect,
                    ScreenCorners = screenCorners,
                    WorldCorners = worldCorners,
                    SortingOrder = canvas != null ? canvas.sortingOrder : 0,
                    Depth = graphic.depth,
                    RaycastTarget = graphic.raycastTarget,
                    BlocksRaycasts = GraphicAllowsRaycast(graphic),
                    Active = active && graphic.enabled,
                };
            }
        }

        public static bool TryGetScreenRect(RectTransform rectTransform, out Rect screenRect, out Vector3[] worldCorners, out Vector2[] screenCorners)
        {
            screenRect = default;
            worldCorners = null;
            screenCorners = null;
            if (rectTransform == null)
            {
                return false;
            }

            Canvas canvas = rectTransform.GetComponentInParent<Canvas>(true);
            Camera eventCamera = GetEventCamera(canvas);

            worldCorners = new Vector3[4];
            rectTransform.GetWorldCorners(worldCorners);
            screenCorners = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                screenCorners[i] = RectTransformUtility.WorldToScreenPoint(eventCamera, worldCorners[i]);
            }

            float minX = screenCorners.Min(corner => corner.x);
            float maxX = screenCorners.Max(corner => corner.x);
            float minY = screenCorners.Min(corner => corner.y);
            float maxY = screenCorners.Max(corner => corner.y);
            screenRect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }

        public static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            return transform.parent == null
                ? transform.name
                : GetHierarchyPath(transform.parent) + "/" + transform.name;
        }

        public static Camera GetEventCamera(Canvas canvas)
        {
            if (canvas == null)
            {
                return Camera.main;
            }

            Canvas rootCanvas = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
            if (rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            return rootCanvas.worldCamera != null ? rootCanvas.worldCamera : Camera.main;
        }

        static bool GraphicAllowsRaycast(Graphic graphic)
        {
            if (graphic == null || !graphic.raycastTarget || !graphic.enabled || !graphic.gameObject.activeInHierarchy)
            {
                return false;
            }

            bool ignoreParentGroups = false;
            foreach (CanvasGroup canvasGroup in graphic.GetComponentsInParent<CanvasGroup>(true))
            {
                if (!canvasGroup.enabled)
                {
                    continue;
                }

                if (!canvasGroup.blocksRaycasts)
                {
                    return false;
                }

                if (canvasGroup.ignoreParentGroups)
                {
                    ignoreParentGroups = true;
                }

                if (ignoreParentGroups)
                {
                    break;
                }
            }

            return true;
        }
    }
}
