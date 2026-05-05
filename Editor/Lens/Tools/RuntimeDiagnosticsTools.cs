using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.Adapters.Unity;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Services;
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

        sealed class MouseInputQueueResult
        {
            public bool available;
            public bool attempted;
            public bool scheduled;
            public bool processed;
            public bool succeeded;
            public string deliveryMode;
            public string error;
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

        public const string PointerInputSmokeDescription = @"Runs a play-mode pointer input smoke check with UI and world hit evidence.

The tool is intended for verification, not authoring. It can attempt to queue a synthetic Input System mouse state through reflection, then samples observed mouse state, UI raycast hits, and optional world raycast evidence.";

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

        [McpSchema("Unity.PlayMode.PointerInputSmoke")]
        public static object GetPointerInputSmokeSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    screenX = new { type = "number", description = "Screen-space X coordinate in pixels." },
                    screenY = new { type = "number", description = "Screen-space Y coordinate in pixels." },
                    button = new { type = "string", description = "Mouse button name to press while queueing input: left, right, middle, or none." },
                    scrollX = new { type = "number", description = "Synthetic mouse wheel X scroll value to queue through MouseState.scroll." },
                    scrollY = new { type = "number", description = "Synthetic mouse wheel Y scroll value to queue through MouseState.scroll." },
                    queueInput = new { type = "boolean", description = "Queue a synthetic Input System mouse state before sampling." },
                    stepFrames = new { type = "integer", description = "Advance this many editor frames after queueing input when play mode is paused." },
                    advanceFrames = new { type = "integer", description = "Advance or wait this many runtime frames after queueing input before sampling state." },
                    settleMs = new { type = "integer", description = "Delay after queueing input before reading observed state." },
                    uiTarget = new { type = "string", description = "Optional UI root scope for raycast evidence." },
                    uiSearchMethod = new { type = "string", description = "How to find the optional UI root." },
                    includeInactive = new { type = "boolean", description = "Include inactive UI elements while evaluating raycast evidence." },
                    cameraTarget = new { type = "string", description = "Optional camera target used for world raycast evidence." },
                    cameraSearchMethod = new { type = "string", description = "How to find the optional camera target." },
                    layerMask = new { type = "integer", description = "Layer mask for optional physics raycast evidence. Defaults to all layers." },
                    stateTargets = new
                    {
                        type = "array",
                        description = "Optional runtime state targets to sample before and after input.",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                key = new { type = "string", description = "Stable key used by state assertions." },
                                target = new { type = "string", description = "Runtime GameObject, hierarchy path, or instance id." },
                                searchMethod = new { type = "string", description = "How to find the target." },
                                targetPath = new { type = "string", description = "Relative child path under the resolved target." },
                                includeInactive = new { type = "boolean", description = "Include inactive objects when resolving the target." },
                                componentType = new { type = "string", description = "Optional component type to read from the target object." },
                                componentIndex = new { type = "integer", description = "0-based component index when multiple matching components exist." },
                                memberPath = new { type = "string", description = "Field/property path to read via reflection." },
                                propertyPath = new { type = "string", description = "Serialized property path to read from the component." }
                            }
                        }
                    },
                    stateAssertions = new
                    {
                        type = "array",
                        description = "Optional assertions over sampled runtime state.",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                type = new { type = "string", description = "Assertion type: changed, equals, not_equals, contains, greater_than, or less_than." },
                                targetKey = new { type = "string", description = "State target key this assertion evaluates." },
                                value = new { description = "Expected value for equals, not_equals, greater_than, or less_than." },
                                contains = new { type = "string", description = "Expected substring for contains." },
                                tolerance = new { type = "number", description = "Numeric comparison tolerance." }
                            }
                        }
                    }
                }
            };
        }

        [McpTool("Unity.PlayMode.PointerInputSmoke", PointerInputSmokeDescription, Groups = new[] { "runtime", "diagnostics" }, EnabledByDefault = true)]
        public static async Task<object> PointerInputSmoke(JObject @params)
        {
            PointerInputSmokeParams parameters = NormalizePointerInputSmokeParams(@params);
            var timing = new ToolOperationTiming("Unity.PlayMode.PointerInputSmoke", "pointer_input_smoke", 0);
            object data;
            string errorKind = null;
            bool success = true;

            try
            {
                using (timing.Measure("normalization"))
                {
                    parameters.StepFrames = Math.Max(0, parameters.StepFrames);
                    parameters.AdvanceFrames = Math.Max(0, parameters.AdvanceFrames);
                    parameters.SettleMs = Math.Max(0, parameters.SettleMs);
                }

                using (timing.Measure("service"))
                {
                    if (!EditorApplication.isPlaying)
                    {
                        success = false;
                        errorKind = "not_in_play_mode";
                    }
                }

                using (timing.Measure("adapter"))
                {
                    data = await BuildPointerInputSmokeData(parameters);
                }
            }
            catch (Exception ex)
            {
                success = false;
                errorKind = ex.GetType().Name;
                data = new { errorKind, error = ex.Message };
            }

            object response;
            using (timing.Measure("result_shaping"))
            {
                response = success
                    ? Response.Success("Completed pointer input smoke.", ToolResultCompactor.ShapeStructuredPayload(
                        "Unity.PlayMode.PointerInputSmoke",
                        data,
                        BuildPointerInputSmokeCompactData(data),
                        new { kind = "playmode_pointer_input_smoke_full_result" },
                        "playmode_pointer_input_smoke"))
                    : Response.Error("Pointer input smoke failed.", data);
                timing.SetResponseBytes(GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, errorKind);
            return response;
        }

        static PointerInputSmokeParams NormalizePointerInputSmokeParams(JObject parameters)
        {
            parameters ??= new JObject();
            return new PointerInputSmokeParams
            {
                ScreenX = GetFloat(parameters, 0f, "screenX", "ScreenX"),
                ScreenY = GetFloat(parameters, 0f, "screenY", "ScreenY"),
                Button = GetString(parameters, "button", "Button") ?? "left",
                ScrollX = GetFloat(parameters, 0f, "scrollX", "ScrollX"),
                ScrollY = GetFloat(parameters, 0f, "scrollY", "ScrollY"),
                QueueInput = GetBool(parameters, true, "queueInput", "QueueInput"),
                StepFrames = GetInt(parameters, 1, "stepFrames", "StepFrames"),
                AdvanceFrames = GetInt(parameters, 0, "advanceFrames", "AdvanceFrames"),
                SettleMs = GetInt(parameters, 100, "settleMs", "SettleMs"),
                UiTarget = GetString(parameters, "uiTarget", "UiTarget"),
                UiSearchMethod = GetString(parameters, "uiSearchMethod", "UiSearchMethod") ?? "by_name",
                IncludeInactive = GetBool(parameters, false, "includeInactive", "IncludeInactive"),
                CameraTarget = GetString(parameters, "cameraTarget", "CameraTarget"),
                CameraSearchMethod = GetString(parameters, "cameraSearchMethod", "CameraSearchMethod") ?? "by_name",
                LayerMask = GetInt(parameters, -1, "layerMask", "LayerMask"),
                StateTargets = GetToken(parameters, "stateTargets", "StateTargets")?.ToObject<PointerSmokeStateTarget[]>() ?? Array.Empty<PointerSmokeStateTarget>(),
                StateAssertions = GetToken(parameters, "stateAssertions", "StateAssertions")?.ToObject<PointerSmokeStateAssertion[]>() ?? Array.Empty<PointerSmokeStateAssertion>()
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

        static float GetFloat(JObject parameters, float fallback, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<float>();
        }

        static int GetInt(JObject parameters, int fallback, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<int>();
        }

        static bool GetBool(JObject parameters, bool fallback, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<bool>();
        }

        static async Task<object> BuildPointerInputSmokeData(PointerInputSmokeParams parameters)
        {
            Vector2 point = new(parameters.ScreenX, parameters.ScreenY);
            Vector2 scroll = new(parameters.ScrollX, parameters.ScrollY);
            var beforeState = CaptureStateTargets(parameters.StateTargets);
            var queue = TryQueueMouseState(point, scroll, parameters.Button, parameters.QueueInput);

            for (int i = 0; i < parameters.StepFrames && EditorApplication.isPlaying && EditorApplication.isPaused; i++)
            {
                EditorApplication.Step();
                await Task.Delay(50);
            }

            int framesToAdvance = Math.Max(parameters.AdvanceFrames, parameters.QueueInput ? 1 : 0);
            for (int i = 0; i < framesToAdvance && EditorApplication.isPlaying; i++)
            {
                if (EditorApplication.isPaused)
                    EditorApplication.Step();

                await Task.Delay(EditorApplication.isPaused ? 50 : 20);
            }

            if (parameters.SettleMs > 0)
                await Task.Delay(parameters.SettleMs);

            var observed = ReadObservedMouseState();
            var ui = BuildUiPointerEvidence(parameters, point);
            var world = BuildWorldPointerEvidence(parameters, point);
            var afterState = CaptureStateTargets(parameters.StateTargets);
            var state = BuildStateEvidence(beforeState, afterState, parameters.StateAssertions);
            bool inputPassed = !parameters.QueueInput || queue.succeeded;
            bool passed = EditorApplication.isPlaying && inputPassed && (JObject.FromObject(state)["passed"]?.Value<bool>() != false);

            return new
            {
                passed,
                editor = new
                {
                    isPlaying = EditorApplication.isPlaying,
                    isPaused = EditorApplication.isPaused,
                    stepFrames = parameters.StepFrames,
                    advanceFrames = parameters.AdvanceFrames,
                    settleMs = parameters.SettleMs
                },
                requested = new
                {
                    point = ToVector2Object(point),
                    scroll = ToVector2Object(scroll),
                    button = parameters.Button,
                    queueInput = parameters.QueueInput
                },
                inputSystem = new
                {
                    queue.available,
                    queue.attempted,
                    queue.scheduled,
                    queue.processed,
                    queue.succeeded,
                    queue.deliveryMode,
                    queue.error
                },
                observed,
                ui,
                world,
                state
            };
        }

        static MouseInputQueueResult TryQueueMouseState(Vector2 point, Vector2 scroll, string button, bool queueInput)
        {
            MouseInputQueueResult result = new()
            {
                deliveryMode = "not_requested"
            };

            Type inputSystemType = Type.GetType("UnityEngine.InputSystem.InputSystem,Unity.InputSystem");
            Type mouseType = Type.GetType("UnityEngine.InputSystem.Mouse,Unity.InputSystem");
            Type mouseStateType = Type.GetType("UnityEngine.InputSystem.LowLevel.MouseState,Unity.InputSystem");
            if (inputSystemType == null || mouseType == null || mouseStateType == null)
            {
                result.error = "Input System types are not loaded.";
                return result;
            }

            result.available = true;
            if (!queueInput)
                return result;

            result.attempted = true;
            object mouse = mouseType.GetProperty("current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (mouse == null)
            {
                result.error = "Mouse.current is null.";
                return result;
            }

            try
            {
                object state = Activator.CreateInstance(mouseStateType);
                SetFieldOrProperty(state, mouseStateType, "position", point);
                SetFieldOrProperty(state, mouseStateType, "delta", Vector2.zero);
                SetFieldOrProperty(state, mouseStateType, "scroll", scroll);

                MethodInfo withButton = mouseStateType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(method => method.Name == "WithButton" && method.GetParameters().Length >= 1);
                Type mouseButtonType = Type.GetType("UnityEngine.InputSystem.LowLevel.MouseButton,Unity.InputSystem");
                if (withButton != null && mouseButtonType != null && !string.Equals(button, "none", StringComparison.OrdinalIgnoreCase))
                {
                    object buttonValue = Enum.Parse(mouseButtonType, NormalizeMouseButton(button), ignoreCase: true);
                    state = withButton.GetParameters().Length == 1
                        ? withButton.Invoke(state, new[] { buttonValue })
                        : withButton.Invoke(state, new[] { buttonValue, true });
                }

                MethodInfo queueStateEvent = inputSystemType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(method => method.Name == "QueueStateEvent" && method.IsGenericMethodDefinition && method.GetParameters().Length >= 2);
                if (queueStateEvent == null)
                {
                    result.error = "InputSystem.QueueStateEvent<TState> could not be resolved.";
                    return result;
                }

                MethodInfo generic = queueStateEvent.MakeGenericMethod(mouseStateType);
                Action queueNow = () =>
                {
                    try
                    {
                        QueueMouseStateEvent(generic, mouse, state);
                        result.processed = true;
                        result.succeeded = true;
                    }
                    catch (Exception ex)
                    {
                        result.processed = true;
                        result.succeeded = false;
                        result.error = ex.InnerException?.Message ?? ex.Message;
                    }
                };

                EventInfo beforeUpdate = inputSystemType.GetEvent("onBeforeUpdate", BindingFlags.Public | BindingFlags.Static);
                if (beforeUpdate != null && EditorApplication.isPlaying)
                {
                    Action handler = null;
                    handler = () =>
                    {
                        beforeUpdate.RemoveEventHandler(null, handler);
                        queueNow();
                    };

                    beforeUpdate.AddEventHandler(null, handler);
                    result.scheduled = true;
                    result.deliveryMode = "input_system_on_before_update";
                    return result;
                }

                queueNow();
                inputSystemType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(method => method.Name == "Update" && method.GetParameters().Length == 0)
                    ?.Invoke(null, Array.Empty<object>());
                result.deliveryMode = "immediate_update";
                return result;
            }
            catch (Exception ex)
            {
                result.error = ex.InnerException?.Message ?? ex.Message;
                return result;
            }
        }

        static void QueueMouseStateEvent(MethodInfo genericQueueStateEvent, object mouse, object state)
        {
            var parameters = genericQueueStateEvent.GetParameters();
            object[] args = parameters.Length >= 3 ? new[] { mouse, state, (object)(-1d) } : new[] { mouse, state };
            genericQueueStateEvent.Invoke(null, args);
        }

        static void SetFieldOrProperty(object target, Type type, string name, object value)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }

            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property != null && property.CanWrite)
                property.SetValue(target, value);
        }

        static string NormalizeMouseButton(string button)
        {
            return (button ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "right" => "Right",
                "middle" => "Middle",
                _ => "Left"
            };
        }

        static object ReadObservedMouseState()
        {
            Type mouseType = Type.GetType("UnityEngine.InputSystem.Mouse,Unity.InputSystem");
            object mouse = mouseType?.GetProperty("current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (mouse == null)
                return new { available = mouseType != null, present = false };

            return new
            {
                available = true,
                present = true,
                position = ReadControlValue(mouseType.GetProperty("position")?.GetValue(mouse)),
                leftButton = ReadControlValue(mouseType.GetProperty("leftButton")?.GetValue(mouse)),
                rightButton = ReadControlValue(mouseType.GetProperty("rightButton")?.GetValue(mouse)),
                middleButton = ReadControlValue(mouseType.GetProperty("middleButton")?.GetValue(mouse)),
                scroll = ReadControlValue(mouseType.GetProperty("scroll")?.GetValue(mouse))
            };
        }

        static object ReadControlValue(object control)
        {
            if (control == null)
                return null;

            try
            {
                object value = control.GetType().GetMethod("ReadValue", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null)?.Invoke(control, Array.Empty<object>());
                return value switch
                {
                    Vector2 vector => ToVector2Object(vector),
                    float number => number,
                    bool flag => flag,
                    _ => value?.ToString()
                };
            }
            catch (Exception ex)
            {
                return new { error = ex.Message };
            }
        }

        static List<object> CaptureStateTargets(PointerSmokeStateTarget[] targets)
        {
            var rows = new List<object>();
            foreach (PointerSmokeStateTarget target in targets ?? Array.Empty<PointerSmokeStateTarget>())
            {
                rows.Add(CaptureStateTarget(target));
            }

            return rows;
        }

        static object CaptureStateTarget(PointerSmokeStateTarget target)
        {
            string key = target?.Key;
            if (target == null || string.IsNullOrWhiteSpace(target.Target))
            {
                return new
                {
                    key,
                    success = false,
                    error = "State target requires target."
                };
            }

            if (!TryResolveGameObject(target.Target, target.SearchMethod, target.IncludeInactive, out GameObject root))
            {
                return new
                {
                    key,
                    success = false,
                    error = $"Target '{target.Target}' could not be resolved."
                };
            }

            string targetPath = string.IsNullOrWhiteSpace(target.TargetPath) ? "." : target.TargetPath.Trim();
            Transform transform = targetPath == "." ? root.transform : root.transform.Find(targetPath);
            if (transform == null)
            {
                return new
                {
                    key,
                    success = false,
                    root = UiDiagnosticsHelper.GetHierarchyPath(root.transform),
                    error = $"TargetPath '{targetPath}' was not found."
                };
            }

            object readTarget = transform.gameObject;
            string componentTypeName = null;
            if (!string.IsNullOrWhiteSpace(target.ComponentType))
            {
                Type componentType = UnityComponentResolver.FindType(target.ComponentType);
                if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
                {
                    return new
                    {
                        key,
                        success = false,
                        path = UiDiagnosticsHelper.GetHierarchyPath(transform),
                        error = $"Component type '{target.ComponentType}' could not be resolved."
                    };
                }

                Component[] components = transform.GetComponents(componentType);
                int componentIndex = Math.Max(0, target.ComponentIndex);
                if (components == null || componentIndex >= components.Length || components[componentIndex] == null)
                {
                    return new
                    {
                        key,
                        success = false,
                        path = UiDiagnosticsHelper.GetHierarchyPath(transform),
                        error = $"Component '{target.ComponentType}' with index {componentIndex} was not found."
                    };
                }

                readTarget = components[componentIndex];
                componentTypeName = components[componentIndex].GetType().FullName;
            }

            bool readSucceeded;
            object value;
            string error = null;
            if (!string.IsNullOrWhiteSpace(target.PropertyPath) && readTarget is Component component)
            {
                readSucceeded = TryReadSerializedProperty(component, target.PropertyPath, out value, out error);
            }
            else if (!string.IsNullOrWhiteSpace(target.MemberPath))
            {
                readSucceeded = TryReadMemberPath(readTarget, target.MemberPath, out value, out error);
            }
            else
            {
                readSucceeded = true;
                value = DescribeInspectableObject(readTarget);
            }

            return new
            {
                key,
                success = readSucceeded,
                path = UiDiagnosticsHelper.GetHierarchyPath(transform),
                componentType = componentTypeName,
                memberPath = target.MemberPath,
                propertyPath = target.PropertyPath,
                value = NormalizeInspectableValue(value),
                error
            };
        }

        static object BuildStateEvidence(List<object> beforeRows, List<object> afterRows, PointerSmokeStateAssertion[] assertions)
        {
            var beforeByKey = RowsByKey(beforeRows);
            var afterByKey = RowsByKey(afterRows);
            var assertionRows = new List<object>();
            bool passed = true;

            foreach (PointerSmokeStateAssertion assertion in assertions ?? Array.Empty<PointerSmokeStateAssertion>())
            {
                object row = EvaluateStateAssertion(assertion, beforeByKey, afterByKey);
                assertionRows.Add(row);
                bool rowPassed = JObject.FromObject(row)["passed"]?.Value<bool>() == true;
                passed &= rowPassed;
            }

            return new
            {
                passed,
                targetCount = afterRows?.Count ?? 0,
                assertionCount = assertionRows.Count,
                before = beforeRows ?? new List<object>(),
                after = afterRows ?? new List<object>(),
                assertions = assertionRows.ToArray()
            };
        }

        static Dictionary<string, JObject> RowsByKey(IEnumerable<object> rows)
        {
            var result = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            foreach (object row in rows ?? Array.Empty<object>())
            {
                JObject obj = JObject.FromObject(row);
                string key = obj["key"]?.ToString();
                if (!string.IsNullOrWhiteSpace(key))
                    result[key] = obj;
            }

            return result;
        }

        static object EvaluateStateAssertion(PointerSmokeStateAssertion assertion, IReadOnlyDictionary<string, JObject> beforeByKey, IReadOnlyDictionary<string, JObject> afterByKey)
        {
            string type = (assertion?.Type ?? "changed").Trim().ToLowerInvariant();
            string key = assertion?.TargetKey;
            beforeByKey.TryGetValue(key ?? string.Empty, out JObject before);
            afterByKey.TryGetValue(key ?? string.Empty, out JObject after);
            JToken beforeValue = before?["value"];
            JToken afterValue = after?["value"];
            bool targetResolved = after?["success"]?.Value<bool>() == true;
            bool passed = false;
            string message = null;

            if (!targetResolved)
            {
                message = after?["error"]?.ToString() ?? $"State target '{key}' was not resolved.";
            }
            else
            {
                switch (type)
                {
                    case "equals":
                        passed = JTokenEquals(afterValue, assertion.Value, assertion.Tolerance);
                        break;
                    case "not_equals":
                        passed = !JTokenEquals(afterValue, assertion.Value, assertion.Tolerance);
                        break;
                    case "contains":
                        passed = (afterValue?.ToString() ?? string.Empty).IndexOf(assertion.Contains ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;
                        break;
                    case "greater_than":
                        passed = TryGetDouble(afterValue, out double greaterActual) && TryGetDouble(assertion.Value, out double greaterExpected) && greaterActual > greaterExpected - Math.Max(0, assertion.Tolerance);
                        break;
                    case "less_than":
                        passed = TryGetDouble(afterValue, out double lessActual) && TryGetDouble(assertion.Value, out double lessExpected) && lessActual < lessExpected + Math.Max(0, assertion.Tolerance);
                        break;
                    default:
                        passed = !JToken.DeepEquals(beforeValue, afterValue);
                        type = "changed";
                        break;
                }
            }

            return new
            {
                type,
                targetKey = key,
                passed,
                before = beforeValue,
                after = afterValue,
                expected = assertion?.Value,
                contains = assertion?.Contains,
                tolerance = assertion?.Tolerance ?? 0.001f,
                message
            };
        }

        static bool JTokenEquals(JToken actual, JToken expected, float tolerance)
        {
            if (TryGetDouble(actual, out double actualNumber) && TryGetDouble(expected, out double expectedNumber))
                return Math.Abs(actualNumber - expectedNumber) <= Math.Max(0, tolerance);

            return string.Equals(actual?.ToString(), expected?.ToString(), StringComparison.Ordinal);
        }

        static bool TryGetDouble(JToken token, out double value)
        {
            value = 0;
            if (token == null || token.Type == JTokenType.Null)
                return false;

            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
            {
                value = token.Value<double>();
                return true;
            }

            return double.TryParse(token.ToString(), out value);
        }

        static bool TryReadSerializedProperty(Component component, string propertyPath, out object value, out string error)
        {
            value = null;
            error = null;
            try
            {
                SerializedObject serializedObject = new(component);
                SerializedProperty property = serializedObject.FindProperty(propertyPath);
                if (property == null)
                {
                    error = $"Serialized property '{propertyPath}' was not found.";
                    return false;
                }

                value = ReadSerializedPropertyValue(property);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        static bool TryReadMemberPath(object target, string memberPath, out object value, out string error)
        {
            value = target;
            error = null;
            foreach (string segment in (memberPath ?? string.Empty).Split('.'))
            {
                if (value == null)
                {
                    error = $"Cannot read '{segment}' from null.";
                    return false;
                }

                Type type = value.GetType();
                FieldInfo field = type.GetField(segment, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    value = field.GetValue(value);
                    continue;
                }

                PropertyInfo property = type.GetProperty(segment, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null)
                {
                    value = property.GetValue(value);
                    continue;
                }

                error = $"Member '{segment}' was not found on '{type.FullName}'.";
                return false;
            }

            return true;
        }

        static object ReadSerializedPropertyValue(SerializedProperty property)
        {
            return property.propertyType switch
            {
                SerializedPropertyType.Boolean => property.boolValue,
                SerializedPropertyType.Integer => property.intValue,
                SerializedPropertyType.Float => property.floatValue,
                SerializedPropertyType.String => property.stringValue,
                SerializedPropertyType.Color => ToColorObject(property.colorValue),
                SerializedPropertyType.ObjectReference => DescribeUnityObject(property.objectReferenceValue),
                SerializedPropertyType.Enum => property.enumDisplayNames != null && property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length
                    ? property.enumDisplayNames[property.enumValueIndex]
                    : property.enumValueIndex,
                SerializedPropertyType.Vector2 => ToVector2Object(property.vector2Value),
                SerializedPropertyType.Vector3 => ToVector3Object(property.vector3Value),
                SerializedPropertyType.Rect => ToRectObject(property.rectValue),
                _ => property.ToString()
            };
        }

        static object DescribeInspectableObject(object value)
        {
            return value switch
            {
                Component component => DescribeUnityObject(component),
                GameObject gameObject => DescribeUnityObject(gameObject),
                UnityEngine.Object unityObject => DescribeUnityObject(unityObject),
                _ => value
            };
        }

        static object NormalizeInspectableValue(object value)
        {
            return value switch
            {
                Vector2 vector2 => ToVector2Object(vector2),
                Vector3 vector3 => ToVector3Object(vector3),
                Color color => ToColorObject(color),
                Rect rect => ToRectObject(rect),
                UnityEngine.Object unityObject => DescribeUnityObject(unityObject),
                _ => value
            };
        }

        static object DescribeUnityObject(UnityEngine.Object value)
        {
            if (value == null)
                return null;

            if (value is Component component)
            {
                return new
                {
                    type = component.GetType().FullName,
                    name = component.name,
                    hierarchyPath = UiDiagnosticsHelper.GetHierarchyPath(component.transform)
                };
            }

            if (value is GameObject gameObject)
            {
                return new
                {
                    type = gameObject.GetType().FullName,
                    name = gameObject.name,
                    hierarchyPath = UiDiagnosticsHelper.GetHierarchyPath(gameObject.transform)
                };
            }

            string assetPath = AssetDatabase.GetAssetPath(value);
            return new
            {
                type = value.GetType().FullName,
                name = value.name,
                assetPath = string.IsNullOrWhiteSpace(assetPath) ? null : assetPath
            };
        }

        static object BuildUiPointerEvidence(PointerInputSmokeParams parameters, Vector2 point)
        {
            var roots = UiDiagnosticsHelper.ResolveUiRoots(parameters.UiTarget, parameters.UiSearchMethod, parameters.IncludeInactive).ToList();
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
                .Take(10)
                .ToArray();
            var topBlocker = ordered.FirstOrDefault(info => info.Active && info.BlocksRaycasts);
            return new
            {
                rootCount = roots.Count,
                hitCount = ordered.Length,
                blocked = topBlocker != null,
                topHit = topBlocker == null ? null : BuildUiHit(topBlocker),
                hits = ordered.Select(BuildUiHit).ToArray()
            };
        }

        static object BuildWorldPointerEvidence(PointerInputSmokeParams parameters, Vector2 point)
        {
            Camera camera = ResolveCamera(parameters.CameraTarget, parameters.CameraSearchMethod, parameters.IncludeInactive, out string cameraLabel);
            if (camera == null)
                return new { camera = (string)null, hit = false };

            Ray ray = camera.ScreenPointToRay(point);
            bool hit3d = Physics.Raycast(ray, out RaycastHit hit, float.PositiveInfinity, parameters.LayerMask);
            RaycastHit2D hit2d = Physics2D.GetRayIntersection(ray, float.PositiveInfinity, parameters.LayerMask);
            return new
            {
                camera = cameraLabel,
                ray = new { origin = ToVector3Object(ray.origin), direction = ToVector3Object(ray.direction) },
                hit3d = hit3d ? new { path = UiDiagnosticsHelper.GetHierarchyPath(hit.collider.transform), point = ToVector3Object(hit.point), distance = hit.distance } : null,
                hit2d = hit2d.collider != null ? new { path = UiDiagnosticsHelper.GetHierarchyPath(hit2d.collider.transform), point = ToVector2Object(hit2d.point), distance = hit2d.distance } : null
            };
        }

        static object BuildUiHit(UiDiagnosticsHelper.UiElementHitInfo info)
        {
            return new
            {
                path = info.Path,
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

        static object BuildPointerInputSmokeCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            return new
            {
                passed = root["passed"],
                editor = root["editor"],
                requested = root["requested"],
                inputSystem = root["inputSystem"],
                observed = root["observed"],
                ui = new
                {
                    rootCount = root["ui"]?["rootCount"],
                    hitCount = root["ui"]?["hitCount"],
                    blocked = root["ui"]?["blocked"],
                    topHit = root["ui"]?["topHit"]
                },
                world = root["world"],
                state = new
                {
                    passed = root["state"]?["passed"],
                    targetCount = root["state"]?["targetCount"],
                    assertionCount = root["state"]?["assertionCount"],
                    assertions = root["state"]?["assertions"]
                }
            };
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

            Camera[] cameras = UnityApiAdapter.FindObjectsByType<Camera>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
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

        static object ToVector2Object(Vector2 vector)
        {
            return new { x = vector.x, y = vector.y };
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

        static int GetUtf8ByteCount(string value) => System.Text.Encoding.UTF8.GetByteCount(value ?? string.Empty);
    }
}
