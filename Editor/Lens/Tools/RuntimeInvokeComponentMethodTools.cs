#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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
        const int MaxReturnedDataDepth = 4;
        const int MaxReturnedDataItems = 64;

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
                    requirePlayMode = new { type = "boolean", description = "Refuse invocation outside play mode. Defaults to true." },
                    requireLensCallable = new { type = "boolean", description = "Require the method to carry an allowed Lens marker attribute such as LensCallable or LensSmokeAction. Defaults to false." },
                    allowedMethodMarkers = new
                    {
                        type = "array",
                        description = "Allowed marker attribute names when requireLensCallable is true. Defaults to LensCallable and LensSmokeAction.",
                        items = new { type = "string" }
                    },
                    surfacePolicy = new { type = "string", description = "Callable surface policy. Phase 8 supports publicInstance only." }
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
                bool requireLensCallable;
                string[] allowedMethodMarkers;
                string surfacePolicy;
                bool componentIndexSpecified;

                using (timing.Measure("normalization"))
                {
                    target = GetString(@params, "target", "Target");
                    searchMethod = GetString(@params, "searchMethod", "SearchMethod") ?? "by_name";
                    componentTypeName = GetString(@params, "componentType", "ComponentType");
                    componentIndexSpecified = GetToken(@params, "componentIndex", "ComponentIndex") != null;
                    componentIndex = Math.Max(0, GetInt(@params, 0, "componentIndex", "ComponentIndex"));
                    methodName = GetString(@params, "methodName", "MethodName");
                    args = GetToken(@params, "args", "Args") as JArray ?? new JArray();
                    includeInactive = GetBool(@params, false, "includeInactive", "IncludeInactive");
                    waitFrames = Math.Clamp(GetInt(@params, 0, "waitFrames", "WaitFrames"), 0, 600);
                    captureConsoleDelta = GetBool(@params, true, "captureConsoleDelta", "CaptureConsoleDelta");
                    requirePlayMode = GetBool(@params, true, "requirePlayMode", "RequirePlayMode");
                    requireLensCallable = GetBool(@params, false, "requireLensCallable", "RequireLensCallable");
                    allowedMethodMarkers = GetStringArray(@params, "allowedMethodMarkers", "AllowedMethodMarkers");
                    if (allowedMethodMarkers.Length == 0)
                        allowedMethodMarkers = new[] { "LensCallable", "LensSmokeAction" };
                    surfacePolicy = GetString(@params, "surfacePolicy", "SurfacePolicy") ?? "publicInstance";
                }

                using (timing.Measure("service"))
                {
                    data = await InvokeAsync(
                        target,
                        searchMethod,
                        componentTypeName,
                        componentIndex,
                        componentIndexSpecified,
                        methodName,
                        args,
                        includeInactive,
                        waitFrames,
                        captureConsoleDelta,
                        requirePlayMode,
                        requireLensCallable,
                        allowedMethodMarkers,
                        surfacePolicy);
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
            bool componentIndexSpecified,
            string methodName,
            JArray args,
            bool includeInactive,
            int waitFrames,
            bool captureConsoleDelta,
            bool requirePlayMode,
            bool requireLensCallable,
            string[] allowedMethodMarkers,
            string surfacePolicy)
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
            if (!string.Equals(surfacePolicy, "publicInstance", StringComparison.OrdinalIgnoreCase))
                return Failed("unsupported_surface_policy", $"surfacePolicy '{surfacePolicy}' is not supported. Phase 8 only supports publicInstance.");
            if (!UnityComponentResolver.TryResolve(componentTypeName, out Type componentType, out string typeError))
                return Failed("component_type_not_found", typeError);

            if (!TryResolveSingleTarget(target, searchMethod, includeInactive, out GameObject targetObject, out object resolution, out string resolutionReason))
                return Failed(resolutionReason, $"Target '{target}' could not be resolved unambiguously using search method '{searchMethod}'.", resolution);

            Component[] components = targetObject.GetComponents(componentType);
            if (components.Length <= componentIndex || components[componentIndex] == null)
                return Failed("component_not_found", $"Component '{componentTypeName}' with index {componentIndex} was not found on '{UiDiagnosticsHelper.GetHierarchyPath(targetObject.transform)}'.");
            if (!componentIndexSpecified && components.Length > 1)
                return Failed("component_ambiguous", $"Target has {components.Length} components assignable to '{componentTypeName}'. Provide componentIndex to choose one.");

            Component component = components[componentIndex];
            if (!TryResolveMethod(component.GetType(), methodName, args, requireLensCallable, allowedMethodMarkers, out MethodInfo method, out object[] convertedArgs, out object markerInfo, out string methodError))
                return Failed("method_not_found", methodError);

            ConsoleCursorSnapshot consoleBefore = captureConsoleDelta ? ConsoleCursorDelta.Capture() : null;
            object beforeFrameCounts = EditorToolStateHelpers.BuildRuntimeProbeData();
            object beforeState = ComponentSummarySerializer.GetSafeComponentData(component, includeNonPublicSerializedFields: true);
            object returnValue = method.Invoke(component, convertedArgs);
            ReturnedDataShape returnedData = BuildReturnedData(returnValue);

            if (waitFrames > 0)
                await Task.Delay(Math.Max(1, waitFrames) * 16);

            object afterFrameCounts = EditorToolStateHelpers.BuildRuntimeProbeData();
            object afterState = ComponentSummarySerializer.GetSafeComponentData(component, includeNonPublicSerializedFields: true);
            object consoleDelta = ConsoleCursorDelta.BuildDelta(
                captureConsoleDelta,
                consoleBefore,
                ToolName,
                new { kind = "runtime_invoke_component_method_console_delta", methodName });
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
                    objectId = GetStableObjectId(targetObject),
                    unityObjectId = UnityApiAdapter.GetObjectIdOrZero(targetObject)
                },
                component = new
                {
                    requestedType = componentTypeName,
                    resolvedType = component.GetType().FullName,
                    componentIndex,
                    componentId = GetStableObjectId(component),
                    unityObjectId = UnityApiAdapter.GetObjectIdOrZero(component)
                },
                method = new
                {
                    methodName = method.Name,
                    declaringType = method.DeclaringType?.FullName,
                    parameterCount = method.GetParameters().Length,
                    returnType = method.ReturnType.FullName,
                    requireLensCallable,
                    allowedMethodMarkers,
                    surfacePolicy = "publicInstance",
                    markers = markerInfo
                },
                args = args.ToArray(),
                returnValue = DescribeValue(returnValue),
                returnedData = returnedData.InlineData,
                returnedDataIncluded = returnedData.IncludedInline,
                returnedDataBytes = returnedData.Bytes,
                returnedDataDetailRef = returnedData.DetailRef,
                returnedDataTruncated = returnedData.Truncated,
                waitFrames,
                frameCounts = new
                {
                    before = beforeFrameCounts,
                    after = afterFrameCounts
                },
                stateChanged = !string.Equals(beforeJson, afterJson, StringComparison.Ordinal),
                stateSummary = new
                {
                    beforeBytes = PayloadBudgeting.GetUtf8ByteCount(beforeJson),
                    afterBytes = PayloadBudgeting.GetUtf8ByteCount(afterJson)
                },
                beforeState,
                afterState,
                consoleDelta,
                consoleErrorsDetected = ConsoleDeltaHasErrors(consoleDelta)
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
                returnedData = root["returnedData"],
                returnedDataIncluded = root["returnedDataIncluded"],
                returnedDataBytes = root["returnedDataBytes"],
                returnedDataDetailRef = root["returnedDataDetailRef"],
                returnedDataTruncated = root["returnedDataTruncated"],
                waitFrames = root["waitFrames"],
                frameCounts = root["frameCounts"],
                stateChanged = root["stateChanged"],
                stateSummary = root["stateSummary"],
                consoleDelta = root["consoleDelta"],
                consoleErrorsDetected = root["consoleErrorsDetected"]
            };
        }

        static bool ConsoleDeltaHasErrors(object consoleDelta)
        {
            try
            {
                return JObject.FromObject(consoleDelta ?? new { }).Value<bool?>("consoleErrorsDetected") == true;
            }
            catch
            {
                return false;
            }
        }

        static bool TryResolveMethod(
            Type componentType,
            string methodName,
            JArray args,
            bool requireLensCallable,
            string[] allowedMethodMarkers,
            out MethodInfo method,
            out object[] convertedArgs,
            out object markerInfo,
            out string error)
        {
            method = null;
            convertedArgs = null;
            markerInfo = null;
            error = null;

            var candidates = componentType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(candidate => !candidate.IsSpecialName && !candidate.ContainsGenericParameters)
                .Where(candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                .ToArray();

            var matches = new List<(MethodInfo Method, object[] Args)>();
            foreach (var candidate in candidates)
            {
                if (requireLensCallable && !HasAllowedMethodMarker(candidate, allowedMethodMarkers))
                    continue;
                if (TryConvertArguments(candidate.GetParameters(), args, out object[] converted))
                    matches.Add((candidate, converted));
            }

            if (matches.Count == 1)
            {
                method = matches[0].Method;
                convertedArgs = matches[0].Args;
                markerInfo = BuildMarkerInfo(method, allowedMethodMarkers);
                return true;
            }

            string markerRequirement = requireLensCallable ? $" and marker(s) [{string.Join(", ", allowedMethodMarkers)}]" : string.Empty;
            error = matches.Count == 0
                ? $"No public instance method '{methodName}' on '{componentType.FullName}' matched {args.Count} argument(s){markerRequirement}."
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

        static object Failed(string reason, string message, object resolution)
        {
            return new
            {
                status = "failed",
                reason,
                message,
                isPlaying = EditorApplication.isPlaying,
                resolution
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
            return TryResolveSingleTarget(target, searchMethod, includeInactive, out GameObject gameObject, out _, out _)
                ? gameObject
                : null;
        }

        static bool TryResolveSingleTarget(string target, string searchMethod, bool includeInactive, out GameObject gameObject, out object resolution, out string reason)
        {
            gameObject = null;
            resolution = null;
            reason = null;
            if (string.IsNullOrWhiteSpace(target))
            {
                reason = "target_required";
                return false;
            }

            GameObject[] objects = UnityApiAdapter.FindObjectsByType<GameObject>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
            string method = (searchMethod ?? "by_name").Trim().ToLowerInvariant();
            GameObject[] matches = objects
                .Where(candidate =>
                (method == "by_id" && UnityApiAdapter.ObjectIdEquals(candidate, target)) ||
                (method == "by_id" && ObjectIdEquals(candidate, target)) ||
                (method == "by_path" && string.Equals(UiDiagnosticsHelper.GetHierarchyPath(candidate.transform), target, StringComparison.Ordinal)) ||
                (method == "by_id_or_name_or_path" && (
                    UnityApiAdapter.ObjectIdEquals(candidate, target) ||
                    ObjectIdEquals(candidate, target) ||
                    string.Equals(candidate.name, target, StringComparison.Ordinal) ||
                    string.Equals(UiDiagnosticsHelper.GetHierarchyPath(candidate.transform), target, StringComparison.Ordinal))) ||
                (method != "by_id" && method != "by_path" && method != "by_id_or_name_or_path" && string.Equals(candidate.name, target, StringComparison.Ordinal)))
                .OrderBy(candidate => UiDiagnosticsHelper.GetHierarchyPath(candidate.transform), StringComparer.Ordinal)
                .ToArray();

            resolution = new
            {
                target,
                searchMethod = method,
                includeInactive,
                matchCount = matches.Length,
                candidates = matches.Take(8).Select(candidate => new
                {
                    name = candidate.name,
                    path = UiDiagnosticsHelper.GetHierarchyPath(candidate.transform),
                    sceneName = candidate.scene.name,
                    scenePath = candidate.scene.path,
                    objectId = GetStableObjectId(candidate),
                    unityObjectId = UnityApiAdapter.GetObjectIdOrZero(candidate)
                }).ToArray(),
                omittedCandidateCount = Math.Max(0, matches.Length - 8)
            };

            if (matches.Length == 1)
            {
                gameObject = matches[0];
                return true;
            }

            reason = matches.Length == 0 ? "target_not_found" : "target_ambiguous";
            return false;
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

        sealed class ReturnedDataShape
        {
            public object InlineData;
            public bool IncludedInline;
            public int Bytes;
            public object DetailRef;
            public bool Truncated;
        }

        static ReturnedDataShape BuildReturnedData(object value)
        {
            bool truncated = false;
            object shaped = ShapeReturnedValue(value, 0, new HashSet<int>(), ref truncated);
            string serialized = JsonConvert.SerializeObject(shaped, Formatting.None);
            int bytes = PayloadBudgeting.GetUtf8ByteCount(serialized);
            object detailRef = bytes > PayloadBudgetPolicy.MaxToolResultBytes
                ? ToolResultCompactor.CreateStoredDetailRef(
                    ToolName,
                    shaped,
                    bytes,
                    new { kind = "runtime_invoke_component_method_returned_data" })
                : null;
            bool includeInline = bytes <= PayloadBudgetPolicy.MaxToolResultBytes || detailRef == null;

            return new ReturnedDataShape
            {
                InlineData = includeInline ? shaped : null,
                IncludedInline = includeInline,
                Bytes = bytes,
                DetailRef = detailRef,
                Truncated = truncated
            };
        }

        static object ShapeReturnedValue(object value, int depth, HashSet<int> visited, ref bool truncated)
        {
            if (value == null)
                return null;

            Type type = value.GetType();
            Type nullableType = Nullable.GetUnderlyingType(type) ?? type;
            if (value is string || nullableType.IsPrimitive || nullableType.IsEnum || value is decimal)
                return value;
            if (value is DateTime dateTime)
                return dateTime.ToString("O");
            if (value is DateTimeOffset dateTimeOffset)
                return dateTimeOffset.ToString("O");
            if (value is Guid guid)
                return guid.ToString("D");
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

            if (depth >= MaxReturnedDataDepth)
            {
                truncated = true;
                return new { type = type.FullName, truncated = true };
            }

            if (!type.IsValueType)
            {
                int identity = RuntimeHelpers.GetHashCode(value);
                if (!visited.Add(identity))
                {
                    truncated = true;
                    return new { type = type.FullName, circularReference = true };
                }
            }

            if (value is IDictionary dictionary)
            {
                var shaped = new Dictionary<string, object>();
                int count = 0;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (count >= MaxReturnedDataItems)
                    {
                        truncated = true;
                        break;
                    }

                    string key = entry.Key?.ToString() ?? string.Empty;
                    shaped[key] = ShapeReturnedValue(entry.Value, depth + 1, visited, ref truncated);
                    count++;
                }

                return shaped;
            }

            if (value is IEnumerable enumerable)
            {
                var shaped = new List<object>();
                int count = 0;
                foreach (object item in enumerable)
                {
                    if (count >= MaxReturnedDataItems)
                    {
                        truncated = true;
                        break;
                    }

                    shaped.Add(ShapeReturnedValue(item, depth + 1, visited, ref truncated));
                    count++;
                }

                return shaped.ToArray();
            }

            return ShapeReturnedObject(value, type, depth, visited, ref truncated);
        }

        static object ShapeReturnedObject(object value, Type type, int depth, HashSet<int> visited, ref bool truncated)
        {
            var shaped = new Dictionary<string, object>
            {
                ["type"] = type.FullName
            };

            int count = 0;
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
                .OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                if (count >= MaxReturnedDataItems)
                {
                    truncated = true;
                    break;
                }

                try
                {
                    shaped[property.Name] = ShapeReturnedValue(property.GetValue(value), depth + 1, visited, ref truncated);
                    count++;
                }
                catch
                {
                    shaped[property.Name] = new { unavailable = true };
                }
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(field => field.Name, StringComparer.Ordinal))
            {
                if (count >= MaxReturnedDataItems)
                {
                    truncated = true;
                    break;
                }

                try
                {
                    shaped[field.Name] = ShapeReturnedValue(field.GetValue(value), depth + 1, visited, ref truncated);
                    count++;
                }
                catch
                {
                    shaped[field.Name] = new { unavailable = true };
                }
            }

            if (count == 0)
                shaped["stringValue"] = value.ToString();

            return shaped;
        }

        static bool HasAllowedMethodMarker(MethodInfo method, string[] allowedMethodMarkers)
        {
            var allowed = BuildAllowedMarkerNames(allowedMethodMarkers);
            return method.GetCustomAttributes(inherit: true)
                .OfType<Attribute>()
                .SelectMany(attribute => GetAttributeMarkerNames(attribute))
                .Any(marker => allowed.Contains(marker));
        }

        static object BuildMarkerInfo(MethodInfo method, string[] allowedMethodMarkers)
        {
            string[] presentMarkers = method.GetCustomAttributes(inherit: true)
                .OfType<Attribute>()
                .SelectMany(attribute => GetAttributeMarkerNames(attribute))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var allowed = BuildAllowedMarkerNames(allowedMethodMarkers);
            return new
            {
                presentMarkers,
                allowedMethodMarkers = allowed.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
                hasAllowedMarker = presentMarkers.Any(marker => allowed.Contains(marker))
            };
        }

        static HashSet<string> BuildAllowedMarkerNames(string[] allowedMethodMarkers)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (string marker in allowedMethodMarkers ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(marker))
                    continue;
                string trimmed = marker.Trim();
                names.Add(trimmed);
                if (trimmed.EndsWith("Attribute", StringComparison.Ordinal))
                    names.Add(trimmed[..^"Attribute".Length]);
                else
                    names.Add(trimmed + "Attribute");
            }

            if (names.Count == 0)
            {
                names.Add("LensCallable");
                names.Add("LensCallableAttribute");
                names.Add("LensSmokeAction");
                names.Add("LensSmokeActionAttribute");
            }

            return names;
        }

        static IEnumerable<string> GetAttributeMarkerNames(Attribute attribute)
        {
            Type type = attribute.GetType();
            if (!string.IsNullOrWhiteSpace(type.Name))
            {
                yield return type.Name;
                if (type.Name.EndsWith("Attribute", StringComparison.Ordinal))
                    yield return type.Name[..^"Attribute".Length];
            }

            if (!string.IsNullOrWhiteSpace(type.FullName))
                yield return type.FullName;
        }

        static string GetStableObjectId(Object obj)
        {
            if (obj == null)
                return null;

#pragma warning disable CS0618
            return obj.GetInstanceID().ToString(CultureInfo.InvariantCulture);
#pragma warning restore CS0618
        }

        static bool ObjectIdEquals(Object obj, string id)
        {
            string trimmed = id?.Trim();
            return string.Equals(GetStableObjectId(obj), trimmed, StringComparison.Ordinal) ||
                UnityApiAdapter.ObjectIdEquals(obj, trimmed);
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

        static string[] GetStringArray(JObject parameters, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            if (token is not JArray array)
                return Array.Empty<string>();

            return array
                .Select(item => item?.Value<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
    }
}
