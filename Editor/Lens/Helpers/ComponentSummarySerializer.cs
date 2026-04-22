using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Becool.UnityMcpLens.Editor.Helpers
{
    static class ComponentSummarySerializer
    {
        public static object GetSafeComponentData(Component component, bool includeNonPublicSerializedFields = true)
        {
            if (component == null)
            {
                return null;
            }

            if (TryGetCuratedSummary(component, out object curated))
            {
                return curated;
            }

            try
            {
                return GameObjectSerializer.GetComponentData(component, includeNonPublicSerializedFields);
            }
            catch (System.Exception ex)
            {
                return BuildFallbackSummary(component, ex.Message);
            }
        }

        static bool TryGetCuratedSummary(Component component, out object data)
        {
            switch (component)
            {
                case MeshFilter meshFilter:
                    data = new
                    {
                        typeName = component.GetType().FullName,
                        instanceID = UnityApiAdapter.GetObjectId(component),
                        summaryMode = "curated",
                        sharedMeshName = meshFilter.sharedMesh != null ? meshFilter.sharedMesh.name : null,
                        sharedMeshInstanceID = UnityApiAdapter.GetObjectIdOrZero(meshFilter.sharedMesh)
                    };
                    return true;
                case Collider collider:
                    data = new
                    {
                        typeName = component.GetType().FullName,
                        instanceID = UnityApiAdapter.GetObjectId(component),
                        summaryMode = "curated",
                        enabled = collider.enabled,
                        isTrigger = collider.isTrigger,
                        attachedRigidbodyInstanceID = UnityApiAdapter.GetObjectIdOrZero(collider.attachedRigidbody),
                        bounds = ToBoundsData(collider.bounds)
                    };
                    return true;
                case Collider2D collider2D:
                    data = new
                    {
                        typeName = component.GetType().FullName,
                        instanceID = UnityApiAdapter.GetObjectId(component),
                        summaryMode = "curated",
                        enabled = collider2D.enabled,
                        isTrigger = collider2D.isTrigger,
                        offset = ToVector2Data(collider2D.offset),
                        bounds = ToBoundsData(collider2D.bounds)
                    };
                    return true;
                case Renderer renderer:
                    data = new
                    {
                        typeName = component.GetType().FullName,
                        instanceID = UnityApiAdapter.GetObjectId(component),
                        summaryMode = "curated",
                        enabled = renderer.enabled,
                        sortingLayerID = renderer.sortingLayerID,
                        sortingOrder = renderer.sortingOrder,
                        materialCount = renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 0,
                        bounds = ToBoundsData(renderer.bounds)
                    };
                    return true;
                case RectTransform rectTransform:
                    data = new
                    {
                        typeName = component.GetType().FullName,
                        instanceID = UnityApiAdapter.GetObjectId(component),
                        summaryMode = "curated",
                        anchorMin = ToVector2Data(rectTransform.anchorMin),
                        anchorMax = ToVector2Data(rectTransform.anchorMax),
                        pivot = ToVector2Data(rectTransform.pivot),
                        sizeDelta = ToVector2Data(rectTransform.sizeDelta),
                        anchoredPosition = ToVector2Data(rectTransform.anchoredPosition),
                        localScale = ToVector3Data(rectTransform.localScale)
                    };
                    return true;
                case Canvas canvas:
                    data = new
                    {
                        typeName = component.GetType().FullName,
                        instanceID = UnityApiAdapter.GetObjectId(component),
                        summaryMode = "curated",
                        renderMode = canvas.renderMode.ToString(),
                        sortingOrder = canvas.sortingOrder,
                        overrideSorting = canvas.overrideSorting,
                        planeDistance = canvas.planeDistance,
                        scaleFactor = canvas.scaleFactor
                    };
                    return true;
                case Graphic graphic:
                    data = new
                    {
                        typeName = component.GetType().FullName,
                        instanceID = UnityApiAdapter.GetObjectId(component),
                        summaryMode = "curated",
                        enabled = graphic.enabled,
                        raycastTarget = graphic.raycastTarget,
                        depth = graphic.depth,
                        color = ToColorData(graphic.color)
                    };
                    return true;
                default:
                    data = null;
                    return false;
            }
        }

        static object ToVector2Data(Vector2 value)
        {
            return new
            {
                x = value.x,
                y = value.y
            };
        }

        static object ToVector3Data(Vector3 value)
        {
            return new
            {
                x = value.x,
                y = value.y,
                z = value.z
            };
        }

        static object ToBoundsData(Bounds value)
        {
            return new
            {
                center = ToVector3Data(value.center),
                size = ToVector3Data(value.size),
                extents = ToVector3Data(value.extents)
            };
        }

        static object ToColorData(Color value)
        {
            return new
            {
                r = value.r,
                g = value.g,
                b = value.b,
                a = value.a
            };
        }

        static object BuildFallbackSummary(Component component, string error)
        {
            var properties = new Dictionary<string, object>
            {
                ["name"] = component.name,
                ["gameObjectInstanceID"] = UnityApiAdapter.GetObjectIdOrZero(component.gameObject)
            };

            if (component is Behaviour behaviour)
            {
                properties["enabled"] = behaviour.enabled;
            }

            return new
            {
                typeName = component.GetType().FullName,
                instanceID = UnityApiAdapter.GetObjectId(component),
                summaryMode = "fallback",
                error,
                properties
            };
        }
    }
}
