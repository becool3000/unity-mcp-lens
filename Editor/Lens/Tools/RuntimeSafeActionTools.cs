#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
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
    public static class RuntimeSafeActionTools
    {
        const string SetComponentPropertyToolName = "Unity.Runtime.SetComponentProperty";
        const string AddTemporaryComponentToolName = "Unity.Runtime.AddTemporaryComponent";

        [McpSchema(SetComponentPropertyToolName)]
        public static object GetSetComponentPropertySchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    target = new { type = "string", description = "Target GameObject name, hierarchy path, or signed/string object id." },
                    searchMethod = new { type = "string", description = "Target search method: by_name, by_path, by_id, or by_id_or_name_or_path. Defaults to by_name." },
                    componentType = new { type = "string", description = "Component type name. Short or fully-qualified names are accepted." },
                    componentIndex = new { type = "integer", description = "0-based index among matching components. Required when multiple matching components exist." },
                    memberName = new { type = "string", description = "Public writable property/field name, or non-public [SerializeField] field name when allowNonPublicSerializedField is true." },
                    value = new { description = "JSON value converted to the target member type." },
                    includeInactive = new { type = "boolean", description = "Include inactive objects when resolving target. Defaults to false." },
                    requirePlayMode = new { type = "boolean", description = "Refuse mutation outside Play Mode. Defaults to true." },
                    allowNonPublicSerializedField = new { type = "boolean", description = "Allow direct writes to non-public fields marked [SerializeField]. Defaults to true." },
                    waitFrames = new { type = "integer", description = "Approximate runtime frames to wait after mutation before collecting after-state. Defaults to 0." },
                    captureConsoleDelta = new { type = "boolean", description = "Capture error-count delta before and after mutation. Defaults to true." }
                },
                required = new[] { "target", "componentType", "memberName", "value" }
            };
        }

        [McpTool(SetComponentPropertyToolName,
            "Sets a public writable property/field or [SerializeField] runtime field on a resolved component, then returns before/after evidence.",
            "Set Runtime Component Property",
            Groups = new[] { "runtime", "diagnostics" },
            EnabledByDefault = true)]
        public static async Task<object> SetComponentProperty(JObject @params)
        {
            @params ??= new JObject();
            var timing = new ToolOperationTiming(SetComponentPropertyToolName, "set_component_property", PayloadBudgeting.GetUtf8ByteCount(@params.ToString(Formatting.None)));
            bool success = true;
            string errorKind = null;
            object data;

            try
            {
                string target;
                string searchMethod;
                string componentTypeName;
                int componentIndex;
                bool componentIndexSpecified;
                string memberName;
                JToken valueToken;
                bool includeInactive;
                bool requirePlayMode;
                bool allowNonPublicSerializedField;
                int waitFrames;
                bool captureConsoleDelta;

                using (timing.Measure("normalization"))
                {
                    target = GetString(@params, "target", "Target");
                    searchMethod = GetString(@params, "searchMethod", "SearchMethod") ?? "by_name";
                    componentTypeName = GetString(@params, "componentType", "ComponentType");
                    componentIndexSpecified = GetToken(@params, "componentIndex", "ComponentIndex") != null;
                    componentIndex = Math.Max(0, GetInt(@params, 0, "componentIndex", "ComponentIndex"));
                    memberName = GetString(@params, "memberName", "MemberName", "propertyName", "PropertyName");
                    valueToken = GetToken(@params, "value", "Value");
                    includeInactive = GetBool(@params, false, "includeInactive", "IncludeInactive");
                    requirePlayMode = GetBool(@params, true, "requirePlayMode", "RequirePlayMode");
                    allowNonPublicSerializedField = GetBool(@params, true, "allowNonPublicSerializedField", "AllowNonPublicSerializedField");
                    waitFrames = Math.Clamp(GetInt(@params, 0, "waitFrames", "WaitFrames"), 0, 600);
                    captureConsoleDelta = GetBool(@params, true, "captureConsoleDelta", "CaptureConsoleDelta");
                }

                using (timing.Measure("service"))
                {
                    data = await SetComponentPropertyAsync(
                        target,
                        searchMethod,
                        componentTypeName,
                        componentIndex,
                        componentIndexSpecified,
                        memberName,
                        valueToken,
                        includeInactive,
                        requirePlayMode,
                        allowNonPublicSerializedField,
                        waitFrames,
                        captureConsoleDelta);
                    JObject root = JObject.FromObject(data);
                    success = string.Equals(root.Value<string>("status"), "ready", StringComparison.OrdinalIgnoreCase) &&
                        root["consoleErrorsDetected"]?.Value<bool>() != true;
                    errorKind = success ? null : root.Value<string>("reason") ?? "runtime_set_component_property_failed";
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
                    ? Response.Success("Runtime component property set completed.", ToolResultCompactor.ShapeStructuredPayload(
                        SetComponentPropertyToolName,
                        data,
                        BuildSetCompactData(data),
                        new { kind = "runtime_set_component_property_full_result" },
                        "runtime_set_component_property",
                        detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes))
                    : Response.Error("RUNTIME_SET_COMPONENT_PROPERTY_FAILED", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, success ? null : errorKind);
            return response;
        }

        [McpSchema(AddTemporaryComponentToolName)]
        public static object GetAddTemporaryComponentSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    target = new { type = "string", description = "Target GameObject name, hierarchy path, or signed/string object id." },
                    searchMethod = new { type = "string", description = "Target search method: by_name, by_path, by_id, or by_id_or_name_or_path. Defaults to by_name." },
                    componentType = new { type = "string", description = "Component type name. Short or fully-qualified names are accepted." },
                    includeInactive = new { type = "boolean", description = "Include inactive objects when resolving target. Defaults to false." },
                    requirePlayMode = new { type = "boolean", description = "Refuse addition outside Play Mode. Defaults to true." },
                    allowDuplicate = new { type = "boolean", description = "Allow adding when the target already has this component type. Defaults to false." },
                    markDontSave = new { type = "boolean", description = "Mark the added component with DontSave flags. Defaults to true." },
                    waitFrames = new { type = "integer", description = "Approximate runtime frames to wait after creation before collecting snapshot. Defaults to 0." },
                    captureConsoleDelta = new { type = "boolean", description = "Capture error-count delta before and after creation. Defaults to true." }
                },
                required = new[] { "target", "componentType" }
            };
        }

        [McpTool(AddTemporaryComponentToolName,
            "Adds a runtime-only component to a resolved GameObject in Play Mode and returns temporary-component evidence without saving scene or prefab state.",
            "Add Temporary Runtime Component",
            Groups = new[] { "runtime", "diagnostics" },
            EnabledByDefault = true)]
        public static async Task<object> AddTemporaryComponent(JObject @params)
        {
            @params ??= new JObject();
            var timing = new ToolOperationTiming(AddTemporaryComponentToolName, "add_temporary_component", PayloadBudgeting.GetUtf8ByteCount(@params.ToString(Formatting.None)));
            bool success = true;
            string errorKind = null;
            object data;

            try
            {
                string target;
                string searchMethod;
                string componentTypeName;
                bool includeInactive;
                bool requirePlayMode;
                bool allowDuplicate;
                bool markDontSave;
                int waitFrames;
                bool captureConsoleDelta;

                using (timing.Measure("normalization"))
                {
                    target = GetString(@params, "target", "Target");
                    searchMethod = GetString(@params, "searchMethod", "SearchMethod") ?? "by_name";
                    componentTypeName = GetString(@params, "componentType", "ComponentType");
                    includeInactive = GetBool(@params, false, "includeInactive", "IncludeInactive");
                    requirePlayMode = GetBool(@params, true, "requirePlayMode", "RequirePlayMode");
                    allowDuplicate = GetBool(@params, false, "allowDuplicate", "AllowDuplicate");
                    markDontSave = GetBool(@params, true, "markDontSave", "MarkDontSave");
                    waitFrames = Math.Clamp(GetInt(@params, 0, "waitFrames", "WaitFrames"), 0, 600);
                    captureConsoleDelta = GetBool(@params, true, "captureConsoleDelta", "CaptureConsoleDelta");
                }

                using (timing.Measure("service"))
                {
                    data = await AddTemporaryComponentAsync(
                        target,
                        searchMethod,
                        componentTypeName,
                        includeInactive,
                        requirePlayMode,
                        allowDuplicate,
                        markDontSave,
                        waitFrames,
                        captureConsoleDelta);
                    JObject root = JObject.FromObject(data);
                    success = string.Equals(root.Value<string>("status"), "ready", StringComparison.OrdinalIgnoreCase) &&
                        root["consoleErrorsDetected"]?.Value<bool>() != true;
                    errorKind = success ? null : root.Value<string>("reason") ?? "runtime_add_temporary_component_failed";
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
                    ? Response.Success("Temporary runtime component added.", ToolResultCompactor.ShapeStructuredPayload(
                        AddTemporaryComponentToolName,
                        data,
                        BuildAddCompactData(data),
                        new { kind = "runtime_add_temporary_component_full_result" },
                        "runtime_add_temporary_component",
                        detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes))
                    : Response.Error("RUNTIME_ADD_TEMPORARY_COMPONENT_FAILED", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, success ? null : errorKind);
            return response;
        }

        static async Task<object> SetComponentPropertyAsync(
            string target,
            string searchMethod,
            string componentTypeName,
            int componentIndex,
            bool componentIndexSpecified,
            string memberName,
            JToken valueToken,
            bool includeInactive,
            bool requirePlayMode,
            bool allowNonPublicSerializedField,
            int waitFrames,
            bool captureConsoleDelta)
        {
            if (requirePlayMode && !EditorApplication.isPlaying)
                return Failed("not_in_play_mode", "Runtime component property mutation requires Play Mode by default.", requirePlayMode);
            if (string.IsNullOrWhiteSpace(memberName))
                return Failed("member_name_required", "memberName is required.", requirePlayMode);
            if (valueToken == null)
                return Failed("value_required", "value is required.", requirePlayMode);
            if (!TryResolveComponent(target, searchMethod, componentTypeName, componentIndex, componentIndexSpecified, includeInactive, requirePlayMode, out GameObject targetObject, out Component component, out object failure))
                return failure;
            if (!TryResolveWritableMember(component.GetType(), memberName, allowNonPublicSerializedField, out RuntimeMemberAccessor member, out string memberError))
                return Failed("member_not_found", memberError, requirePlayMode);
            if (!TryConvertValue(valueToken, member.ValueType, out object convertedValue, out string conversionError))
                return Failed("value_conversion_failed", conversionError, requirePlayMode);

            bool sceneDirtyBefore = targetObject.scene.isDirty;
            ConsoleCursorSnapshot consoleBefore = captureConsoleDelta ? ConsoleCursorDelta.Capture() : null;
            object beforeFrameCounts = EditorToolStateHelpers.BuildRuntimeProbeData();
            object beforeValue = member.CanRead ? DescribeValue(member.GetValue(component)) : null;
            object beforeSnapshot = ComponentSummarySerializer.GetSafeComponentData(component, includeNonPublicSerializedFields: true);

            member.SetValue(component, convertedValue);

            if (waitFrames > 0)
                await Task.Delay(Math.Max(1, waitFrames) * 16);

            object afterFrameCounts = EditorToolStateHelpers.BuildRuntimeProbeData();
            object afterValue = member.CanRead ? DescribeValue(member.GetValue(component)) : null;
            object afterSnapshot = ComponentSummarySerializer.GetSafeComponentData(component, includeNonPublicSerializedFields: true);
            object consoleDelta = BuildConsoleDelta(captureConsoleDelta, consoleBefore, "set_component_property");

            return new
            {
                status = "ready",
                isPlaying = EditorApplication.isPlaying,
                requirePlayMode,
                target = BuildTargetSummary(targetObject),
                component = BuildComponentSummary(component, componentTypeName, componentIndex),
                member = new
                {
                    memberName = member.Name,
                    kind = member.Kind,
                    declaringType = member.DeclaringType,
                    valueType = member.ValueType.FullName ?? member.ValueType.Name,
                    allowNonPublicSerializedField,
                    readable = member.CanRead
                },
                requestedValue = valueToken,
                convertedValue = DescribeValue(convertedValue),
                beforeValue,
                afterValue,
                valueChanged = !JToken.DeepEquals(JToken.FromObject(beforeValue ?? new object()), JToken.FromObject(afterValue ?? new object())),
                waitFrames,
                frameCounts = new
                {
                    before = beforeFrameCounts,
                    after = afterFrameCounts
                },
                sceneDirty = new
                {
                    before = sceneDirtyBefore,
                    after = targetObject.scene.isDirty
                },
                beforeSnapshot,
                afterSnapshot,
                consoleDelta,
                consoleErrorsDetected = ConsoleDeltaHasErrors(consoleDelta)
            };
        }

        static async Task<object> AddTemporaryComponentAsync(
            string target,
            string searchMethod,
            string componentTypeName,
            bool includeInactive,
            bool requirePlayMode,
            bool allowDuplicate,
            bool markDontSave,
            int waitFrames,
            bool captureConsoleDelta)
        {
            if (requirePlayMode && !EditorApplication.isPlaying)
                return Failed("not_in_play_mode", "Temporary component creation requires Play Mode by default.", requirePlayMode);
            if (string.IsNullOrWhiteSpace(target))
                return Failed("target_required", "target is required.", requirePlayMode);
            if (string.IsNullOrWhiteSpace(componentTypeName))
                return Failed("component_type_required", "componentType is required.", requirePlayMode);
            if (!UnityComponentResolver.TryResolve(componentTypeName, out Type componentType, out string typeError))
                return Failed("component_type_not_found", typeError, requirePlayMode);
            if (componentType.IsAbstract || componentType.ContainsGenericParameters)
                return Failed("component_type_not_addable", $"Component type '{componentType.FullName}' is abstract or open generic.", requirePlayMode);
            if (!TryResolveSingleTarget(target, searchMethod, includeInactive, out GameObject targetObject, out object resolution, out string resolutionReason))
                return Failed(resolutionReason, $"Target '{target}' could not be resolved unambiguously using search method '{searchMethod}'.", requirePlayMode, resolution);

            Component[] beforeComponents = targetObject.GetComponents(componentType);
            if (!allowDuplicate && beforeComponents.Length > 0)
                return Failed("component_already_present", $"Target already has {beforeComponents.Length} component(s) assignable to '{componentTypeName}'. Set allowDuplicate=true to add another.", requirePlayMode);

            bool sceneDirtyBefore = targetObject.scene.isDirty;
            ConsoleCursorSnapshot consoleBefore = captureConsoleDelta ? ConsoleCursorDelta.Capture() : null;
            object beforeFrameCounts = EditorToolStateHelpers.BuildRuntimeProbeData();

            Component component = targetObject.AddComponent(componentType);
            if (markDontSave)
                component.hideFlags |= HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

            if (waitFrames > 0)
                await Task.Delay(Math.Max(1, waitFrames) * 16);

            object afterFrameCounts = EditorToolStateHelpers.BuildRuntimeProbeData();
            object componentSnapshot = ComponentSummarySerializer.GetSafeComponentData(component, includeNonPublicSerializedFields: true);
            object consoleDelta = BuildConsoleDelta(captureConsoleDelta, consoleBefore, "add_temporary_component");

            return new
            {
                status = "ready",
                isPlaying = EditorApplication.isPlaying,
                requirePlayMode,
                target = BuildTargetSummary(targetObject),
                component = BuildComponentSummary(component, componentTypeName, targetObject.GetComponents(componentType).ToList().IndexOf(component)),
                temporary = new
                {
                    playModeOnly = EditorApplication.isPlaying,
                    markDontSave,
                    hideFlags = component.hideFlags.ToString(),
                    expectedRemoval = EditorApplication.isPlaying ? "play_mode_exit" : "manual_cleanup_required",
                    allowDuplicate,
                    beforeCount = beforeComponents.Length,
                    afterCount = targetObject.GetComponents(componentType).Length
                },
                waitFrames,
                frameCounts = new
                {
                    before = beforeFrameCounts,
                    after = afterFrameCounts
                },
                sceneDirty = new
                {
                    before = sceneDirtyBefore,
                    after = targetObject.scene.isDirty
                },
                componentSnapshot,
                consoleDelta,
                consoleErrorsDetected = ConsoleDeltaHasErrors(consoleDelta)
            };
        }

        static object BuildSetCompactData(object data)
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
                member = root["member"],
                beforeValue = root["beforeValue"],
                afterValue = root["afterValue"],
                valueChanged = root["valueChanged"],
                waitFrames = root["waitFrames"],
                frameCounts = root["frameCounts"],
                sceneDirty = root["sceneDirty"],
                consoleDelta = root["consoleDelta"],
                consoleErrorsDetected = root["consoleErrorsDetected"]
            };
        }

        static object BuildAddCompactData(object data)
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
                temporary = root["temporary"],
                waitFrames = root["waitFrames"],
                frameCounts = root["frameCounts"],
                sceneDirty = root["sceneDirty"],
                consoleDelta = root["consoleDelta"],
                consoleErrorsDetected = root["consoleErrorsDetected"]
            };
        }

        static bool TryResolveComponent(
            string target,
            string searchMethod,
            string componentTypeName,
            int componentIndex,
            bool componentIndexSpecified,
            bool includeInactive,
            bool requirePlayMode,
            out GameObject targetObject,
            out Component component,
            out object failure)
        {
            targetObject = null;
            component = null;
            failure = null;
            if (string.IsNullOrWhiteSpace(target))
            {
                failure = Failed("target_required", "target is required.", requirePlayMode);
                return false;
            }
            if (string.IsNullOrWhiteSpace(componentTypeName))
            {
                failure = Failed("component_type_required", "componentType is required.", requirePlayMode);
                return false;
            }
            if (!UnityComponentResolver.TryResolve(componentTypeName, out Type componentType, out string typeError))
            {
                failure = Failed("component_type_not_found", typeError, requirePlayMode);
                return false;
            }
            if (!TryResolveSingleTarget(target, searchMethod, includeInactive, out targetObject, out object resolution, out string resolutionReason))
            {
                failure = Failed(resolutionReason, $"Target '{target}' could not be resolved unambiguously using search method '{searchMethod}'.", requirePlayMode, resolution);
                return false;
            }

            Component[] components = targetObject.GetComponents(componentType);
            if (components.Length <= componentIndex || components[componentIndex] == null)
            {
                failure = Failed("component_not_found", $"Component '{componentTypeName}' with index {componentIndex} was not found on '{UiDiagnosticsHelper.GetHierarchyPath(targetObject.transform)}'.", requirePlayMode);
                return false;
            }
            if (!componentIndexSpecified && components.Length > 1)
            {
                failure = Failed("component_ambiguous", $"Target has {components.Length} components assignable to '{componentTypeName}'. Provide componentIndex to choose one.", requirePlayMode);
                return false;
            }

            component = components[componentIndex];
            return true;
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
                    (method == "by_id" && ObjectIdEquals(candidate, target)) ||
                    (method == "by_path" && string.Equals(UiDiagnosticsHelper.GetHierarchyPath(candidate.transform), target, StringComparison.Ordinal)) ||
                    (method == "by_id_or_name_or_path" && (
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
                candidates = matches.Take(8).Select(BuildTargetSummary).ToArray(),
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

        static bool TryResolveWritableMember(Type componentType, string memberName, bool allowNonPublicSerializedField, out RuntimeMemberAccessor accessor, out string error)
        {
            accessor = null;
            error = null;
            var matches = new List<RuntimeMemberAccessor>();

            foreach (PropertyInfo property in componentType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => string.Equals(property.Name, memberName, StringComparison.Ordinal)))
            {
                MethodInfo getter = property.GetGetMethod(nonPublic: false);
                MethodInfo setter = property.GetSetMethod(nonPublic: false);
                if (getter != null && setter != null && property.GetIndexParameters().Length == 0)
                    matches.Add(RuntimeMemberAccessor.ForProperty(property));
            }

            foreach (FieldInfo field in componentType.GetFields(BindingFlags.Public | BindingFlags.Instance).Where(field => string.Equals(field.Name, memberName, StringComparison.Ordinal)))
            {
                if (!field.IsInitOnly && !field.IsLiteral)
                    matches.Add(RuntimeMemberAccessor.ForField(field, "publicField"));
            }

            if (allowNonPublicSerializedField)
            {
                foreach (FieldInfo field in componentType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Where(field => string.Equals(field.Name, memberName, StringComparison.Ordinal)))
                {
                    if (!field.IsInitOnly && !field.IsLiteral && field.GetCustomAttribute<SerializeField>() != null)
                        matches.Add(RuntimeMemberAccessor.ForField(field, "serializedField"));
                }
            }

            if (matches.Count == 1)
            {
                accessor = matches[0];
                return true;
            }

            error = matches.Count == 0
                ? $"No writable public property/field or allowed [SerializeField] field named '{memberName}' was found on '{componentType.FullName}'."
                : $"Multiple writable members named '{memberName}' were found on '{componentType.FullName}'; use a less ambiguous member name.";
            return false;
        }

        static bool TryConvertValue(JToken token, Type targetType, out object value, out string error)
        {
            value = null;
            error = null;
            if (token == null || token.Type == JTokenType.Null)
            {
                if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null)
                    return true;

                error = $"Cannot assign null to non-nullable value type '{targetType.FullName}'.";
                return false;
            }

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
                    if (TryResolveSingleTarget(token.Value<string>(), "by_id_or_name_or_path", includeInactive: true, out GameObject gameObject, out _, out _))
                    {
                        value = gameObject;
                        return true;
                    }
                }
                if (typeof(Component).IsAssignableFrom(nullableType) && token.Type == JTokenType.String)
                {
                    if (TryResolveSingleTarget(token.Value<string>(), "by_id_or_name_or_path", includeInactive: true, out GameObject gameObject, out _, out _))
                    {
                        value = gameObject.GetComponent(nullableType);
                        return value != null;
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            error = $"Value could not be converted to '{targetType.FullName}'. Supported value types are primitives, strings, enums, Vector2, Vector3, Color, GameObject, and Component references by target string.";
            return false;
        }

        static object BuildTargetSummary(GameObject gameObject)
        {
            return new
            {
                name = gameObject.name,
                path = UiDiagnosticsHelper.GetHierarchyPath(gameObject.transform),
                sceneName = gameObject.scene.name,
                scenePath = gameObject.scene.path,
                activeSelf = gameObject.activeSelf,
                activeInHierarchy = gameObject.activeInHierarchy,
                objectId = GetStableObjectId(gameObject),
                unityObjectId = UnityApiAdapter.GetObjectIdOrZero(gameObject)
            };
        }

        static object BuildComponentSummary(Component component, string requestedType, int componentIndex)
        {
            return new
            {
                requestedType,
                resolvedType = component.GetType().FullName,
                componentIndex,
                componentId = GetStableObjectId(component),
                unityObjectId = UnityApiAdapter.GetObjectIdOrZero(component),
                enabled = component is Behaviour behaviour ? behaviour.enabled : (bool?)null
            };
        }

        static object BuildConsoleDelta(bool captureConsoleDelta, ConsoleCursorSnapshot before, string operation)
        {
            return ConsoleCursorDelta.BuildDelta(
                captureConsoleDelta,
                before,
                operation == "add_temporary_component" ? AddTemporaryComponentToolName : SetComponentPropertyToolName,
                new { kind = "runtime_safe_action_console_delta", operation });
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

        static object Failed(string reason, string message, bool requirePlayMode)
        {
            return new
            {
                status = "failed",
                reason,
                message,
                isPlaying = EditorApplication.isPlaying,
                requirePlayMode
            };
        }

        static object Failed(string reason, string message, bool requirePlayMode, object resolution)
        {
            return new
            {
                status = "failed",
                reason,
                message,
                isPlaying = EditorApplication.isPlaying,
                requirePlayMode,
                resolution
            };
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
                    objectId = GetStableObjectId(unityObject),
                    unityObjectId = UnityApiAdapter.GetObjectIdOrZero(unityObject)
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

        sealed class RuntimeMemberAccessor
        {
            readonly Func<Component, object> getter;
            readonly Action<Component, object> setter;

            RuntimeMemberAccessor(string name, string kind, Type valueType, string declaringType, bool canRead, Func<Component, object> getter, Action<Component, object> setter)
            {
                Name = name;
                Kind = kind;
                ValueType = valueType;
                DeclaringType = declaringType;
                CanRead = canRead;
                this.getter = getter;
                this.setter = setter;
            }

            public string Name { get; }
            public string Kind { get; }
            public Type ValueType { get; }
            public string DeclaringType { get; }
            public bool CanRead { get; }

            public object GetValue(Component component) => getter(component);

            public void SetValue(Component component, object value) => setter(component, value);

            public static RuntimeMemberAccessor ForProperty(PropertyInfo property)
            {
                return new RuntimeMemberAccessor(
                    property.Name,
                    "publicProperty",
                    property.PropertyType,
                    property.DeclaringType?.FullName,
                    canRead: true,
                    component => property.GetValue(component, null),
                    (component, value) => property.SetValue(component, value, null));
            }

            public static RuntimeMemberAccessor ForField(FieldInfo field, string kind)
            {
                return new RuntimeMemberAccessor(
                    field.Name,
                    kind,
                    field.FieldType,
                    field.DeclaringType?.FullName,
                    canRead: true,
                    component => field.GetValue(component),
                    (component, value) => field.SetValue(component, value));
            }
        }
    }
}
