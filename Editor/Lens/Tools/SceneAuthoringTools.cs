#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Becool.UnityMcpLens.Editor.Adapters.Unity;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class SceneAuthoringTools
    {
        const string PreviewBindEventToolName = "Unity.Scene.PreviewBindUnityEvent";
        const string ApplyBindEventToolName = "Unity.Scene.ApplyBindUnityEvent";
        const string PreviewSaveReadbackToolName = "Unity.Scene.PreviewSaveAndReadback";
        const string ApplySaveReadbackToolName = "Unity.Scene.ApplySaveAndReadback";

        const string BindEventDescription = @"Previews or applies persistent UnityEvent listener bindings on scene components.

V1 supports void listeners and primitive argument listener modes when UnityEventTools supports the target event signature.";

        const string SaveReadbackDescription = @"Previews dirty scene state or saves open/requested scenes, then returns serialized property readbacks for scene objects.";

        [McpSchema(PreviewBindEventToolName)]
        public static object GetPreviewBindEventSchema() => BuildBindEventSchema();

        [McpSchema(ApplyBindEventToolName)]
        public static object GetApplyBindEventSchema() => BuildBindEventSchema();

        [McpSchema(PreviewSaveReadbackToolName)]
        public static object GetPreviewSaveReadbackSchema() => BuildSaveReadbackSchema();

        [McpSchema(ApplySaveReadbackToolName)]
        public static object GetApplySaveReadbackSchema() => BuildSaveReadbackSchema();

        [McpTool(PreviewBindEventToolName, BindEventDescription, "Preview Bind Unity Event", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object PreviewBindUnityEvent(JObject parameters) => HandleBindEvent(parameters, apply: false);

        [McpTool(ApplyBindEventToolName, BindEventDescription, "Apply Bind Unity Event", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object ApplyBindUnityEvent(JObject parameters) => HandleBindEvent(parameters, apply: true);

        [McpTool(PreviewSaveReadbackToolName, SaveReadbackDescription, "Preview Save And Readback", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object PreviewSaveAndReadback(JObject parameters) => HandleSaveReadback(parameters, apply: false);

        [McpTool(ApplySaveReadbackToolName, SaveReadbackDescription, "Apply Save And Readback", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object ApplySaveAndReadback(JObject parameters) => HandleSaveReadback(parameters, apply: true);

        static object BuildBindEventSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    bindings = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                target = new { description = "Scene object target for the event component." },
                                searchMethod = new { type = "string", @default = "by_name" },
                                targetPath = new { type = "string", description = "Relative child path under target.", @default = "." },
                                componentType = new { type = "string", description = "Component type that owns the UnityEvent." },
                                eventName = new { type = "string", description = "Public field/property event name, such as onClick." },
                                propertyPath = new { type = "string", description = "Alias for eventName in v1." },
                                componentIndex = new { type = "integer", @default = 0 },
                                replaceExisting = new { type = "boolean", @default = false },
                                listeners = new { type = "array", description = "Listener specs with target, componentType, methodName, mode, and argument." }
                            },
                            required = new[] { "target", "componentType", "listeners" }
                        }
                    }
                },
                required = new[] { "bindings" }
            };
        }

        static object BuildSaveReadbackSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    scenePath = new { type = "string", description = "Optional scene path to save/read; defaults to open scenes." },
                    saveOpenScenes = new { type = "boolean", @default = true },
                    readbacks = new { type = "array", description = "Scene serialized property readbacks." }
                }
            };
        }

        static object HandleBindEvent(JObject parameters, bool apply)
        {
            parameters ??= new JObject();
            string toolName = apply ? ApplyBindEventToolName : PreviewBindEventToolName;
            string action = apply ? "apply_bind_unity_event" : "preview_bind_unity_event";
            var timing = new ToolOperationTiming(toolName, action, PayloadBudgeting.GetUtf8ByteCount(parameters.ToString(Formatting.None)));
            object data;
            bool success = true;
            string errorKind = null;
            try
            {
                JArray bindings;
                using (timing.Measure("normalization"))
                {
                    bindings = parameters["bindings"] as JArray ?? parameters["Bindings"] as JArray ?? new JArray();
                    if (bindings.Count == 0)
                        throw new ArgumentException("At least one binding is required.");
                }
                using (timing.Measure("service")) { }
                using (timing.Measure("adapter"))
                {
                    data = RunBindEvent(bindings, apply);
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
                    ? Response.Success(apply ? "Applied UnityEvent bindings." : "Previewed UnityEvent bindings.",
                        ToolResultCompactor.ShapeStructuredPayload(toolName, data, BuildCompactData(data), new { kind = "scene_unity_event_bind_full_result" }, "scene_unity_event_bind"))
                    : Response.Error("UnityEvent binding failed.", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }
            timing.Record(success, errorKind);
            return response;
        }

        static object HandleSaveReadback(JObject parameters, bool apply)
        {
            parameters ??= new JObject();
            string toolName = apply ? ApplySaveReadbackToolName : PreviewSaveReadbackToolName;
            string action = apply ? "apply_save_readback" : "preview_save_readback";
            var timing = new ToolOperationTiming(toolName, action, PayloadBudgeting.GetUtf8ByteCount(parameters.ToString(Formatting.None)));
            object data;
            bool success = true;
            string errorKind = null;
            try
            {
                using (timing.Measure("normalization")) { }
                using (timing.Measure("service")) { }
                using (timing.Measure("adapter"))
                {
                    data = RunSaveReadback(parameters, apply);
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
                    ? Response.Success(apply ? "Saved scenes and captured readbacks." : "Previewed scene save/readback.",
                        ToolResultCompactor.ShapeStructuredPayload(toolName, data, BuildCompactData(data), new { kind = "scene_save_readback_full_result" }, "scene_save_readback"))
                    : Response.Error("Scene save/readback failed.", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }
            timing.Record(success, errorKind);
            return response;
        }

        static object RunBindEvent(JArray bindings, bool apply)
        {
            var rows = new List<object>();
            var issues = new List<object>();
            bool willModify = false;
            bool applied = false;

            foreach (JObject binding in bindings.OfType<JObject>())
            {
                if (!TryResolveEvent(binding, out Component eventComponent, out UnityEventBase unityEvent, out string eventName, out string error))
                {
                    issues.Add(new { code = "event_resolution_failed", severity = "error", message = error });
                    continue;
                }

                bool replaceExisting = binding["replaceExisting"]?.ToObject<bool?>() ?? binding["ReplaceExisting"]?.ToObject<bool?>() ?? false;
                var listenerRows = new List<object>();
                JArray listeners = binding["listeners"] as JArray ?? binding["Listeners"] as JArray ?? new JArray();
                foreach (JObject listener in listeners.OfType<JObject>())
                {
                    if (!TryResolveListener(listener, out UnityEngine.Object listenerTarget, out MethodInfo method, out string mode, out JToken argument, out error))
                    {
                        issues.Add(new { code = "listener_resolution_failed", severity = "error", message = error });
                        continue;
                    }

                    bool alreadyBound = HasPersistentListener(unityEvent, listenerTarget, method.Name);
                    bool listenerChanged = replaceExisting || !alreadyBound;
                    willModify |= listenerChanged;
                    listenerRows.Add(new
                    {
                        target = listenerTarget.name,
                        componentType = listenerTarget.GetType().FullName,
                        method = method.Name,
                        mode,
                        alreadyBound,
                        changed = listenerChanged
                    });

                    if (apply && listenerChanged)
                    {
                        if (replaceExisting)
                        {
                            for (int i = unityEvent.GetPersistentEventCount() - 1; i >= 0; i--)
                                UnityEventTools.RemovePersistentListener(unityEvent, i);
                            replaceExisting = false;
                        }

                        if (!TryAddPersistentListener(unityEvent, listenerTarget, method, mode, argument, out error))
                        {
                            issues.Add(new { code = "listener_binding_failed", severity = "error", message = error });
                            continue;
                        }

                        applied = true;
                    }
                }

                if (apply && applied)
                {
                    EditorUtility.SetDirty(eventComponent);
                    EditorSceneManager.MarkSceneDirty(eventComponent.gameObject.scene);
                }

                rows.Add(new
                {
                    target = UiDiagnosticsHelper.GetHierarchyPath(eventComponent.transform),
                    componentType = eventComponent.GetType().FullName,
                    eventName,
                    replaceExisting = binding["replaceExisting"]?.ToObject<bool?>() ?? binding["ReplaceExisting"]?.ToObject<bool?>() ?? false,
                    persistentListenerCount = unityEvent.GetPersistentEventCount(),
                    listeners = listenerRows.ToArray()
                });
            }

            if (apply && applied)
                EditorSceneManager.SaveOpenScenes();

            return new
            {
                willModify,
                applied,
                bindingCount = rows.Count,
                bindings = rows.ToArray(),
                issues = issues.ToArray()
            };
        }

        static object RunSaveReadback(JObject parameters, bool apply)
        {
            string scenePath = parameters["scenePath"]?.ToString() ?? parameters["ScenePath"]?.ToString();
            bool saveOpenScenes = parameters["saveOpenScenes"]?.ToObject<bool?>() ?? parameters["SaveOpenScenes"]?.ToObject<bool?>() ?? true;
            var beforeScenes = EnumerateScenes().ToArray();
            bool saved = false;
            string saveError = null;

            if (apply)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(scenePath))
                    {
                        Scene scene = FindLoadedScene(scenePath);
                        if (!scene.IsValid())
                            throw new InvalidOperationException($"Scene '{scenePath}' is not loaded.");
                        saved = EditorSceneManager.SaveScene(scene);
                    }
                    else if (saveOpenScenes)
                    {
                        saved = EditorSceneManager.SaveOpenScenes();
                    }
                }
                catch (Exception ex)
                {
                    saveError = ex.Message;
                }
            }

            var readbacks = new List<object>();
            foreach (JObject readback in (parameters["readbacks"] as JArray ?? parameters["Readbacks"] as JArray ?? new JArray()).OfType<JObject>())
            {
                readbacks.Add(ReadSerializedProperty(readback));
            }

            return new
            {
                previewOnly = !apply,
                applied = apply && saved && string.IsNullOrWhiteSpace(saveError),
                saved,
                saveError,
                scenesBefore = beforeScenes,
                scenesAfter = EnumerateScenes().ToArray(),
                readbackCount = readbacks.Count,
                readbacks = readbacks.ToArray()
            };
        }

        static bool TryResolveEvent(JObject binding, out Component component, out UnityEventBase unityEvent, out string eventName, out string error)
        {
            component = null;
            unityEvent = null;
            eventName = binding["eventName"]?.ToString() ?? binding["EventName"]?.ToString() ?? binding["propertyPath"]?.ToString() ?? binding["PropertyPath"]?.ToString();
            error = null;
            if (string.IsNullOrWhiteSpace(eventName))
                eventName = "onClick";

            if (!TryResolveComponent(binding, out component, out error))
                return false;

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo property = component.GetType().GetProperty(eventName, flags);
            object eventValue = property != null ? property.GetValue(component) : null;
            if (eventValue == null)
            {
                FieldInfo field = component.GetType().GetField(eventName, flags);
                eventValue = field?.GetValue(component);
            }

            unityEvent = eventValue as UnityEventBase;
            if (unityEvent == null)
            {
                error = $"Event '{eventName}' on '{component.GetType().FullName}' is not a UnityEventBase field/property.";
                return false;
            }
            return true;
        }

        static bool TryResolveComponent(JObject spec, out Component component, out string error)
        {
            component = null;
            error = null;
            JToken target = spec["target"] ?? spec["Target"];
            string searchMethod = spec["searchMethod"]?.ToString() ?? spec["SearchMethod"]?.ToString() ?? "by_name";
            string targetPath = spec["targetPath"]?.ToString() ?? spec["TargetPath"]?.ToString() ?? ".";
            string componentTypeName = spec["componentType"]?.ToString() ?? spec["ComponentType"]?.ToString();
            int componentIndex = spec["componentIndex"]?.ToObject<int?>() ?? spec["ComponentIndex"]?.ToObject<int?>() ?? 0;
            if (target == null || string.IsNullOrWhiteSpace(componentTypeName))
            {
                error = "target and componentType are required.";
                return false;
            }

            JObject findParams = new() { ["search_inactive"] = true };
            GameObject root = ObjectsHelper.FindObject(target, searchMethod, findParams);
            if (root == null)
            {
                error = "Target scene object could not be resolved.";
                return false;
            }

            Transform transform = targetPath == "." ? root.transform : root.transform.Find(targetPath);
            if (transform == null)
            {
                error = $"TargetPath '{targetPath}' was not found under '{UiDiagnosticsHelper.GetHierarchyPath(root.transform)}'.";
                return false;
            }

            Type componentType = UnityComponentResolver.FindType(componentTypeName);
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                error = $"Component type '{componentTypeName}' could not be resolved.";
                return false;
            }

            Component[] matches = transform.GetComponents(componentType);
            if (matches.Length <= componentIndex)
            {
                error = $"Component '{componentTypeName}' index {componentIndex} was not found on '{UiDiagnosticsHelper.GetHierarchyPath(transform)}'.";
                return false;
            }

            component = matches[componentIndex];
            return true;
        }

        static bool TryResolveListener(JObject listener, out UnityEngine.Object target, out MethodInfo method, out string mode, out JToken argument, out string error)
        {
            target = null;
            method = null;
            mode = listener["mode"]?.ToString() ?? listener["Mode"]?.ToString() ?? "void";
            argument = listener["argument"] ?? listener["Argument"];
            error = null;
            if (!TryResolveComponent(listener, out Component component, out error))
                return false;

            string methodName = listener["methodName"]?.ToString() ?? listener["MethodName"]?.ToString();
            if (string.IsNullOrWhiteSpace(methodName))
            {
                error = "Listener methodName is required.";
                return false;
            }

            Type[] parameterTypes = GetModeParameterTypes(mode);
            method = component.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, parameterTypes, null);
            if (method == null || method.ReturnType != typeof(void))
            {
                error = $"Listener method '{methodName}' with mode '{mode}' was not found or is not void.";
                return false;
            }

            target = component;
            return true;
        }

        static bool TryAddPersistentListener(UnityEventBase unityEvent, UnityEngine.Object target, MethodInfo method, string mode, JToken argument, out string error)
        {
            error = null;
            try
            {
                string normalized = (mode ?? "void").Trim().ToLowerInvariant();
                if (normalized == "void")
                {
                    if (unityEvent is not UnityEvent typedEvent)
                    {
                        error = "Void listener mode requires an event assignable to UnityEvent.";
                        return false;
                    }
                    var unityAction = (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), target, method, throwOnBindFailure: false);
                    if (unityAction == null)
                    {
                        error = "Could not bind method as UnityAction.";
                        return false;
                    }
                    UnityEventTools.AddPersistentListener(typedEvent, unityAction);
                    return true;
                }

                Type argType = normalized switch
                {
                    "bool" => typeof(bool),
                    "int" => typeof(int),
                    "float" => typeof(float),
                    "string" => typeof(string),
                    _ => null
                };
                if (argType == null)
                {
                    error = $"Unsupported listener mode '{mode}'.";
                    return false;
                }

                Type actionType = typeof(UnityAction<>).MakeGenericType(argType);
                Delegate action = Delegate.CreateDelegate(actionType, target, method, throwOnBindFailure: false);
                if (action == null)
                {
                    error = $"Could not bind method as UnityAction<{argType.Name}>.";
                    return false;
                }

                object coercedArgument = CoerceArgument(argument, argType);
                string methodName = normalized switch
                {
                    "bool" => "AddBoolPersistentListener",
                    "int" => "AddIntPersistentListener",
                    "float" => "AddFloatPersistentListener",
                    "string" => "AddStringPersistentListener",
                    _ => null
                };
                MethodInfo unityEventTool = typeof(UnityEventTools).GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(candidate => candidate.Name == methodName && candidate.GetParameters().Length == 3);
                if (unityEventTool == null)
                {
                    error = $"UnityEventTools.{methodName} was not found.";
                    return false;
                }

                unityEventTool.Invoke(null, new[] { unityEvent, action, coercedArgument });
                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }

        static Type[] GetModeParameterTypes(string mode)
        {
            return (mode ?? "void").Trim().ToLowerInvariant() switch
            {
                "bool" => new[] { typeof(bool) },
                "int" => new[] { typeof(int) },
                "float" => new[] { typeof(float) },
                "string" => new[] { typeof(string) },
                _ => Type.EmptyTypes
            };
        }

        static object CoerceArgument(JToken argument, Type type)
        {
            if (type == typeof(bool))
                return argument?.ToObject<bool>() ?? false;
            if (type == typeof(int))
                return argument?.ToObject<int>() ?? 0;
            if (type == typeof(float))
                return argument?.ToObject<float>() ?? 0f;
            if (type == typeof(string))
                return argument?.ToString() ?? string.Empty;
            return null;
        }

        static bool HasPersistentListener(UnityEventBase unityEvent, UnityEngine.Object target, string methodName)
        {
            for (int i = 0; i < unityEvent.GetPersistentEventCount(); i++)
            {
                if (unityEvent.GetPersistentTarget(i) == target &&
                    string.Equals(unityEvent.GetPersistentMethodName(i), methodName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        static object ReadSerializedProperty(JObject spec)
        {
            if (!TryResolveComponent(spec, out Component component, out string error))
                return new { success = false, error };

            string propertyPath = spec["propertyPath"]?.ToString() ?? spec["PropertyPath"]?.ToString();
            if (string.IsNullOrWhiteSpace(propertyPath))
                return new { success = false, error = "propertyPath is required." };

            var serializedObject = new SerializedObject(component);
            SerializedProperty property = serializedObject.FindProperty(propertyPath);
            if (property == null)
                return new { success = false, error = $"Serialized property '{propertyPath}' was not found." };

            return new
            {
                success = true,
                target = UiDiagnosticsHelper.GetHierarchyPath(component.transform),
                componentType = component.GetType().FullName,
                propertyPath,
                propertyType = property.propertyType.ToString(),
                value = DescribeProperty(property)
            };
        }

        static string DescribeProperty(SerializedProperty property)
        {
            return property.propertyType switch
            {
                SerializedPropertyType.Boolean => property.boolValue.ToString(),
                SerializedPropertyType.Integer => property.intValue.ToString(),
                SerializedPropertyType.Float => property.floatValue.ToString("R"),
                SerializedPropertyType.String => property.stringValue ?? string.Empty,
                SerializedPropertyType.Enum => property.enumDisplayNames.ElementAtOrDefault(property.enumValueIndex) ?? property.enumValueIndex.ToString(),
                SerializedPropertyType.ObjectReference => DescribeObjectReference(property.objectReferenceValue),
                _ => property.propertyType.ToString()
            };
        }

        static string DescribeObjectReference(UnityEngine.Object value)
        {
            if (value == null)
                return "null";
            if (value is Component component)
                return UiDiagnosticsHelper.GetHierarchyPath(component.transform);
            if (value is GameObject gameObject)
                return UiDiagnosticsHelper.GetHierarchyPath(gameObject.transform);
            string assetPath = AssetDatabase.GetAssetPath(value);
            return string.IsNullOrWhiteSpace(assetPath) ? value.name : assetPath;
        }

        static Scene FindLoadedScene(string scenePath)
        {
            string normalized = scenePath.Replace('\\', '/');
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (string.Equals(scene.path, normalized, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(scene.name, normalized, StringComparison.OrdinalIgnoreCase))
                    return scene;
            }
            return default;
        }

        static object[] EnumerateScenes()
        {
            var rows = new List<object>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                rows.Add(new
                {
                    name = scene.name,
                    path = scene.path,
                    isLoaded = scene.isLoaded,
                    isDirty = scene.isDirty,
                    isValid = scene.IsValid()
                });
            }
            return rows.ToArray();
        }

        static object BuildCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            JArray bindings = root["bindings"] as JArray ?? new JArray();
            JArray readbacks = root["readbacks"] as JArray ?? new JArray();
            JArray issues = root["issues"] as JArray ?? new JArray();
            return new
            {
                willModify = root["willModify"],
                applied = root["applied"],
                saved = root["saved"],
                saveError = root["saveError"],
                bindingCount = root["bindingCount"],
                readbackCount = root["readbackCount"],
                bindings = bindings.Take(12).ToArray(),
                readbacks = readbacks.Take(12).ToArray(),
                issues
            };
        }
    }
}
