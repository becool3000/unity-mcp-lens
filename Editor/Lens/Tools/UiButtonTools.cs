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
using Becool.UnityMcpLens.Editor.Tools.Parameters;
using Becool.UnityMcpLens.Editor.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class UiButtonTools
    {
        const string PreviewToolName = "Unity.UI.PreviewEnsureButton";
        const string ApplyToolName = "Unity.UI.ApplyEnsureButton";

        const string Description = @"Previews or applies a focused uGUI Button authoring operation under a scene UI parent.

Creates/updates RectTransform, Image, Button, a child legacy Text label, optional layout/display values, and an optional void onClick listener.";

        sealed class Request
        {
            public JToken parentTarget;
            public string parentSearchMethod = "by_name";
            public bool includeInactive = true;
            public string buttonName;
            public string buttonPath;
            public string labelText;
            public JToken layout;
            public JToken imageColor;
            public JToken textColor;
            public bool? interactable;
            public JObject onClick;
        }

        [McpSchema(PreviewToolName)]
        public static object GetPreviewSchema() => BuildSchema();

        [McpSchema(ApplyToolName)]
        public static object GetApplySchema() => BuildSchema();

        [McpTool(PreviewToolName, Description, "Preview Ensure UI Button", Groups = new[] { "ui" }, EnabledByDefault = true)]
        public static object PreviewEnsureButton(JObject parameters) => Handle(parameters, apply: false);

        [McpTool(ApplyToolName, Description, "Apply Ensure UI Button", Groups = new[] { "ui" }, EnabledByDefault = true)]
        public static object ApplyEnsureButton(JObject parameters) => Handle(parameters, apply: true);

        static object BuildSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    parentTarget = new { description = "Scene parent GameObject, path, or instance id." },
                    parentSearchMethod = new { type = "string", description = "How to resolve parentTarget.", @default = "by_name" },
                    includeInactive = new { type = "boolean", description = "Include inactive scene objects.", @default = true },
                    buttonName = new { type = "string", description = "Button GameObject name. Used when buttonPath is omitted." },
                    buttonPath = new { type = "string", description = "Relative path under parentTarget for the button." },
                    labelText = new { type = "string", description = "Legacy Text label content." },
                    layout = new { description = "Optional RectTransform/layout block using the same fields as Unity.UI.ApplyLayoutProperties." },
                    imageColor = new { description = "Optional button Image color {r,g,b,a} or [r,g,b,a]." },
                    textColor = new { description = "Optional label Text color {r,g,b,a} or [r,g,b,a]." },
                    interactable = new { type = "boolean", description = "Optional Button.interactable value." },
                    onClick = new
                    {
                        type = "object",
                        description = "Optional void listener binding.",
                        properties = new
                        {
                            target = new { description = "Listener target scene object." },
                            searchMethod = new { type = "string", description = "Listener target search method.", @default = "by_name" },
                            componentType = new { type = "string", description = "Component type on listener target." },
                            methodName = new { type = "string", description = "Public or non-public instance void method with no parameters." },
                            replaceExisting = new { type = "boolean", description = "Remove existing persistent listeners before adding.", @default = false }
                        }
                    }
                },
                required = new[] { "parentTarget" }
            };
        }

        static object Handle(JObject parameters, bool apply)
        {
            parameters ??= new JObject();
            string toolName = apply ? ApplyToolName : PreviewToolName;
            string action = apply ? "apply_ensure_button" : "preview_ensure_button";
            var timing = new ToolOperationTiming(toolName, action, PayloadBudgeting.GetUtf8ByteCount(parameters.ToString(Formatting.None)));
            object data;
            bool success = true;
            string errorKind = null;

            try
            {
                Request request;
                using (timing.Measure("normalization"))
                {
                    request = Normalize(parameters);
                }

                using (timing.Measure("service"))
                {
                    Validate(request);
                }

                using (timing.Measure("adapter"))
                {
                    data = Run(request, apply);
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
                    ? Response.Success(apply ? "Applied UI button authoring." : "Previewed UI button authoring.",
                        ToolResultCompactor.ShapeStructuredPayload(toolName, data, BuildCompactData(data), new { kind = "ui_button_full_result" }, "ui_button"))
                    : Response.Error("UI button authoring failed.", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, errorKind);
            return response;
        }

        static Request Normalize(JObject parameters)
        {
            return new Request
            {
                parentTarget = parameters["parentTarget"] ?? parameters["ParentTarget"] ?? parameters["target"] ?? parameters["Target"],
                parentSearchMethod = parameters["parentSearchMethod"]?.ToString() ?? parameters["ParentSearchMethod"]?.ToString() ?? parameters["searchMethod"]?.ToString() ?? "by_name",
                includeInactive = parameters["includeInactive"]?.ToObject<bool?>() ?? parameters["IncludeInactive"]?.ToObject<bool?>() ?? true,
                buttonName = parameters["buttonName"]?.ToString() ?? parameters["ButtonName"]?.ToString(),
                buttonPath = parameters["buttonPath"]?.ToString() ?? parameters["ButtonPath"]?.ToString(),
                labelText = parameters["labelText"]?.ToString() ?? parameters["LabelText"]?.ToString(),
                layout = parameters["layout"] ?? parameters["Layout"],
                imageColor = parameters["imageColor"] ?? parameters["ImageColor"],
                textColor = parameters["textColor"] ?? parameters["TextColor"],
                interactable = parameters["interactable"]?.ToObject<bool?>() ?? parameters["Interactable"]?.ToObject<bool?>(),
                onClick = parameters["onClick"] as JObject ?? parameters["OnClick"] as JObject
            };
        }

        static void Validate(Request request)
        {
            if (request.parentTarget == null || request.parentTarget.Type == JTokenType.Null)
                throw new ArgumentException("parentTarget is required.");

            if (string.IsNullOrWhiteSpace(request.buttonPath) && string.IsNullOrWhiteSpace(request.buttonName))
                request.buttonName = "Button";
        }

        static object Run(Request request, bool apply)
        {
            if (!UiAuthoringTools.TryResolveRoot(request.parentTarget, request.parentSearchMethod, request.includeInactive, out GameObject parentRoot, out string error))
                throw new InvalidOperationException(error);

            string relativePath = string.IsNullOrWhiteSpace(request.buttonPath) ? request.buttonName.Trim() : request.buttonPath.Trim().Replace('\\', '/');
            string buttonName = string.IsNullOrWhiteSpace(request.buttonName) ? relativePath.Split('/').Last() : request.buttonName.Trim();
            Transform existing = ResolveRelative(parentRoot.transform, relativePath);
            var changes = new List<object>();
            var issues = new List<object>();
            bool willModify = existing == null;
            bool applied = false;

            GameObject buttonObject = existing != null ? existing.gameObject : null;
            if (buttonObject == null)
            {
                changes.Add(new { property = "button", action = "create", path = relativePath });
                if (apply)
                {
                    buttonObject = CreatePath(parentRoot.transform, relativePath, buttonName);
                    applied = true;
                }
            }

            if (buttonObject != null)
            {
                EnsureComponent<RectTransform>(buttonObject, apply, changes, ref willModify, ref applied);
                EnsureComponent<Image>(buttonObject, apply, changes, ref willModify, ref applied);
                EnsureComponent<Button>(buttonObject, apply, changes, ref willModify, ref applied);

                Button button = buttonObject.GetComponent<Button>();
                Image image = buttonObject.GetComponent<Image>();
                if (request.interactable.HasValue && button != null && button.interactable != request.interactable.Value)
                {
                    changes.Add(new { property = "button.interactable", previousValue = button.interactable, newValue = request.interactable.Value });
                    willModify = true;
                    if (apply)
                    {
                        button.interactable = request.interactable.Value;
                        EditorUtility.SetDirty(button);
                        applied = true;
                    }
                }

                if (request.imageColor != null && request.imageColor.Type != JTokenType.Null)
                {
                    if (!UiAuthoringTools.TryParseColor(request.imageColor, out Color color))
                        throw new ArgumentException("imageColor must be {r,g,b,a} or [r,g,b,a].");
                    if (image != null && image.color != color)
                    {
                        changes.Add(new { property = "image.color", previousValue = ToColorObject(image.color), newValue = ToColorObject(color) });
                        willModify = true;
                        if (apply)
                        {
                            image.color = color;
                            EditorUtility.SetDirty(image);
                            applied = true;
                        }
                    }
                }

                if (request.layout is JObject layoutObject)
                {
                    var layoutParams = BuildLayoutParams(layoutObject, previewOnly: !apply);
                    if (!UiAuthoringTools.ApplyLayoutChanges(buttonObject, layoutParams, changes, out bool layoutWouldModify, out error))
                        throw new InvalidOperationException(error);
                    willModify |= layoutWouldModify;
                    applied |= apply && layoutWouldModify;
                }

                GameObject label = EnsureLabel(buttonObject, request.labelText, request.textColor, apply, changes, ref willModify, ref applied);
                if (request.onClick != null)
                {
                    if (!TryBindOnClick(button, request.onClick, apply, out object listenerRow, out error))
                    {
                        issues.Add(new { code = "listener_binding_failed", severity = "error", message = error });
                    }
                    else
                    {
                        changes.Add(listenerRow);
                        bool listenerChanged = JObject.FromObject(listenerRow)["changed"]?.Value<bool>() == true;
                        willModify |= listenerChanged;
                        applied |= apply && listenerChanged;
                    }
                }

                if (apply && applied)
                {
                    EditorUtility.SetDirty(buttonObject);
                    if (label != null)
                        EditorUtility.SetDirty(label);
                    EditorSceneManager.MarkSceneDirty(buttonObject.scene);
                    EditorSceneManager.SaveOpenScenes();
                }
            }

            return new
            {
                parent = UiDiagnosticsHelper.GetHierarchyPath(parentRoot.transform),
                buttonPath = buttonObject != null ? UiDiagnosticsHelper.GetHierarchyPath(buttonObject.transform) : UiDiagnosticsHelper.GetHierarchyPath(parentRoot.transform) + "/" + relativePath,
                exists = existing != null,
                willModify,
                applied,
                changeCount = changes.Count,
                changes = changes.ToArray(),
                issues = issues.ToArray()
            };
        }

        static SetUiLayoutPropertiesParams BuildLayoutParams(JObject layout, bool previewOnly)
        {
            return new SetUiLayoutPropertiesParams
            {
                PreviewOnly = previewOnly,
                AnchorMin = layout["anchorMin"] ?? layout["AnchorMin"],
                AnchorMax = layout["anchorMax"] ?? layout["AnchorMax"],
                Pivot = layout["pivot"] ?? layout["Pivot"],
                SizeDelta = layout["sizeDelta"] ?? layout["SizeDelta"],
                AnchoredPosition = layout["anchoredPosition"] ?? layout["AnchoredPosition"],
                SiblingIndex = layout["siblingIndex"]?.ToObject<int?>() ?? layout["SiblingIndex"]?.ToObject<int?>(),
                Active = layout["active"]?.ToObject<bool?>() ?? layout["Active"]?.ToObject<bool?>()
            };
        }

        static void EnsureComponent<T>(GameObject target, bool apply, List<object> changes, ref bool willModify, ref bool applied) where T : Component
        {
            if (target.GetComponent<T>() != null)
                return;

            changes.Add(new { property = "component", previousValue = (string)null, newValue = typeof(T).FullName });
            willModify = true;
            if (apply)
            {
                Undo.AddComponent<T>(target);
                applied = true;
            }
        }

        static GameObject EnsureLabel(GameObject buttonObject, string labelText, JToken textColor, bool apply, List<object> changes, ref bool willModify, ref bool applied)
        {
            Transform labelTransform = buttonObject.transform.Find("Text");
            GameObject label = labelTransform != null ? labelTransform.gameObject : null;
            if (label == null)
            {
                changes.Add(new { property = "label", action = "create", path = "Text" });
                willModify = true;
                if (!apply)
                    return null;

                label = new GameObject("Text", typeof(RectTransform), typeof(Text));
                Undo.RegisterCreatedObjectUndo(label, "Create button label");
                label.transform.SetParent(buttonObject.transform, false);
                RectTransform rect = label.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                applied = true;
            }

            Text text = label.GetComponent<Text>();
            if (text == null)
            {
                changes.Add(new { property = "label.component", previousValue = (string)null, newValue = typeof(Text).FullName });
                willModify = true;
                if (apply)
                {
                    text = Undo.AddComponent<Text>(label);
                    applied = true;
                }
            }

            if (text != null)
            {
                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (text.font == null && font != null)
                {
                    changes.Add(new { property = "label.font", previousValue = (string)null, newValue = font.name });
                    willModify = true;
                    if (apply)
                    {
                        text.font = font;
                        applied = true;
                    }
                }

                if (labelText != null && text.text != labelText)
                {
                    changes.Add(new { property = "label.text", previousValue = text.text, newValue = labelText });
                    willModify = true;
                    if (apply)
                    {
                        text.text = labelText;
                        applied = true;
                    }
                }

                if (textColor != null && textColor.Type != JTokenType.Null)
                {
                    if (!UiAuthoringTools.TryParseColor(textColor, out Color color))
                        throw new ArgumentException("textColor must be {r,g,b,a} or [r,g,b,a].");
                    if (text.color != color)
                    {
                        changes.Add(new { property = "label.color", previousValue = ToColorObject(text.color), newValue = ToColorObject(color) });
                        willModify = true;
                        if (apply)
                        {
                            text.color = color;
                            applied = true;
                        }
                    }
                }

                if (apply)
                    EditorUtility.SetDirty(text);
            }

            return label;
        }

        static bool TryBindOnClick(Button button, JObject listenerSpec, bool apply, out object row, out string error)
        {
            row = null;
            error = null;
            if (button == null)
            {
                error = "Button component could not be resolved.";
                return false;
            }

            if (!ResolveListener(listenerSpec, out UnityEngine.Object targetObject, out MethodInfo method, out error))
                return false;

            bool replaceExisting = listenerSpec["replaceExisting"]?.ToObject<bool?>() ?? listenerSpec["ReplaceExisting"]?.ToObject<bool?>() ?? false;
            bool alreadyBound = HasPersistentListener(button.onClick, targetObject, method.Name);
            bool changed = replaceExisting || !alreadyBound;
            row = new
            {
                property = "button.onClick",
                target = targetObject != null ? targetObject.name : null,
                method = method.Name,
                replaceExisting,
                alreadyBound,
                changed
            };

            if (!apply || !changed)
                return true;

            if (replaceExisting)
            {
                for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                    UnityEventTools.RemovePersistentListener(button.onClick, i);
            }

            var action = (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), targetObject, method, throwOnBindFailure: false);
            if (action == null)
            {
                error = $"Method '{method.Name}' could not be bound as UnityAction.";
                return false;
            }

            UnityEventTools.AddPersistentListener(button.onClick, action);
            EditorUtility.SetDirty(button);
            return true;
        }

        static bool ResolveListener(JObject listenerSpec, out UnityEngine.Object targetObject, out MethodInfo method, out string error)
        {
            targetObject = null;
            method = null;
            error = null;
            JToken targetToken = listenerSpec["target"] ?? listenerSpec["Target"];
            string searchMethod = listenerSpec["searchMethod"]?.ToString() ?? listenerSpec["SearchMethod"]?.ToString() ?? "by_name";
            string componentTypeName = listenerSpec["componentType"]?.ToString() ?? listenerSpec["ComponentType"]?.ToString();
            string methodName = listenerSpec["methodName"]?.ToString() ?? listenerSpec["MethodName"]?.ToString();
            if (targetToken == null || string.IsNullOrWhiteSpace(componentTypeName) || string.IsNullOrWhiteSpace(methodName))
            {
                error = "onClick requires target, componentType, and methodName.";
                return false;
            }

            JObject findParams = new() { ["search_inactive"] = true };
            GameObject go = ObjectsHelper.FindObject(targetToken, searchMethod, findParams);
            if (go == null)
            {
                error = "onClick target could not be resolved.";
                return false;
            }

            Type componentType = UnityComponentResolver.FindType(componentTypeName);
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                error = $"Component type '{componentTypeName}' could not be resolved.";
                return false;
            }

            Component component = go.GetComponent(componentType);
            if (component == null)
            {
                error = $"Component '{componentTypeName}' was not found on '{UiDiagnosticsHelper.GetHierarchyPath(go.transform)}'.";
                return false;
            }

            method = componentType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (method == null || method.ReturnType != typeof(void))
            {
                error = $"Method '{methodName}' must be an instance void method with no parameters.";
                return false;
            }

            targetObject = component;
            return true;
        }

        static bool HasPersistentListener(UnityEvent unityEvent, UnityEngine.Object target, string methodName)
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

        static Transform ResolveRelative(Transform root, string relativePath)
        {
            if (root == null || string.IsNullOrWhiteSpace(relativePath) || relativePath == ".")
                return root;

            return root.Find(relativePath);
        }

        static GameObject CreatePath(Transform parent, string relativePath, string finalName)
        {
            Transform current = parent;
            string[] parts = relativePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = i == parts.Length - 1 ? finalName : parts[i];
                Transform child = current.Find(part);
                if (child == null)
                {
                    GameObject created = new(part, typeof(RectTransform));
                    Undo.RegisterCreatedObjectUndo(created, "Create UI path");
                    created.transform.SetParent(current, false);
                    current = created.transform;
                }
                else
                {
                    current = child;
                }
            }
            return current.gameObject;
        }

        static object BuildCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            JArray changes = root["changes"] as JArray ?? new JArray();
            JArray issues = root["issues"] as JArray ?? new JArray();
            return new
            {
                parent = root["parent"],
                buttonPath = root["buttonPath"],
                exists = root["exists"],
                willModify = root["willModify"],
                applied = root["applied"],
                changeCount = root["changeCount"],
                changed = changes.Take(16).ToArray(),
                issues
            };
        }

        static object ToColorObject(Color color) => new { r = color.r, g = color.g, b = color.b, a = color.a };
    }
}
