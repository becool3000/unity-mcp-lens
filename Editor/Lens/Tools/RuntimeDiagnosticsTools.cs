using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Tools.Parameters;
using UnityEditor;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class RuntimeDiagnosticsTools
    {
        sealed class MeasurementSnapshot
        {
            public Bounds? SpriteLocalBounds;
            public Bounds? SpriteWorldBounds;
            public Bounds? RendererBounds;
            public object ColliderData;
            public Rect? ScreenRect;
            public float ActualDiameter;
        }

        sealed class PresentationSample
        {
            public Vector3 RootLocalScale;
            public Vector3 RendererLocalScale;
            public Vector3 RootRotationEuler;
            public Vector3 RendererRotationEuler;
            public Color? Color;
            public float ActualDiameter;
        }

        public const string GetVisualBoundsSnapshotDescription = @"Returns a generic runtime visual-bounds snapshot for a live scene object.

Args:
    Target: Target runtime GameObject, hierarchy path, or instance id.
    SearchMethod: How to find the target ('by_name', 'by_id', 'by_path').
    IncludeInactive: Include inactive objects when resolving targets.
    CameraTarget: Optional camera GameObject used to compute screen-space footprint.
    CameraSearchMethod: How to find the optional camera target ('by_name', 'by_id', 'by_path').
    ReferenceTarget: Optional reference GameObject used to compute ratio versus another runtime object.
    ReferenceSearchMethod: How to find the optional reference target ('by_name', 'by_id', 'by_path').
    IncludeOwnership: Include renderer-scale, baseline-field, tint, flip, sprite, and rotation ownership details.
    SampleOverTime: Sample the target over a short duration to detect pulsing scale, rotation, or color changes.
    SampleDurationMs: Duration for the optional time sample in milliseconds.
    SampleIntervalMs: Delay between time-sample captures in milliseconds.

Returns:
    Dictionary with success/message/data. Data contains transform scale, sprite bounds, renderer bounds, collider radius or bounds, screen-space pixel footprint, optional ownership data, and optional time-sampled presentation changes.";

        [McpTool("Unity.Runtime.GetVisualBoundsSnapshot", GetVisualBoundsSnapshotDescription, Groups = new[] { "runtime", "diagnostics" }, EnabledByDefault = true)]
        public static async Task<object> GetVisualBoundsSnapshot(VisualBoundsSnapshotParams parameters)
        {
            parameters ??= new VisualBoundsSnapshotParams();
            if (string.IsNullOrWhiteSpace(parameters.Target))
            {
                return Response.Error("Target is required.");
            }

            if (!TryResolveGameObject(parameters.Target, parameters.SearchMethod, parameters.IncludeInactive, out GameObject targetGo))
            {
                return Response.Error($"Target '{parameters.Target}' could not be resolved.");
            }

            GameObject referenceGo = null;
            if (!string.IsNullOrWhiteSpace(parameters.ReferenceTarget)
                && !TryResolveGameObject(parameters.ReferenceTarget, parameters.ReferenceSearchMethod, parameters.IncludeInactive, out referenceGo))
            {
                return Response.Error($"Reference target '{parameters.ReferenceTarget}' could not be resolved.");
            }

            Camera camera = ResolveCamera(parameters.CameraTarget, parameters.CameraSearchMethod, parameters.IncludeInactive, out string cameraLabel);
            Renderer renderer = FindFirstComponent<Renderer>(targetGo);
            SpriteRenderer spriteRenderer = FindFirstComponent<SpriteRenderer>(targetGo);
            Collider2D collider2D = FindFirstComponent<Collider2D>(targetGo);
            Collider collider3D = collider2D == null ? FindFirstComponent<Collider>(targetGo) : null;
            MeasurementSnapshot measurement = CaptureMeasurement(targetGo, renderer, spriteRenderer, collider2D, collider3D, camera);
            float referenceDiameter = referenceGo != null ? GetReferenceDiameter(referenceGo) : 0f;
            float? ratioVsReference = referenceGo != null && referenceDiameter > 0.0001f ? measurement.ActualDiameter / referenceDiameter : null;
            object ownership = parameters.IncludeOwnership ? BuildOwnershipData(targetGo, renderer, spriteRenderer, measurement) : null;
            object timeSample = parameters.SampleOverTime
                ? await CaptureTimeSampleAsync(targetGo, renderer, spriteRenderer, collider2D, collider3D, parameters)
                : null;

            return Response.Success($"Captured runtime visual bounds for '{targetGo.name}'.", new
            {
                target = new
                {
                    name = targetGo.name,
                    path = UiDiagnosticsHelper.GetHierarchyPath(targetGo.transform),
                    activeSelf = targetGo.activeSelf,
                    activeInHierarchy = targetGo.activeInHierarchy
                },
                camera = string.IsNullOrWhiteSpace(cameraLabel) ? null : cameraLabel,
                reference = referenceGo == null ? null : new
                {
                    name = referenceGo.name,
                    path = UiDiagnosticsHelper.GetHierarchyPath(referenceGo.transform),
                    diameter = referenceDiameter
                },
                transform = new
                {
                    localScale = ToVector3Object(targetGo.transform.localScale),
                    lossyScale = ToVector3Object(targetGo.transform.lossyScale),
                    position = ToVector3Object(targetGo.transform.position),
                    rotationEuler = ToVector3Object(targetGo.transform.eulerAngles)
                },
                sprite = spriteRenderer == null ? null : new
                {
                    rendererType = spriteRenderer.GetType().FullName,
                    spriteName = spriteRenderer.sprite != null ? spriteRenderer.sprite.name : string.Empty,
                    localBounds = measurement.SpriteLocalBounds.HasValue ? ToBoundsObject(measurement.SpriteLocalBounds.Value) : null,
                    worldBounds = measurement.SpriteWorldBounds.HasValue ? ToBoundsObject(measurement.SpriteWorldBounds.Value) : null,
                    aspectBaseline = spriteRenderer.sprite != null && spriteRenderer.sprite.bounds.size.y > 0.0001f
                        ? new
                        {
                            x = spriteRenderer.sprite.bounds.size.x / spriteRenderer.sprite.bounds.size.y,
                            y = 1f
                        }
                        : null
                },
                renderer = renderer == null ? null : new
                {
                    typeName = renderer.GetType().FullName,
                    bounds = measurement.RendererBounds.HasValue ? ToBoundsObject(measurement.RendererBounds.Value) : null
                },
                collider = measurement.ColliderData,
                screenSpace = measurement.ScreenRect.HasValue ? new
                {
                    rect = ToRectObject(measurement.ScreenRect.Value),
                    pixelWidth = measurement.ScreenRect.Value.width,
                    pixelHeight = measurement.ScreenRect.Value.height
                } : null,
                actualDiameter = measurement.ActualDiameter,
                ratioVsReference,
                ownership,
                timeSample
            });
        }

        static MeasurementSnapshot CaptureMeasurement(GameObject targetGo, Renderer renderer, SpriteRenderer spriteRenderer, Collider2D collider2D, Collider collider3D, Camera camera)
        {
            Bounds? spriteLocalBounds = TryGetSpriteLocalBounds(spriteRenderer, out Bounds localBounds) ? localBounds : null;
            Bounds? spriteWorldBounds = TryGetSpriteWorldBounds(spriteRenderer, out Bounds worldBounds) ? worldBounds : null;
            Bounds? rendererBounds = renderer != null ? renderer.bounds : null;
            object colliderData = BuildColliderData(collider2D, collider3D);
            Bounds? fallbackBounds = rendererBounds ?? spriteWorldBounds;
            if (!fallbackBounds.HasValue && TryGetColliderBounds(collider2D, collider3D, out Bounds colliderBounds))
            {
                fallbackBounds = colliderBounds;
            }

            Rect? screenRect = TryGetScreenRect(camera, fallbackBounds, out Rect footprintRect) ? footprintRect : null;
            return new MeasurementSnapshot
            {
                SpriteLocalBounds = spriteLocalBounds,
                SpriteWorldBounds = spriteWorldBounds,
                RendererBounds = rendererBounds,
                ColliderData = colliderData,
                ScreenRect = screenRect,
                ActualDiameter = GetReferenceDiameter(rendererBounds, spriteWorldBounds, collider2D, collider3D)
            };
        }

        static bool TryResolveGameObject(string target, string searchMethod, bool includeInactive, out GameObject result)
        {
            var findParams = new JObject
            {
                ["search_inactive"] = includeInactive
            };
            result = ObjectsHelper.FindObject(target, searchMethod, findParams);
            return result != null;
        }

        static Camera ResolveCamera(string cameraTarget, string searchMethod, bool includeInactive, out string label)
        {
            label = string.Empty;
            if (!string.IsNullOrWhiteSpace(cameraTarget)
                && TryResolveGameObject(cameraTarget, searchMethod, includeInactive, out GameObject cameraGo))
            {
                Camera resolved = FindFirstComponent<Camera>(cameraGo);
                if (resolved != null)
                {
                    label = UiDiagnosticsHelper.GetHierarchyPath(resolved.transform);
                    return resolved;
                }
            }

            if (Camera.main != null)
            {
                label = UiDiagnosticsHelper.GetHierarchyPath(Camera.main.transform);
                return Camera.main;
            }

            Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null && cameras[i].enabled)
                {
                    label = UiDiagnosticsHelper.GetHierarchyPath(cameras[i].transform);
                    return cameras[i];
                }
            }

            if (cameras.Length > 0 && cameras[0] != null)
            {
                label = UiDiagnosticsHelper.GetHierarchyPath(cameras[0].transform);
                return cameras[0];
            }

            return null;
        }

        static T FindFirstComponent<T>(GameObject targetGo)
            where T : Component
        {
            if (targetGo == null)
            {
                return null;
            }

            T component = targetGo.GetComponent<T>();
            if (component != null)
            {
                return component;
            }

            T[] children = targetGo.GetComponentsInChildren<T>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] != null)
                {
                    return children[i];
                }
            }

            return null;
        }

        static bool TryGetSpriteLocalBounds(SpriteRenderer renderer, out Bounds bounds)
        {
            bounds = default;
            if (renderer == null || renderer.sprite == null)
            {
                return false;
            }

            bounds = renderer.sprite.bounds;
            return true;
        }

        static bool TryGetSpriteWorldBounds(SpriteRenderer renderer, out Bounds bounds)
        {
            bounds = default;
            if (!TryGetSpriteLocalBounds(renderer, out Bounds localBounds))
            {
                return false;
            }

            Vector3 center = localBounds.center;
            Vector3 extents = localBounds.extents;
            Vector3[] corners =
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

            Matrix4x4 matrix = renderer.transform.localToWorldMatrix;
            Vector3 firstPoint = matrix.MultiplyPoint3x4(corners[0]);
            bounds = new Bounds(firstPoint, Vector3.zero);
            for (int i = 1; i < corners.Length; i++)
            {
                bounds.Encapsulate(matrix.MultiplyPoint3x4(corners[i]));
            }

            return true;
        }

        static object BuildColliderData(Collider2D collider2D, Collider collider3D)
        {
            if (collider2D != null)
            {
                return BuildCollider2DData(collider2D);
            }

            if (collider3D != null)
            {
                return BuildCollider3DData(collider3D);
            }

            return null;
        }

        static object BuildCollider2DData(Collider2D collider)
        {
            float? worldRadius = collider switch
            {
                CircleCollider2D circle => circle.radius * Mathf.Max(Mathf.Abs(circle.transform.lossyScale.x), Mathf.Abs(circle.transform.lossyScale.y)),
                CapsuleCollider2D capsule => Mathf.Max(capsule.bounds.extents.x, capsule.bounds.extents.y),
                _ => null
            };

            return new
            {
                typeName = collider.GetType().FullName,
                enabled = collider.enabled,
                isTrigger = collider.isTrigger,
                worldRadius,
                worldBounds = ToBoundsObject(collider.bounds)
            };
        }

        static object BuildCollider3DData(Collider collider)
        {
            float? worldRadius = collider switch
            {
                SphereCollider sphere => sphere.radius * Mathf.Max(Mathf.Abs(sphere.transform.lossyScale.x), Mathf.Abs(sphere.transform.lossyScale.y), Mathf.Abs(sphere.transform.lossyScale.z)),
                CapsuleCollider capsule => Mathf.Max(capsule.bounds.extents.x, capsule.bounds.extents.y, capsule.bounds.extents.z),
                _ => null
            };

            return new
            {
                typeName = collider.GetType().FullName,
                enabled = collider.enabled,
                isTrigger = collider.isTrigger,
                worldRadius,
                worldBounds = ToBoundsObject(collider.bounds)
            };
        }

        static bool TryGetColliderBounds(Collider2D collider2D, Collider collider3D, out Bounds bounds)
        {
            if (collider2D != null)
            {
                bounds = collider2D.bounds;
                return true;
            }

            if (collider3D != null)
            {
                bounds = collider3D.bounds;
                return true;
            }

            bounds = default;
            return false;
        }

        static bool TryGetScreenRect(Camera camera, Bounds? bounds, out Rect screenRect)
        {
            screenRect = default;
            if (camera == null || !bounds.HasValue)
            {
                return false;
            }

            Bounds value = bounds.Value;
            Vector3 center = value.center;
            Vector3 extents = value.extents;
            Vector3[] corners =
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

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            bool valid = false;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 screenPoint = camera.WorldToScreenPoint(corners[i]);
                if (screenPoint.z < 0f)
                {
                    continue;
                }

                valid = true;
                minX = Mathf.Min(minX, screenPoint.x);
                minY = Mathf.Min(minY, screenPoint.y);
                maxX = Mathf.Max(maxX, screenPoint.x);
                maxY = Mathf.Max(maxY, screenPoint.y);
            }

            if (!valid)
            {
                return false;
            }

            screenRect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }

        static object BuildOwnershipData(GameObject targetGo, Renderer renderer, SpriteRenderer spriteRenderer, MeasurementSnapshot measurement)
        {
            Transform rendererTransform = renderer != null ? renderer.transform : targetGo.transform;
            List<object> baselineFields = DetectBaselineFields(targetGo, rendererTransform);
            object primaryBaseline = baselineFields.Count > 0 ? baselineFields[0] : null;
            object derivedMultiplier = BuildDerivedMultiplierData(primaryBaseline, targetGo.transform, rendererTransform);
            TryGetRendererColor(renderer, spriteRenderer, out Color? color);

            return new
            {
                rootTransform = new
                {
                    path = UiDiagnosticsHelper.GetHierarchyPath(targetGo.transform),
                    localScale = ToVector3Object(targetGo.transform.localScale),
                    lossyScale = ToVector3Object(targetGo.transform.lossyScale),
                    localRotationEuler = ToVector3Object(targetGo.transform.localEulerAngles)
                },
                childRenderer = renderer == null ? null : new
                {
                    path = UiDiagnosticsHelper.GetHierarchyPath(rendererTransform),
                    localScale = ToVector3Object(rendererTransform.localScale),
                    lossyScale = ToVector3Object(rendererTransform.lossyScale),
                    localRotationEuler = ToVector3Object(rendererTransform.localEulerAngles)
                },
                detectedBaselineFields = baselineFields.Count > 0 ? baselineFields : null,
                effectiveAuthoredMultiplier = derivedMultiplier,
                presentation = new
                {
                    spriteName = spriteRenderer != null && spriteRenderer.sprite != null ? spriteRenderer.sprite.name : string.Empty,
                    color = color.HasValue ? ToColorObject(color.Value) : null,
                    flipX = spriteRenderer != null ? spriteRenderer.flipX : (bool?)null,
                    flipY = spriteRenderer != null ? spriteRenderer.flipY : (bool?)null,
                    rendererLocalRotationEuler = renderer != null ? ToVector3Object(rendererTransform.localEulerAngles) : null,
                    finalRendererBounds = measurement.RendererBounds.HasValue ? ToBoundsObject(measurement.RendererBounds.Value) : null,
                    finalScreenFootprint = measurement.ScreenRect.HasValue ? ToRectObject(measurement.ScreenRect.Value) : null
                }
            };
        }

        static async Task<object> CaptureTimeSampleAsync(GameObject targetGo, Renderer renderer, SpriteRenderer spriteRenderer, Collider2D collider2D, Collider collider3D, VisualBoundsSnapshotParams parameters)
        {
            int durationMs = Mathf.Clamp(parameters.SampleDurationMs, 50, 5000);
            int intervalMs = Mathf.Clamp(parameters.SampleIntervalMs, 10, 1000);
            var samples = new List<PresentationSample>();
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            double startedAt = EditorApplication.timeSinceStartup;
            double nextSampleAt = startedAt;

            void Complete()
            {
                EditorApplication.update -= OnEditorUpdate;
                tcs.TrySetResult(BuildTimeSampleData(samples, durationMs, startedAt, EditorApplication.timeSinceStartup));
            }

            void OnEditorUpdate()
            {
                double now = EditorApplication.timeSinceStartup;
                if (targetGo == null)
                {
                    Complete();
                    return;
                }

                if (samples.Count == 0 || now >= nextSampleAt)
                {
                    samples.Add(CapturePresentationSample(targetGo, renderer, spriteRenderer, collider2D, collider3D));
                    nextSampleAt = now + (intervalMs / 1000.0);
                }

                if (((now - startedAt) * 1000.0) >= durationMs)
                {
                    Complete();
                }
            }

            EditorApplication.update += OnEditorUpdate;
            return await tcs.Task;
        }

        static object BuildTimeSampleData(List<PresentationSample> samples, int requestedDurationMs, double startedAt, double endedAt)
        {
            if (samples == null || samples.Count == 0)
            {
                return null;
            }

            PresentationSample first = samples[0];
            Vector3 minRootScale = first.RootLocalScale;
            Vector3 maxRootScale = first.RootLocalScale;
            Vector3 minRendererScale = first.RendererLocalScale;
            Vector3 maxRendererScale = first.RendererLocalScale;
            float minDiameter = first.ActualDiameter;
            float maxDiameter = first.ActualDiameter;
            Vector3 maxRootRotationDelta = Vector3.zero;
            Vector3 maxRendererRotationDelta = Vector3.zero;
            Color? startColor = first.Color;
            Color? endColor = samples[samples.Count - 1].Color;
            bool colorChanged = false;

            for (int i = 0; i < samples.Count; i++)
            {
                PresentationSample sample = samples[i];
                minRootScale = MinVector(minRootScale, sample.RootLocalScale);
                maxRootScale = MaxVector(maxRootScale, sample.RootLocalScale);
                minRendererScale = MinVector(minRendererScale, sample.RendererLocalScale);
                maxRendererScale = MaxVector(maxRendererScale, sample.RendererLocalScale);
                minDiameter = Mathf.Min(minDiameter, sample.ActualDiameter);
                maxDiameter = Mathf.Max(maxDiameter, sample.ActualDiameter);
                maxRootRotationDelta = MaxVector(maxRootRotationDelta, AbsDelta(sample.RootRotationEuler, first.RootRotationEuler));
                maxRendererRotationDelta = MaxVector(maxRendererRotationDelta, AbsDelta(sample.RendererRotationEuler, first.RendererRotationEuler));

                if (!colorChanged && startColor.HasValue && sample.Color.HasValue && !Approximately(startColor.Value, sample.Color.Value))
                {
                    colorChanged = true;
                }
            }

            return new
            {
                sampleCount = samples.Count,
                requestedDurationMs = requestedDurationMs,
                actualDurationMs = (endedAt - startedAt) * 1000.0,
                rootLocalScale = new
                {
                    min = ToVector3Object(minRootScale),
                    max = ToVector3Object(maxRootScale)
                },
                rendererLocalScale = new
                {
                    min = ToVector3Object(minRendererScale),
                    max = ToVector3Object(maxRendererScale)
                },
                actualDiameter = new
                {
                    min = minDiameter,
                    max = maxDiameter
                },
                rootRotationDeltaEuler = ToVector3Object(maxRootRotationDelta),
                rendererRotationDeltaEuler = ToVector3Object(maxRendererRotationDelta),
                color = new
                {
                    changed = colorChanged,
                    start = startColor.HasValue ? ToColorObject(startColor.Value) : null,
                    end = endColor.HasValue ? ToColorObject(endColor.Value) : null
                }
            };
        }

        static PresentationSample CapturePresentationSample(GameObject targetGo, Renderer renderer, SpriteRenderer spriteRenderer, Collider2D collider2D, Collider collider3D)
        {
            Transform rendererTransform = renderer != null ? renderer.transform : targetGo.transform;
            MeasurementSnapshot measurement = CaptureMeasurement(targetGo, renderer, spriteRenderer, collider2D, collider3D, null);
            TryGetRendererColor(renderer, spriteRenderer, out Color? color);

            return new PresentationSample
            {
                RootLocalScale = targetGo.transform.localScale,
                RendererLocalScale = rendererTransform.localScale,
                RootRotationEuler = targetGo.transform.localEulerAngles,
                RendererRotationEuler = rendererTransform.localEulerAngles,
                Color = color,
                ActualDiameter = measurement.ActualDiameter
            };
        }

        static List<object> DetectBaselineFields(GameObject targetGo, Transform rendererTransform)
        {
            var results = new List<object>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            AddBaselineFields(results, visited, targetGo);
            if (rendererTransform != null && rendererTransform.gameObject != targetGo)
            {
                AddBaselineFields(results, visited, rendererTransform.gameObject);
            }

            return results;
        }

        static void AddBaselineFields(List<object> results, HashSet<string> visited, GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            Component[] components = gameObject.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                FieldInfo[] fields = component.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
                {
                    FieldInfo field = fields[fieldIndex];
                    if (!ShouldInspectBaselineField(field))
                    {
                        continue;
                    }

                    object value = field.GetValue(component);
                    if (!TryConvertScaleVector(value, out Vector3 baselineScale))
                    {
                        continue;
                    }

                    string key = component.GetType().FullName + "|" + field.Name + "|" + UiDiagnosticsHelper.GetHierarchyPath(gameObject.transform);
                    if (!visited.Add(key))
                    {
                        continue;
                    }

                    results.Add(new
                    {
                        componentType = component.GetType().FullName,
                        hierarchyPath = UiDiagnosticsHelper.GetHierarchyPath(gameObject.transform),
                        fieldName = field.Name,
                        fieldType = field.FieldType.FullName,
                        value = ToInspectableObject(value),
                        normalizedScale = ToVector3Object(baselineScale)
                    });
                }
            }
        }

        static bool ShouldInspectBaselineField(FieldInfo field)
        {
            if (field == null || field.IsStatic)
            {
                return false;
            }

            if (!(field.IsPublic || field.GetCustomAttribute<SerializeField>() != null) ||
                field.GetCustomAttribute<NonSerializedAttribute>() != null)
            {
                return false;
            }

            string fieldName = field.Name ?? string.Empty;
            if (fieldName.IndexOf("baseline", StringComparison.OrdinalIgnoreCase) < 0 &&
                !fieldName.Equals("authoredScaleBaseline", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return field.FieldType == typeof(float) ||
                   field.FieldType == typeof(double) ||
                   field.FieldType == typeof(int) ||
                   field.FieldType == typeof(Vector2) ||
                   field.FieldType == typeof(Vector3);
        }

        static object BuildDerivedMultiplierData(object baselineFieldEntry, Transform rootTransform, Transform rendererTransform)
        {
            if (baselineFieldEntry == null)
            {
                return null;
            }

            PropertyInfo valueProperty = baselineFieldEntry.GetType().GetProperty("value");
            if (valueProperty == null)
            {
                return null;
            }

            object rawValue = valueProperty.GetValue(baselineFieldEntry);
            if (!TryConvertScaleVector(rawValue, out Vector3 baselineScale))
            {
                return null;
            }

            float baselineMax = Mathf.Max(0.0001f, GetMaxDimension(baselineScale));
            return new
            {
                baseline = ToVector3Object(baselineScale),
                rootLocalScaleVsBaseline = ToVector3Object(DivideVector(rootTransform.localScale, baselineScale)),
                childRendererLocalScaleVsBaseline = ToVector3Object(DivideVector(rendererTransform.localScale, baselineScale)),
                rootLocalScaleMaxRatio = GetMaxDimension(rootTransform.localScale) / baselineMax,
                childRendererLocalScaleMaxRatio = GetMaxDimension(rendererTransform.localScale) / baselineMax
            };
        }

        static bool TryGetRendererColor(Renderer renderer, SpriteRenderer spriteRenderer, out Color? color)
        {
            if (spriteRenderer != null)
            {
                color = spriteRenderer.color;
                return true;
            }

            if (renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_Color"))
            {
                color = renderer.sharedMaterial.color;
                return true;
            }

            color = null;
            return false;
        }

        static bool TryConvertScaleVector(object value, out Vector3 vector)
        {
            switch (value)
            {
                case Vector3 vector3:
                    vector = vector3;
                    return true;
                case Vector2 vector2:
                    vector = new Vector3(vector2.x, vector2.y, 1f);
                    return true;
                case float floatValue:
                    vector = new Vector3(floatValue, floatValue, floatValue);
                    return true;
                case double doubleValue:
                    float castDouble = (float)doubleValue;
                    vector = new Vector3(castDouble, castDouble, castDouble);
                    return true;
                case int intValue:
                    vector = new Vector3(intValue, intValue, intValue);
                    return true;
                default:
                    if (TryConvertAnonymousScaleObject(value, out vector))
                    {
                        return true;
                    }

                    vector = default;
                    return false;
            }
        }

        static bool TryConvertAnonymousScaleObject(object value, out Vector3 vector)
        {
            vector = default;
            if (value == null)
            {
                return false;
            }

            Type type = value.GetType();
            PropertyInfo xProperty = type.GetProperty("x");
            PropertyInfo yProperty = type.GetProperty("y");
            PropertyInfo zProperty = type.GetProperty("z");
            if (xProperty == null || yProperty == null)
            {
                return false;
            }

            float x = Convert.ToSingle(xProperty.GetValue(value));
            float y = Convert.ToSingle(yProperty.GetValue(value));
            float z = zProperty != null ? Convert.ToSingle(zProperty.GetValue(value)) : 1f;
            vector = new Vector3(x, y, z);
            return true;
        }

        static object ToInspectableObject(object value)
        {
            switch (value)
            {
                case Vector3 vector3:
                    return ToVector3Object(vector3);
                case Vector2 vector2:
                    return new { x = vector2.x, y = vector2.y };
                case Color color:
                    return ToColorObject(color);
                default:
                    return value;
            }
        }

        static Vector3 DivideVector(Vector3 value, Vector3 divisor)
        {
            return new Vector3(
                Mathf.Abs(divisor.x) > 0.0001f ? value.x / divisor.x : 0f,
                Mathf.Abs(divisor.y) > 0.0001f ? value.y / divisor.y : 0f,
                Mathf.Abs(divisor.z) > 0.0001f ? value.z / divisor.z : 0f);
        }

        static Vector3 MinVector(Vector3 a, Vector3 b)
        {
            return new Vector3(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Min(a.z, b.z));
        }

        static Vector3 MaxVector(Vector3 a, Vector3 b)
        {
            return new Vector3(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y), Mathf.Max(a.z, b.z));
        }

        static Vector3 AbsDelta(Vector3 a, Vector3 b)
        {
            return new Vector3(Mathf.Abs(Mathf.DeltaAngle(a.x, b.x)), Mathf.Abs(Mathf.DeltaAngle(a.y, b.y)), Mathf.Abs(Mathf.DeltaAngle(a.z, b.z)));
        }

        static bool Approximately(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.001f &&
                   Mathf.Abs(a.g - b.g) < 0.001f &&
                   Mathf.Abs(a.b - b.b) < 0.001f &&
                   Mathf.Abs(a.a - b.a) < 0.001f;
        }

        static float GetReferenceDiameter(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return 0f;
            }

            Renderer renderer = FindFirstComponent<Renderer>(gameObject);
            SpriteRenderer spriteRenderer = FindFirstComponent<SpriteRenderer>(gameObject);
            Collider2D collider2D = FindFirstComponent<Collider2D>(gameObject);
            Collider collider3D = collider2D == null ? FindFirstComponent<Collider>(gameObject) : null;
            Bounds? spriteWorldBounds = TryGetSpriteWorldBounds(spriteRenderer, out Bounds spriteBounds) ? spriteBounds : null;
            Bounds? rendererBounds = renderer != null ? renderer.bounds : null;
            return GetReferenceDiameter(rendererBounds, spriteWorldBounds, collider2D, collider3D);
        }

        static float GetReferenceDiameter(Bounds? rendererBounds, Bounds? spriteWorldBounds, Collider2D collider2D, Collider collider3D)
        {
            if (rendererBounds.HasValue)
            {
                return GetMaxDimension(rendererBounds.Value.size);
            }

            if (spriteWorldBounds.HasValue)
            {
                return GetMaxDimension(spriteWorldBounds.Value.size);
            }

            if (collider2D is CircleCollider2D circle2D)
            {
                return circle2D.radius * Mathf.Max(Mathf.Abs(circle2D.transform.lossyScale.x), Mathf.Abs(circle2D.transform.lossyScale.y)) * 2f;
            }

            if (collider3D is SphereCollider sphere3D)
            {
                return sphere3D.radius * Mathf.Max(Mathf.Abs(sphere3D.transform.lossyScale.x), Mathf.Abs(sphere3D.transform.lossyScale.y), Mathf.Abs(sphere3D.transform.lossyScale.z)) * 2f;
            }

            if (TryGetColliderBounds(collider2D, collider3D, out Bounds colliderBounds))
            {
                return GetMaxDimension(colliderBounds.size);
            }

            return 0f;
        }

        static float GetMaxDimension(Vector3 size)
        {
            return Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
        }

        static object ToVector3Object(Vector3 vector)
        {
            return new { x = vector.x, y = vector.y, z = vector.z };
        }

        static object ToColorObject(Color color)
        {
            return new { r = color.r, g = color.g, b = color.b, a = color.a };
        }

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
