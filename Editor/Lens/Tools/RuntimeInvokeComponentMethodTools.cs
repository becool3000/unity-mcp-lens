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
using Object = UnityEngine.Object;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class RuntimeInvokeComponentMethodTools
    {
        const string ToolName = "Unity.Runtime.InvokeComponentMethod";

        [McpSchema(ToolName)]
        public static object GetSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    target = new { type = "string", description = "Target GameObject name, hierarchy path, or id." },
                    searchMethod = new { type = "string", description = "Target search method: by_name, by_path, by_id, or by_id_or_name_or_path. Defaults to by_name." },
                    componentType = new { type = "string", description = "Component type name. Short or fully-qualified names are accepted." },
                    componentIndex = new { type = "integer", description = "0-based index among matching components. Defaults to 0." },
                    methodName = new { type = "string", description = "Public instance method name to invoke." },
                    args = new { type = "array", description = "JSON argument values converted to the selected method parameter types.", items = new { } },
                    includeInactive = new { type = "boolean", description = "Include inactive objects when resolving target. Defaults to false." },
                    waitFrames = new { type = "integer", description = "Approximate rendered frames to wait after invocation before collecting after-state. Defaults to 0." },
                    captureConsoleDelta = new { type = "boolean", description = "Capture error-count delta before and after invocation. Defaults to true." },
                    requirePlayMode = new { type = "boolean", description = "Refuse invocation outside play mode. Defaults to true." }
                },
                required = new[] { "target", "componentType", "methodName" }
            };
        }

        [McpTool(ToolName,
            "Invokes a public instance method on a runtime component with typed JSON arguments, optional post-call wait, and compact state/console evidence.",
            "Invoke Runtime Component Method",
            Groups = new[] { "runtime", "diagnostics" },
            EnabledByDefault = true)]
        public static async Task<object> InvokeComponentMethod(JObject @params)
        {
            @params ??= new JObject();
            var timing = new ToolOperationTiming(ToolName, "invoke_component_method", PayloadBudgeting.GetUtf8ByteCount(@params.ToString(Formatting.None)));
            bool success = true;
            string errorKind = null;
            object data;

            try
            {
                string target;
                string searchMethod;
                string componentTypeName;
                int componentIndex;
                string methodName;
                JArray args;
                bool includeInactive;
                int waitFrames;
                bool captureConsoleDelta;
                bool requirePlayMode;

                using (timing.Measure("normalization"))
                {
                    target = GetString(@params, "target", "Target");
                    searchMethod = GetString(@params, "searchMethod", "SearchMethod") ?? "by_name";
                    componentTypeName = GetString(@params, "componentType", "ComponentType");
                    componentIndex = Math.Max(0, GetInt(@params, 0, "componentIndex", "ComponentIndex"));
                    methodName = GetString(@params, "methodName", "MethodName");
                    args = GetToken(@params, "args", "Args") as JArray ?? new JArray();
                    includeInactive = GetBool(@params, false, "includeInactive", "IncludeInactive");
                    waitFrames = Math.Clamp(GetInt(@params, 0, "waitFrames", "WaitFrames"), 0, 600);
                    captureConsoleDelta = GetBool(@params, true, "captureConsoleDelta", "CaptureConsoleDelta");
                    requirePlayMode = GetBool(@params, true, "requirePlayMode", "RequirePlayMode");
                }

                using (timing.Measure("service"))
                {
                    data = await InvokeAsync(target, searchMethod, componentTypeName, componentIndex, methodName, args, includeInactive, waitFrames, captureConsoleDelta, requirePlayMode);
                    string serialized = JsonConvert.SerializeObject(data, Formatting.None);
                    success = serialized.IndexOf("\"status\":\"ready\"", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        serialized.IndexOf("\"consoleErrorsDetected\":true", StringComparison.OrdinalIgnoreCase) < 0;
                    errorKind = success ? null : ExtractReason(serialized) ?? "runtime_invoke_failed";
                }

                using (timing.Measure("adapter"))
                {
                }
            }
            catch (Exception ex)
            {
                success = false;
                errorKind = ex.GetType().Name;
                data = new
                {
                    status = "failed",
                    errorKind,
                    error = ex.Message,
                    isPlaying = EditorApplication.isPlaying
                };
            }

            object response;
            using (timing.Measure("result_shaping"))
            {
                response = success
                    ? Response.Success("Runtime component method invocation completed.", ToolResultCompactor.ShapeStructuredPayload(
                        ToolName,
                        data,
                        BuildCompactData(data),
                        new { kind = "runtime_invoke_component_method_full_result" },
                        "runtime_invoke_component_method",
                        detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes))
                    : Response.Error("RUNTIME_INVOKE_COMPONENT_METHOD_FAILED", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, success ? null : errorKind);
            return response;
        }

        static async Task<object> InvokeAsync(
            string target,
            string searchMethod,
            string componentTypeName,
            int componentIndex,
            string methodName,
            JArray args,
            bool includeInactive,
            int waitFrames,
            bool captureConsoleDelta,
            bool requirePlayMode)
        {
            if (requirePlayMode && !EditorApplication.isPlaying)
            {
                return new
                {
                    status = "refused",
                    reason = "not_in_play_mode",
                    isPlaying = EditorApplication.isPlaying,
                    requirePlayMode
                };
            }

            if (string.IsNullOrWhiteSpace(target))
                return Failed("target_required", "target is required.");
            if (string.IsNullOrWhiteSpace(componentTypeName))
                return Failed("component_type_required", "componentType is required.");
            if (string.IsNullOrWhiteSpace(methodName))
                return Failed("method_name_required", "methodName is required.");
            if (!UnityComponentResolver.TryResolve(componentTypeName, out Type componentType, out string typeError))
                return Failed("component_type_not_found", typeError);

            GameObject targetObject = ResolveTarget(target, searchMethod, includeInactive);
            if (targetObject == null)
                return Failed("target_not_found", $"Target '{target}' was not found using search method '{searchMethod}'.");

            Component[] components = targetObject.GetComponents(componentType);
            if (components.Length <= componentIndex || components[componentIndex] == null)
                return Failed("component_not_found", $"Component '{componentTypeName}' with index {componentIndex} was not found on '{UiDiagnosticsHelper.GetHierarchyPath(targetObject.transform)}'.");

            Component component = components[componentIndex];
            if (!TryResolveMethod(component.GetType(), methodName, args, out MethodInfo method, out object[] convertedArgs, out string methodError))
                return Failed("method_not_found", methodError);

            int initialConsoleErrorCount = captureConsoleDelta ? EditorToolStateHelpers.CountConsoleErrors() : -1;
            object beforeState = GameObjectSerializer.GetComponentData(component, includeNonPublicSerializedFields: true);
            object returnValue = method.Invoke(component, convertedArgs);

            if (waitFrames > 0)
                await Task.Delay(Math.Max(1, waitFrames) * 16);

            object afterState = GameObjectSerializer.GetComponentData(component, includeNonPublicSerializedFields: true);
            int finalConsoleErrorCount = captureConsoleDelta ? EditorToolStateHelpers.CountConsoleErrors() : -1;
            int newConsoleErrorCount = captureConsoleDelta && initialConsoleErrorCount >= 0 && finalConsoleErrorCount >= 0
                ? Math.Max(0, finalConsoleErrorCount - initialConsoleErrorCount)
                : 0;
            string beforeJson = JsonConvert.SerializeObject(beforeState, Formatting.None);
            string afterJson = JsonConvert.SerializeObject(afterState, Formatting.None);

            return new
            {
                status = "ready",
                isPlaying = EditorApplication.isPlaying,
                requirePlayMode,
                target = new
                {
                    name = targetObject.name,
                    path = UiDiagnosticsHelper.GetHierarchyPath(targetObject.transform),
                    activeSelf = targetObject.activeSelf,
                    activeInHierarchy = targetObject.activeInHierarchy,
                    objectId = UnityApiAdapter.GetObjectIdOrZero(targetObject)
                },
                component = new
                {
                    requestedType = componentTypeName,
                    resolvedType = component.GetType().FullName,
                    componentIndex,
                    componentId = UnityApiAdapter.GetObjectIdOrZero(component)
                },
                method = new
                {
                    methodName = method.Name,
                    declaringType = method.DeclaringType?.FullName,
                    parameterCount = method.GetParameters().Length,
                    returnType = method.ReturnType.FullName
                },
                args = args.ToArray(),
                returnValue = DescribeValue(returnValue),
                waitFrames,
                stateChanged = !string.Equals(beforeJson, afterJson, StringComparison.Ordinal),
                stateSummary = new
                {
                    beforeBytes = PayloadBudgeting.GetUtf8ByteCount(beforeJson),
                    afterBytes = PayloadBudgeting.GetUtf8ByteCount(afterJson)
                },
                beforeState,
                afterState,
                consoleDelta = captureConsoleDelta
                    ? new
                    {
                        initialConsoleErrorCount,
                        finalConsoleErrorCount,
                        newConsoleErrorCount,
                        consoleErrorsDetected = newConsoleErrorCount > 0
                    }
                    : null,
                consoleErrorsDetected = newConsoleErrorCount > 0
            };
        }

        static object BuildCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            return new
            {
                status = root["status"],
                reason = root["reason"],
                isPlaying = root["isPlaying"],
                requirePlayMode = root["requirePlayMode"],
                target = root["target"],
                component = root["component"],
                method = root["method"],
                returnValue = root["returnValue"],
                waitFrames = root["waitFrames"],
                stateChanged = root["stateChanged"],
                stateSummary = root["stateSummary"],
                consoleDelta = root["consoleDelta"],
                consoleErrorsDetected = root["consoleErrorsDetected"]
            };
        }

        static bool TryResolveMethod(Type componentType, string methodName, JArray args, out MethodInfo method, out object[] convertedArgs, out string error)
        {
            method = null;
            convertedArgs = null;
            error = null;

            var candidates = componentType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(candidate => !candidate.IsSpecialName && !candidate.ContainsGenericParameters)
                .Where(candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                .ToArray();

            var matches = new List<(MethodInfo Method, object[] Args)>();
            foreach (var candidate in candidates)
            {
                if (TryConvertArguments(candidate.GetParameters(), args, out object[] converted))
                    matches.Add((candidate, converted));
            }

            if (matches.Count == 1)
            {
                method = matches[0].Method;
                convertedArgs = matches[0].Args;
                return true;
            }

            error = matches.Count == 0
                ? $"No public instance method '{methodName}' on '{componentType.FullName}' matched {args.Count} argument(s)."
                : $"Multiple public instance overloads named '{methodName}' on '{componentType.FullName}' matched {args.Count} argument(s); use a less ambiguous argument list.";
            return false;
        }

        static bool TryConvertArguments(ParameterInfo[] parameters, JArray args, out object[] converted)
        {
            converted = null;
            if (args.Count > parameters.Length)
                return false;

            var values = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i >= args.Count)
                {
                    if (parameters[i].HasDefaultValue)
                    {
                        values[i] = parameters[i].DefaultValue;
                        continue;
                    }

                    return false;
                }

                if (!TryConvertArgument(args[i], parameters[i].ParameterType, out values[i]))
                    return false;
            }

            converted = values;
            return true;
        }

        static bool TryConvertArgument(JToken token, Type targetType, out object value)
        {
            value = null;
            if (token == null || token.Type == JTokenType.Null)
                return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null;

            Type nullableType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            try
            {
                if (nullableType == typeof(string))
                {
                    value = token.Value<string>();
                    return true;
                }
                if (nullableType == typeof(bool))
                {
                    value = token.Value<bool>();
                    return true;
                }
                if (nullableType == typeof(int))
                {
                    value = token.Value<int>();
                    return true;
                }
                if (nullableType == typeof(long))
                {
                    value = token.Value<long>();
                    return true;
                }
                if (nullableType == typeof(float))
                {
                    value = token.Value<float>();
                    return true;
                }
                if (nullableType == typeof(double))
                {
                    value = token.Value<double>();
                    return true;
                }
                if (nullableType.IsEnum)
                {
                    value = token.Type == JTokenType.Integer
                        ? Enum.ToObject(nullableType, token.Value<int>())
                        : Enum.Parse(nullableType, token.Value<string>(), ignoreCase: true);
                    return true;
                }
                if (nullableType == typeof(Vector2) && TryParseVector2(token, out Vector2 vector2))
                {
                    value = vector2;
                    return true;
                }
                if (nullableType == typeof(Vector3) && TryParseVector3(token, out Vector3 vector3))
                {
                    value = vector3;
                    return true;
                }
                if (nullableType == typeof(Color) && TryParseColor(token, out Color color))
                {
                    value = color;
                    return true;
                }
                if (typeof(GameObject).IsAssignableFrom(nullableType) && token.Type == JTokenType.String)
                {
                    value = ResolveTarget(token.Value<string>(), "by_id_or_name_or_path", includeInactive: true);
                    return value != null;
                }
                if (typeof(Component).IsAssignableFrom(nullableType) && token.Type == JTokenType.String)
                {
                    var go = ResolveTarget(token.Value<string>(), "by_id_or_name_or_path", includeInactive: true);
                    value = go == null ? null : go.GetComponent(nullableType);
                    return value != null;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        static object Failed(string reason, string message)
        {
            return new
            {
                status = "failed",
                reason,
                message,
                isPlaying = EditorApplication.isPlaying
            };
        }

        static string ExtractReason(string serialized)
        {
            try
            {
                var root = JObject.Parse(serialized);
                return root["reason"]?.Value<string>();
            }
            catch
            {
                return null;
            }
        }

        static GameObject ResolveTarget(string target, string searchMethod, bool includeInactive)
        {
            if (string.IsNullOrWhiteSpace(target))
                return null;

            GameObject[] objects = UnityApiAdapter.FindObjectsByType<GameObject>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
            string method = (searchMethod ?? "by_name").Trim().ToLowerInvariant();
            return objects.FirstOrDefault(candidate =>
                (method == "by_id" && UnityApiAdapter.ObjectIdEquals(candidate, target)) ||
                (method == "by_path" && string.Equals(UiDiagnosticsHelper.GetHierarchyPath(candidate.transform), target, StringComparison.Ordinal)) ||
                (method == "by_id_or_name_or_path" && (
                    UnityApiAdapter.ObjectIdEquals(candidate, target) ||
                    string.Equals(candidate.name, target, StringComparison.Ordinal) ||
                    string.Equals(UiDiagnosticsHelper.GetHierarchyPath(candidate.transform), target, StringComparison.Ordinal))) ||
                (method != "by_id" && method != "by_path" && method != "by_id_or_name_or_path" && string.Equals(candidate.name, target, StringComparison.Ordinal)));
        }

        static object DescribeValue(object value)
        {
            if (value == null)
                return null;
            if (value is string || value.GetType().IsPrimitive || value.GetType().IsEnum)
                return value;
            if (value is Vector2 vector2)
                return new { x = vector2.x, y = vector2.y };
            if (value is Vector3 vector3)
                return new { x = vector3.x, y = vector3.y, z = vector3.z };
            if (value is Color color)
                return new { r = color.r, g = color.g, b = color.b, a = color.a };
            if (value is Object unityObject)
            {
                return new
                {
                    name = unityObject.name,
                    type = unityObject.GetType().FullName,
                    objectId = UnityApiAdapter.GetObjectIdOrZero(unityObject)
                };
            }

            return value.ToString();
        }

        static bool TryParseVector2(JToken value, out Vector2 vector)
        {
            vector = default;
            if (value is JArray array && array.Count >= 2)
            {
                vector = new Vector2(array[0].Value<float>(), array[1].Value<float>());
                return true;
            }
            if (value is JObject obj &&
                obj.TryGetValue("x", StringComparison.OrdinalIgnoreCase, out JToken x) &&
                obj.TryGetValue("y", StringComparison.OrdinalIgnoreCase, out JToken y))
            {
                vector = new Vector2(x.Value<float>(), y.Value<float>());
                return true;
            }
            return false;
        }

        static bool TryParseVector3(JToken value, out Vector3 vector)
        {
            vector = default;
            if (value is JArray array && array.Count >= 3)
            {
                vector = new Vector3(array[0].Value<float>(), array[1].Value<float>(), array[2].Value<float>());
                return true;
            }
            if (value is JObject obj &&
                obj.TryGetValue("x", StringComparison.OrdinalIgnoreCase, out JToken x) &&
                obj.TryGetValue("y", StringComparison.OrdinalIgnoreCase, out JToken y) &&
                obj.TryGetValue("z", StringComparison.OrdinalIgnoreCase, out JToken z))
            {
                vector = new Vector3(x.Value<float>(), y.Value<float>(), z.Value<float>());
                return true;
            }
            return false;
        }

        static bool TryParseColor(JToken value, out Color color)
        {
            color = default;
            if (value is JArray array && array.Count >= 3)
            {
                color = new Color(array[0].Value<float>(), array[1].Value<float>(), array[2].Value<float>(), array.Count > 3 ? array[3].Value<float>() : 1f);
                return true;
            }
            if (value is JObject obj &&
                obj.TryGetValue("r", StringComparison.OrdinalIgnoreCase, out JToken r) &&
                obj.TryGetValue("g", StringComparison.OrdinalIgnoreCase, out JToken g) &&
                obj.TryGetValue("b", StringComparison.OrdinalIgnoreCase, out JToken b))
            {
                color = new Color(r.Value<float>(), g.Value<float>(), b.Value<float>(), obj.TryGetValue("a", StringComparison.OrdinalIgnoreCase, out JToken a) ? a.Value<float>() : 1f);
                return true;
            }
            return false;
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

        static string GetString(JObject parameters, params string[] names)
        {
            return GetToken(parameters, names)?.Value<string>();
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
    }
}
