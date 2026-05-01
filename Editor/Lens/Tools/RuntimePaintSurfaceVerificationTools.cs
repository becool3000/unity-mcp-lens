#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
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
    public static class RuntimePaintSurfaceVerificationTools
    {
        const string ToolName = "Unity.PlayMode.VerifyPaintSurfaceInteraction";
        const string Description = @"Runs a configured play-mode paint-surface verification workflow.

Samples public component properties/methods before and after operations, runs UI-block/world-raycast/public-method/wait/step operations, and evaluates compact assertions for paint, stain, smudge, UI blocking, and layer/color deltas.";

        [McpSchema(ToolName)]
        public static object GetSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    controller = new { description = "Default controller component selector." },
                    surface = new { description = "Default paint surface component selector." },
                    camera = new { description = "Optional camera selector." },
                    uiRoot = new { description = "Optional UI root selector for hit evidence." },
                    samples = new { type = "array", description = "Samples to capture before and after operations." },
                    operations = new { type = "array", description = "Operations: ui_block_check, world_raycast_uv, invoke_method, wait, step_frames." },
                    assertions = new { type = "array", description = "Assertions over operation rows and before/after samples." }
                }
            };
        }

        [McpTool(ToolName, Description, "Verify Paint Surface Interaction", Groups = new[] { "runtime" }, EnabledByDefault = true)]
        public static async Task<object> VerifyPaintSurfaceInteraction(JObject parameters)
        {
            parameters ??= new JObject();
            var timing = new ToolOperationTiming(ToolName, "verify_paint_surface_interaction", PayloadBudgeting.GetUtf8ByteCount(parameters.ToString(Formatting.None)));
            object data;
            bool success = true;
            string errorKind = null;

            try
            {
                using (timing.Measure("normalization")) { }
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
                    data = await Run(parameters);
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
                    ? Response.Success("Completed paint-surface interaction verification.",
                        ToolResultCompactor.ShapeStructuredPayload(ToolName, data, BuildCompactData(data), new { kind = "paint_surface_verify_full_result" }, "paint_surface_verify"))
                    : Response.Error("Paint-surface interaction verification failed.", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, errorKind);
            return response;
        }

        static async Task<object> Run(JObject parameters)
        {
            JObject controllerSelector = parameters["controller"] as JObject ?? parameters["Controller"] as JObject;
            JObject surfaceSelector = parameters["surface"] as JObject ?? parameters["Surface"] as JObject;
            JObject cameraSelector = parameters["camera"] as JObject ?? parameters["Camera"] as JObject;
            JObject uiRootSelector = parameters["uiRoot"] as JObject ?? parameters["UiRoot"] as JObject;

            Component controller = ResolveOptionalComponent(controllerSelector);
            Component surface = ResolveOptionalComponent(surfaceSelector);
            Camera camera = ResolveCamera(cameraSelector);
            GameObject uiRoot = ResolveOptionalGameObject(uiRootSelector);

            var beforeSamples = CaptureSamples(parameters["samples"] as JArray ?? parameters["Samples"] as JArray, controller, surface);
            var operations = new List<object>();
            foreach (JObject operation in (parameters["operations"] as JArray ?? parameters["Operations"] as JArray ?? new JArray()).OfType<JObject>())
            {
                operations.Add(await RunOperation(operation, controller, surface, camera, uiRoot));
            }
            var afterSamples = CaptureSamples(parameters["samples"] as JArray ?? parameters["Samples"] as JArray, controller, surface);
            var assertionRows = EvaluateAssertions(parameters["assertions"] as JArray ?? parameters["Assertions"] as JArray, beforeSamples, afterSamples, operations);
            bool passed = assertionRows.All(row => JObject.FromObject(row)["passed"]?.Value<bool>() != false) &&
                operations.All(row => JObject.FromObject(row)["success"]?.Value<bool>() != false);

            return new
            {
                passed,
                editor = new { isPlaying = EditorApplication.isPlaying, isPaused = EditorApplication.isPaused },
                controller = DescribeComponent(controller),
                surface = DescribeComponent(surface),
                camera = camera != null ? UiDiagnosticsHelper.GetHierarchyPath(camera.transform) : null,
                beforeSamples,
                operations = operations.ToArray(),
                afterSamples,
                assertions = assertionRows.ToArray()
            };
        }

        static async Task<object> RunOperation(JObject operation, Component controller, Component surface, Camera camera, GameObject uiRoot)
        {
            string type = (operation["type"]?.ToString() ?? operation["Type"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            string key = operation["key"]?.ToString() ?? operation["Key"]?.ToString() ?? type;
            try
            {
                switch (type)
                {
                    case "ui_block_check":
                        return BuildUiBlockCheck(key, operation, uiRoot);
                    case "world_raycast_uv":
                        return BuildWorldRaycast(key, operation, camera, surface);
                    case "invoke_method":
                        return InvokeConfiguredMethod(key, operation, controller, surface);
                    case "wait":
                        int milliseconds = operation["milliseconds"]?.ToObject<int?>() ?? operation["Milliseconds"]?.ToObject<int?>() ?? 100;
                        await Task.Delay(Math.Max(0, milliseconds));
                        return new { key, type, success = true, milliseconds };
                    case "step_frames":
                        int frames = operation["frames"]?.ToObject<int?>() ?? operation["Frames"]?.ToObject<int?>() ?? 1;
                        for (int i = 0; i < Math.Max(0, frames); i++)
                        {
                            if (EditorApplication.isPaused)
                                EditorApplication.Step();
                            await Task.Delay(50);
                        }
                        return new { key, type, success = true, frames };
                    default:
                        return new { key, type, success = false, error = $"Unsupported operation type '{type}'." };
                }
            }
            catch (Exception ex)
            {
                return new { key, type, success = false, error = ex.InnerException?.Message ?? ex.Message };
            }
        }

        static object BuildUiBlockCheck(string key, JObject operation, GameObject uiRoot)
        {
            Vector2 point = ReadPoint(operation);
            var roots = uiRoot != null ? new[] { uiRoot } : UiDiagnosticsHelper.ResolveUiRoots(null, "by_name", includeInactive: false).ToArray();
            var hits = roots.SelectMany(root => UiDiagnosticsHelper.EnumerateGraphics(root, true, false))
                .Where(hit => hit.ScreenRect.Contains(point))
                .OrderByDescending(hit => hit.Active)
                .ThenByDescending(hit => hit.BlocksRaycasts)
                .ThenByDescending(hit => hit.SortingOrder)
                .ThenByDescending(hit => hit.Depth)
                .Take(12)
                .ToArray();
            var topBlocker = hits.FirstOrDefault(hit => hit.Active && hit.BlocksRaycasts);
            return new
            {
                key,
                type = "ui_block_check",
                success = true,
                point = ToVector2Object(point),
                blocked = topBlocker != null,
                topHit = topBlocker == null ? null : BuildUiHit(topBlocker),
                hitCount = hits.Length,
                hits = hits.Select(BuildUiHit).ToArray()
            };
        }

        static object BuildWorldRaycast(string key, JObject operation, Camera camera, Component surface)
        {
            if (camera == null)
                return new { key, type = "world_raycast_uv", success = false, error = "Camera could not be resolved." };

            Vector2 point = ReadPoint(operation);
            Ray ray = camera.ScreenPointToRay(point);
            bool hit3d = Physics.Raycast(ray, out RaycastHit hit, float.PositiveInfinity);
            bool matchesSurface = surface == null || (hit3d && hit.collider != null && hit.collider.GetComponentInParent(surface.GetType()) == surface);
            return new
            {
                key,
                type = "world_raycast_uv",
                success = true,
                point = ToVector2Object(point),
                hit = hit3d,
                matchesSurface,
                path = hit3d && hit.collider != null ? UiDiagnosticsHelper.GetHierarchyPath(hit.collider.transform) : null,
                worldPoint = hit3d ? ToVector3Object(hit.point) : null,
                uv = hit3d ? ToVector2Object(hit.textureCoord) : null,
                distance = hit3d ? hit.distance : 0f
            };
        }

        static object InvokeConfiguredMethod(string key, JObject operation, Component controller, Component surface)
        {
            Component target = ResolveOperationTarget(operation, controller, surface);
            if (target == null)
                return new { key, type = "invoke_method", success = false, error = "Operation target component could not be resolved." };

            string methodName = operation["methodName"]?.ToString() ?? operation["MethodName"]?.ToString();
            JArray args = operation["args"] as JArray ?? operation["Args"] as JArray ?? new JArray();
            MethodInfo method = FindCallableMethod(target.GetType(), methodName, args);
            if (method == null)
                return new { key, type = "invoke_method", success = false, error = $"Public method '{methodName}' with {args.Count} argument(s) was not found." };

            object[] converted = ConvertArguments(args, method.GetParameters());
            object value = method.Invoke(target, converted);
            return new
            {
                key,
                type = "invoke_method",
                success = true,
                target = DescribeComponent(target),
                methodName,
                returned = NormalizeValue(value)
            };
        }

        static Dictionary<string, JToken> CaptureSamples(JArray samples, Component controller, Component surface)
        {
            var results = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
            foreach (JObject sample in (samples ?? new JArray()).OfType<JObject>())
            {
                string key = sample["key"]?.ToString() ?? sample["Key"]?.ToString();
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                try
                {
                    Component target = ResolveOperationTarget(sample, controller, surface);
                    object value = ReadSampleValue(sample, target);
                    results[key] = JToken.FromObject(NormalizeValue(value) ?? new { value = (object)null });
                }
                catch (Exception ex)
                {
                    results[key] = JToken.FromObject(new { error = ex.InnerException?.Message ?? ex.Message });
                }
            }
            return results;
        }

        static object ReadSampleValue(JObject sample, Component target)
        {
            if (target == null)
                throw new InvalidOperationException("Sample target component could not be resolved.");

            string memberName = sample["propertyName"]?.ToString() ?? sample["fieldName"]?.ToString() ?? sample["memberName"]?.ToString();
            string methodName = sample["methodName"]?.ToString() ?? sample["MethodName"]?.ToString();
            if (!string.IsNullOrWhiteSpace(methodName))
            {
                JArray args = sample["args"] as JArray ?? sample["Args"] as JArray ?? new JArray();
                MethodInfo method = FindCallableMethod(target.GetType(), methodName, args);
                if (method == null)
                    throw new MissingMethodException(target.GetType().FullName, methodName);
                return method.Invoke(target, ConvertArguments(args, method.GetParameters()));
            }

            if (string.IsNullOrWhiteSpace(memberName))
                throw new ArgumentException("Sample requires propertyName, fieldName, memberName, or methodName.");

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            PropertyInfo property = target.GetType().GetProperty(memberName, flags);
            if (property != null && property.CanRead)
                return property.GetValue(target);

            FieldInfo field = target.GetType().GetField(memberName, flags);
            if (field != null)
                return field.GetValue(target);

            throw new MissingMemberException(target.GetType().FullName, memberName);
        }

        static object[] EvaluateAssertions(JArray assertions, Dictionary<string, JToken> before, Dictionary<string, JToken> after, List<object> operations)
        {
            var rows = new List<object>();
            var operationMap = operations.Select(JObject.FromObject).ToDictionary(row => row["key"]?.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            foreach (JObject assertion in (assertions ?? new JArray()).OfType<JObject>())
            {
                string type = (assertion["type"]?.ToString() ?? assertion["Type"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                string key = assertion["key"]?.ToString() ?? assertion["Key"]?.ToString() ?? assertion["sampleKey"]?.ToString() ?? assertion["SampleKey"]?.ToString();
                rows.Add(EvaluateAssertion(type, key, assertion, before, after, operationMap));
            }
            return rows.ToArray();
        }

        static object EvaluateAssertion(string type, string key, JObject assertion, Dictionary<string, JToken> before, Dictionary<string, JToken> after, Dictionary<string, JObject> operations)
        {
            bool passed;
            switch (type)
            {
                case "ui_blocked":
                    string operationKey = assertion["operationKey"]?.ToString() ?? key;
                    passed = operations.TryGetValue(operationKey ?? string.Empty, out JObject uiRow) && uiRow["blocked"]?.Value<bool>() == (assertion["expected"]?.ToObject<bool?>() ?? true);
                    return new { type, operationKey, passed, actual = uiRow?["blocked"] };
                case "world_hit_target":
                    operationKey = assertion["operationKey"]?.ToString() ?? key;
                    string contains = assertion["pathContains"]?.ToString() ?? assertion["PathContains"]?.ToString();
                    passed = operations.TryGetValue(operationKey ?? string.Empty, out JObject worldRow) &&
                        worldRow["hit"]?.Value<bool>() == true &&
                        (string.IsNullOrWhiteSpace(contains) || (worldRow["path"]?.ToString() ?? string.Empty).IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0);
                    return new { type, operationKey, passed, path = worldRow?["path"], uv = worldRow?["uv"] };
                case "layer_count_changed":
                    float minDelta = assertion["minDelta"]?.ToObject<float?>() ?? 1f;
                    float delta = Numeric(after, key) - Numeric(before, key);
                    passed = delta >= minDelta;
                    return new { type, sampleKey = key, passed, delta, minDelta, before = GetSample(before, key), after = GetSample(after, key) };
                case "color_delta_min":
                    float min = assertion["min"]?.ToObject<float?>() ?? assertion["threshold"]?.ToObject<float?>() ?? 0.01f;
                    float colorDelta = ColorDelta(GetSample(before, key), GetSample(after, key));
                    passed = colorDelta >= min;
                    return new { type, sampleKey = key, passed, delta = colorDelta, min };
                case "color_delta_max":
                    float max = assertion["max"]?.ToObject<float?>() ?? assertion["threshold"]?.ToObject<float?>() ?? 0.01f;
                    colorDelta = ColorDelta(GetSample(before, key), GetSample(after, key));
                    passed = colorDelta <= max;
                    return new { type, sampleKey = key, passed, delta = colorDelta, max };
                case "property_equals":
                    JToken expected = assertion["expected"] ?? assertion["Expected"];
                    passed = JToken.DeepEquals(GetSample(after, key), expected);
                    return new { type, sampleKey = key, passed, expected, actual = GetSample(after, key) };
                case "property_changed":
                    passed = !JToken.DeepEquals(GetSample(before, key), GetSample(after, key));
                    return new { type, sampleKey = key, passed, before = GetSample(before, key), after = GetSample(after, key) };
                default:
                    return new { type, sampleKey = key, passed = false, error = $"Unsupported assertion type '{type}'." };
            }
        }

        static JToken GetSample(Dictionary<string, JToken> samples, string key)
        {
            if (samples == null || string.IsNullOrWhiteSpace(key))
                return null;

            return samples.TryGetValue(key, out JToken value) ? value : null;
        }

        static Component ResolveOperationTarget(JObject spec, Component controller, Component surface)
        {
            string targetKind = (spec["targetKind"]?.ToString() ?? spec["TargetKind"]?.ToString() ?? spec["target"]?.ToString() ?? "controller").Trim().ToLowerInvariant();
            if (targetKind == "surface")
                return surface;
            if (targetKind == "controller")
                return controller;

            JObject selector = spec["selector"] as JObject ?? spec["Selector"] as JObject;
            return ResolveOptionalComponent(selector);
        }

        static Component ResolveOptionalComponent(JObject selector)
        {
            if (selector == null)
                return null;
            GameObject go = ResolveOptionalGameObject(selector);
            if (go == null)
                return null;

            string componentTypeName = selector["componentType"]?.ToString() ?? selector["ComponentType"]?.ToString();
            if (string.IsNullOrWhiteSpace(componentTypeName))
                return go.GetComponent<Component>();

            Type componentType = UnityComponentResolver.FindType(componentTypeName);
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
                return null;

            int index = selector["componentIndex"]?.ToObject<int?>() ?? selector["ComponentIndex"]?.ToObject<int?>() ?? 0;
            return go.GetComponents(componentType).ElementAtOrDefault(index);
        }

        static GameObject ResolveOptionalGameObject(JObject selector)
        {
            if (selector == null)
                return null;
            JToken target = selector["target"] ?? selector["Target"] ?? selector["name"] ?? selector["Name"];
            if (target == null)
                return null;
            string searchMethod = selector["searchMethod"]?.ToString() ?? selector["SearchMethod"]?.ToString() ?? "by_name";
            bool includeInactive = selector["includeInactive"]?.ToObject<bool?>() ?? selector["IncludeInactive"]?.ToObject<bool?>() ?? true;
            JObject findParams = new() { ["search_inactive"] = includeInactive };
            return ObjectsHelper.FindObject(target, searchMethod, findParams);
        }

        static Camera ResolveCamera(JObject selector)
        {
            GameObject go = ResolveOptionalGameObject(selector);
            if (go != null)
                return go.GetComponent<Camera>();
            return Camera.main ?? UnityApiAdapter.FindObjectsByType<Camera>(FindObjectsInactive.Exclude).FirstOrDefault(camera => camera != null && camera.enabled);
        }

        static MethodInfo FindCallableMethod(Type type, string methodName, JArray args)
        {
            if (string.IsNullOrWhiteSpace(methodName))
                return null;
            return type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.Name == methodName && method.GetParameters().Length == (args?.Count ?? 0))
                .FirstOrDefault(method => CanConvertArguments(args ?? new JArray(), method.GetParameters()));
        }

        static bool CanConvertArguments(JArray args, ParameterInfo[] parameters)
        {
            try
            {
                ConvertArguments(args, parameters);
                return true;
            }
            catch
            {
                return false;
            }
        }

        static object[] ConvertArguments(JArray args, ParameterInfo[] parameters)
        {
            var result = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
                result[i] = ConvertArgument(args[i], parameters[i].ParameterType);
            return result;
        }

        static object ConvertArgument(JToken token, Type type)
        {
            if (type == typeof(string))
                return token?.ToString();
            if (type == typeof(bool))
                return token?.ToObject<bool>() ?? false;
            if (type == typeof(int))
                return token?.ToObject<int>() ?? 0;
            if (type == typeof(float))
                return token?.ToObject<float>() ?? 0f;
            if (type == typeof(double))
                return token?.ToObject<double>() ?? 0d;
            if (type == typeof(Vector2))
            {
                if (UiAuthoringTools.TryParseVector2(token, out Vector2 vector))
                    return vector;
            }
            if (type == typeof(Vector3) && token is JObject obj)
                return new Vector3(obj["x"]?.Value<float>() ?? 0f, obj["y"]?.Value<float>() ?? 0f, obj["z"]?.Value<float>() ?? 0f);
            return token?.ToObject(type);
        }

        static Vector2 ReadPoint(JObject obj)
        {
            return new Vector2(
                obj["screenX"]?.ToObject<float?>() ?? obj["ScreenX"]?.ToObject<float?>() ?? 0f,
                obj["screenY"]?.ToObject<float?>() ?? obj["ScreenY"]?.ToObject<float?>() ?? 0f);
        }

        static float Numeric(Dictionary<string, JToken> values, string key)
        {
            if (string.IsNullOrWhiteSpace(key) || !values.TryGetValue(key, out JToken token))
                return 0f;
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                return token.Value<float>();
            if (token["value"] != null)
                return token["value"].Value<float>();
            return 0f;
        }

        static float ColorDelta(JToken before, JToken after)
        {
            Vector3 a = ReadColorVector(before);
            Vector3 b = ReadColorVector(after);
            return Vector3.Distance(a, b);
        }

        static Vector3 ReadColorVector(JToken token)
        {
            if (token == null)
                return Vector3.zero;
            JToken source = token["value"] ?? token;
            return new Vector3(source["r"]?.Value<float>() ?? 0f, source["g"]?.Value<float>() ?? 0f, source["b"]?.Value<float>() ?? 0f);
        }

        static object NormalizeValue(object value)
        {
            return value switch
            {
                null => null,
                Color color => new { r = color.r, g = color.g, b = color.b, a = color.a },
                Vector2 vector2 => new { x = vector2.x, y = vector2.y },
                Vector3 vector3 => new { x = vector3.x, y = vector3.y, z = vector3.z },
                UnityEngine.Object unityObject => new { name = unityObject.name, type = unityObject.GetType().FullName },
                _ => value
            };
        }

        static object BuildUiHit(UiDiagnosticsHelper.UiElementHitInfo hit)
        {
            return new
            {
                path = hit.Path,
                canvasPath = hit.CanvasPath,
                screenRect = ToRectObject(hit.ScreenRect),
                active = hit.Active,
                raycastTarget = hit.RaycastTarget,
                blocksRaycasts = hit.BlocksRaycasts,
                sortingOrder = hit.SortingOrder,
                depth = hit.Depth,
                graphicType = hit.Graphic != null ? hit.Graphic.GetType().FullName : null
            };
        }

        static object BuildCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            JArray operations = root["operations"] as JArray ?? new JArray();
            JArray assertions = root["assertions"] as JArray ?? new JArray();
            return new
            {
                passed = root["passed"],
                editor = root["editor"],
                controller = root["controller"],
                surface = root["surface"],
                operationCount = operations.Count,
                failedOperations = operations.Where(row => row["success"]?.Value<bool>() == false).ToArray(),
                assertions = assertions,
                beforeSamples = root["beforeSamples"],
                afterSamples = root["afterSamples"]
            };
        }

        static object DescribeComponent(Component component)
        {
            return component == null ? null : new
            {
                path = UiDiagnosticsHelper.GetHierarchyPath(component.transform),
                type = component.GetType().FullName
            };
        }

        static object ToVector2Object(Vector2 value) => new { x = value.x, y = value.y };
        static object ToVector3Object(Vector3 value) => new { x = value.x, y = value.y, z = value.z };
        static object ToRectObject(Rect value) => new { x = value.x, y = value.y, width = value.width, height = value.height };
    }
}
