using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry;
using Unity.AI.MCP.Editor.Tools.Parameters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.AI.MCP.Editor.Tools
{
    public static class UiAuthoringTools
    {
        public const string EnsureNamedHierarchyDescription = @"Ensures a named UI hierarchy exists under a scene object or canvas root.

Args:
    Target: Scene GameObject, path, or instance id to use as the root parent.
    SearchMethod: How to find the target root ('by_name', 'by_id', 'by_path').
    Nodes: Named UI node specs to ensure under the root target.
      Name: UI GameObject name.
      ComponentTypes: Component type names that must exist on the node.
      Children: Child node specs that must exist under the node.
    PreviewOnly: When true, reports the create or recreate operations without saving the scene.

Returns:
    Dictionary with success/message/data. Data contains resolved paths and create or recreate actions.";

        public const string SetLayoutPropertiesDescription = @"Sets authored UI layout and display properties on a scene object.

Args:
    Target: Scene GameObject, path, or instance id to edit.
    SearchMethod: How to find the target ('by_name', 'by_id', 'by_path').
    TargetPath: Optional relative child path under the target root. Use '.' or omit for the root GameObject.
    PreviewOnly: When true, validates and reports the layout changes without saving the scene.
    RectTransform fields: AnchorMin, AnchorMax, Pivot, SizeDelta, AnchoredPosition, SiblingIndex.
    GameObject field: Active.
    CanvasGroup fields: CanvasGroupAlpha, CanvasGroupInteractable, CanvasGroupBlocksRaycasts.
    Image fields: ImageSpritePath, ImageColor.
    Text fields: Text, TextColor.
    Button field: ButtonInteractable.

Returns:
    Dictionary with success/message/data. Data contains the applied or previewed property changes.";

        [McpTool("Unity.UI.EnsureNamedHierarchy", EnsureNamedHierarchyDescription, Groups = new[] { "ui", "editor" }, EnabledByDefault = true)]
        public static object EnsureNamedHierarchy(EnsureNamedHierarchyParams parameters)
        {
            parameters ??= new EnsureNamedHierarchyParams();
            if (parameters.Target == null)
            {
                return Response.Error("Target is required.");
            }

            if (parameters.Nodes == null || parameters.Nodes.Length == 0)
            {
                return Response.Error("At least one node spec is required.");
            }

            JObject findParams = new()
            {
                ["search_inactive"] = parameters.IncludeInactive
            };
            GameObject targetRoot = ObjectsHelper.FindObject(parameters.Target, parameters.SearchMethod, findParams);
            if (targetRoot == null)
            {
                return Response.Error("UI root target not found.");
            }

            var resolvedNodes = new List<object>();
            foreach (UiNamedHierarchyNodeSpec node in parameters.Nodes)
            {
                EnsureNode(targetRoot.transform, node, parameters.PreviewOnly, resolvedNodes, out string error);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    return Response.Error(error, new
                    {
                        target = UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform),
                        nodes = resolvedNodes
                    });
                }
            }

            if (!parameters.PreviewOnly)
            {
                EditorSceneManager.MarkSceneDirty(targetRoot.scene);
                EditorSceneManager.SaveOpenScenes();
            }

            return Response.Success(parameters.PreviewOnly
                ? $"Validated named UI hierarchy under '{UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform)}'."
                : $"Ensured named UI hierarchy under '{UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform)}'.", new
            {
                target = UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform),
                previewOnly = parameters.PreviewOnly,
                nodes = resolvedNodes
            });
        }

        [McpTool("Unity.UI.SetLayoutProperties", SetLayoutPropertiesDescription, Groups = new[] { "ui", "editor" }, EnabledByDefault = true)]
        public static object SetLayoutProperties(SetUiLayoutPropertiesParams parameters)
        {
            parameters ??= new SetUiLayoutPropertiesParams();
            if (string.IsNullOrWhiteSpace(parameters.Target))
            {
                return Response.Error("Target is required.");
            }

            JObject findParams = new()
            {
                ["search_inactive"] = parameters.IncludeInactive
            };
            GameObject targetRoot = ObjectsHelper.FindObject(new JValue(parameters.Target), parameters.SearchMethod, findParams);
            if (targetRoot == null)
            {
                return Response.Error("UI target root not found.");
            }

            string targetPath = string.IsNullOrWhiteSpace(parameters.TargetPath) ? "." : parameters.TargetPath.Trim();
            Transform targetTransform = targetPath == "." ? targetRoot.transform : targetRoot.transform.Find(targetPath);
            if (targetTransform == null)
            {
                return Response.Error($"TargetPath '{targetPath}' was not found under '{UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform)}'.");
            }

            var changes = new List<object>();
            if (!ApplyLayoutChanges(targetTransform.gameObject, parameters, changes, out string error))
            {
                return Response.Error(error, new
                {
                    target = UiDiagnosticsHelper.GetHierarchyPath(targetTransform),
                    changes
                });
            }

            if (!parameters.PreviewOnly)
            {
                EditorSceneManager.MarkSceneDirty(targetTransform.gameObject.scene);
                EditorSceneManager.SaveOpenScenes();
            }

            return Response.Success(parameters.PreviewOnly
                ? $"Validated UI layout changes on '{UiDiagnosticsHelper.GetHierarchyPath(targetTransform)}'."
                : $"Saved UI layout changes on '{UiDiagnosticsHelper.GetHierarchyPath(targetTransform)}'.", new
            {
                target = UiDiagnosticsHelper.GetHierarchyPath(targetTransform),
                previewOnly = parameters.PreviewOnly,
                changes
            });
        }

        static void EnsureNode(Transform parent, UiNamedHierarchyNodeSpec spec, bool previewOnly, List<object> resolvedNodes, out string error)
        {
            error = null;
            if (spec == null || string.IsNullOrWhiteSpace(spec.Name))
            {
                error = "Each UI node spec must include a Name.";
                return;
            }

            Transform existing = FindDirectChild(parent, spec.Name);
            string completenessError = null;
            bool isComplete = existing != null && IsNodeComplete(existing, spec, out completenessError);
            if (!string.IsNullOrWhiteSpace(completenessError))
            {
                error = completenessError;
                return;
            }

            if (!isComplete)
            {
                string action = existing == null ? "create" : "recreate";
                string path = UiDiagnosticsHelper.GetHierarchyPath(parent) + "/" + spec.Name;
                if (previewOnly)
                {
                    resolvedNodes.Add(new
                    {
                        path,
                        action,
                        existed = existing != null,
                        componentTypes = spec.ComponentTypes ?? Array.Empty<string>()
                    });
                    return;
                }

                if (existing != null)
                {
                    Undo.DestroyObjectImmediate(existing.gameObject);
                }

                GameObject created = CreateNodeRecursive(parent, spec, out error);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    return;
                }

                resolvedNodes.Add(new
                {
                    path = UiDiagnosticsHelper.GetHierarchyPath(created.transform),
                    action,
                    existed = existing != null,
                    instanceId = created.GetInstanceID(),
                    componentTypes = created.GetComponents<Component>().Where(component => component != null).Select(component => component.GetType().FullName).ToArray()
                });
                return;
            }

            resolvedNodes.Add(new
            {
                path = UiDiagnosticsHelper.GetHierarchyPath(existing),
                action = "preserve",
                existed = true,
                instanceId = existing.gameObject.GetInstanceID(),
                componentTypes = existing.GetComponents<Component>().Where(component => component != null).Select(component => component.GetType().FullName).ToArray()
            });

            foreach (UiNamedHierarchyNodeSpec child in EnumerateChildren(spec))
            {
                EnsureNode(existing, child, previewOnly, resolvedNodes, out error);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    return;
                }
            }
        }

        static bool IsNodeComplete(Transform node, UiNamedHierarchyNodeSpec spec, out string error)
        {
            error = null;
            if (node == null)
            {
                return false;
            }

            bool shouldHaveRectTransform = node.parent is RectTransform || node.GetComponentInParent<Canvas>(true) != null;
            if (shouldHaveRectTransform && node is not RectTransform)
            {
                return false;
            }

            foreach (string componentTypeName in spec.ComponentTypes ?? Array.Empty<string>())
            {
                Type componentType = ManageGameObject.FindType(componentTypeName);
                if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
                {
                    error = $"Component type '{componentTypeName}' could not be resolved.";
                    return false;
                }

                if (componentType == typeof(Transform) || componentType == typeof(RectTransform))
                {
                    continue;
                }

                if (node.GetComponent(componentType) == null)
                {
                    return false;
                }
            }

            foreach (UiNamedHierarchyNodeSpec childSpec in EnumerateChildren(spec))
            {
                Transform child = FindDirectChild(node, childSpec.Name);
                if (!IsNodeComplete(child, childSpec, out error))
                {
                    return false;
                }
            }

            return true;
        }

        static GameObject CreateNodeRecursive(Transform parent, UiNamedHierarchyNodeSpec spec, out string error)
        {
            error = null;
            bool useRectTransform = parent is RectTransform || parent.GetComponentInParent<Canvas>(true) != null;
            var componentTypes = new List<Type>();
            if (useRectTransform)
            {
                componentTypes.Add(typeof(RectTransform));
            }

            foreach (string componentTypeName in spec.ComponentTypes ?? Array.Empty<string>())
            {
                Type componentType = ManageGameObject.FindType(componentTypeName);
                if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
                {
                    error = $"Component type '{componentTypeName}' could not be resolved.";
                    return null;
                }

                if (componentType == typeof(Transform) || componentType == typeof(RectTransform))
                {
                    continue;
                }

                if (!componentTypes.Contains(componentType))
                {
                    componentTypes.Add(componentType);
                }
            }

            GameObject created = componentTypes.Count > 0
                ? new GameObject(spec.Name, componentTypes.ToArray())
                : new GameObject(spec.Name);
            Undo.RegisterCreatedObjectUndo(created, $"Create UI node {spec.Name}");
            created.transform.SetParent(parent, false);

            foreach (UiNamedHierarchyNodeSpec child in EnumerateChildren(spec))
            {
                if (CreateNodeRecursive(created.transform, child, out error) == null)
                {
                    return null;
                }
            }

            EditorUtility.SetDirty(created);
            return created;
        }

        static Transform FindDirectChild(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child != null && string.Equals(child.name, childName, StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return null;
        }

        static IEnumerable<UiNamedHierarchyNodeSpec> EnumerateChildren(UiNamedHierarchyNodeSpec spec)
        {
            if (spec?.Children is not JArray array)
            {
                yield break;
            }

            foreach (JToken childToken in array)
            {
                if (childToken == null || childToken.Type == JTokenType.Null)
                {
                    continue;
                }

                UiNamedHierarchyNodeSpec child = childToken.ToObject<UiNamedHierarchyNodeSpec>();
                if (child != null)
                {
                    yield return child;
                }
            }
        }

        static bool ApplyLayoutChanges(GameObject target, SetUiLayoutPropertiesParams parameters, List<object> changes, out string error)
        {
            error = null;
            RectTransform rectTransform = target.transform as RectTransform;
            if (rectTransform == null)
            {
                error = $"Target '{UiDiagnosticsHelper.GetHierarchyPath(target.transform)}' does not have a RectTransform.";
                return false;
            }

            if (parameters.AnchorMin != null && parameters.AnchorMin.Type != JTokenType.Null)
            {
                if (!TryParseVector2(parameters.AnchorMin, out Vector2 value))
                {
                    error = "AnchorMin must be {x,y} or [x,y].";
                    return false;
                }

                changes.Add(BuildChange("anchorMin", rectTransform.anchorMin, value));
                if (!parameters.PreviewOnly)
                {
                    rectTransform.anchorMin = value;
                }
            }

            if (parameters.AnchorMax != null && parameters.AnchorMax.Type != JTokenType.Null)
            {
                if (!TryParseVector2(parameters.AnchorMax, out Vector2 value))
                {
                    error = "AnchorMax must be {x,y} or [x,y].";
                    return false;
                }

                changes.Add(BuildChange("anchorMax", rectTransform.anchorMax, value));
                if (!parameters.PreviewOnly)
                {
                    rectTransform.anchorMax = value;
                }
            }

            if (parameters.Pivot != null && parameters.Pivot.Type != JTokenType.Null)
            {
                if (!TryParseVector2(parameters.Pivot, out Vector2 value))
                {
                    error = "Pivot must be {x,y} or [x,y].";
                    return false;
                }

                changes.Add(BuildChange("pivot", rectTransform.pivot, value));
                if (!parameters.PreviewOnly)
                {
                    rectTransform.pivot = value;
                }
            }

            if (parameters.SizeDelta != null && parameters.SizeDelta.Type != JTokenType.Null)
            {
                if (!TryParseVector2(parameters.SizeDelta, out Vector2 value))
                {
                    error = "SizeDelta must be {x,y} or [x,y].";
                    return false;
                }

                changes.Add(BuildChange("sizeDelta", rectTransform.sizeDelta, value));
                if (!parameters.PreviewOnly)
                {
                    rectTransform.sizeDelta = value;
                }
            }

            if (parameters.AnchoredPosition != null && parameters.AnchoredPosition.Type != JTokenType.Null)
            {
                if (!TryParseVector2(parameters.AnchoredPosition, out Vector2 value))
                {
                    error = "AnchoredPosition must be {x,y} or [x,y].";
                    return false;
                }

                changes.Add(BuildChange("anchoredPosition", rectTransform.anchoredPosition, value));
                if (!parameters.PreviewOnly)
                {
                    rectTransform.anchoredPosition = value;
                }
            }

            if (parameters.SiblingIndex.HasValue)
            {
                changes.Add(BuildChange("siblingIndex", rectTransform.GetSiblingIndex(), parameters.SiblingIndex.Value));
                if (!parameters.PreviewOnly)
                {
                    rectTransform.SetSiblingIndex(parameters.SiblingIndex.Value);
                }
            }

            if (parameters.Active.HasValue)
            {
                changes.Add(BuildChange("activeSelf", target.activeSelf, parameters.Active.Value));
                if (!parameters.PreviewOnly)
                {
                    target.SetActive(parameters.Active.Value);
                }
            }

            CanvasGroup canvasGroup = target.GetComponent<CanvasGroup>();
            if (parameters.CanvasGroupAlpha.HasValue || parameters.CanvasGroupInteractable.HasValue || parameters.CanvasGroupBlocksRaycasts.HasValue)
            {
                if (canvasGroup == null)
                {
                    error = $"Target '{UiDiagnosticsHelper.GetHierarchyPath(target.transform)}' does not have a CanvasGroup.";
                    return false;
                }

                if (parameters.CanvasGroupAlpha.HasValue)
                {
                    changes.Add(BuildChange("canvasGroup.alpha", canvasGroup.alpha, parameters.CanvasGroupAlpha.Value));
                    if (!parameters.PreviewOnly)
                    {
                        canvasGroup.alpha = parameters.CanvasGroupAlpha.Value;
                    }
                }

                if (parameters.CanvasGroupInteractable.HasValue)
                {
                    changes.Add(BuildChange("canvasGroup.interactable", canvasGroup.interactable, parameters.CanvasGroupInteractable.Value));
                    if (!parameters.PreviewOnly)
                    {
                        canvasGroup.interactable = parameters.CanvasGroupInteractable.Value;
                    }
                }

                if (parameters.CanvasGroupBlocksRaycasts.HasValue)
                {
                    changes.Add(BuildChange("canvasGroup.blocksRaycasts", canvasGroup.blocksRaycasts, parameters.CanvasGroupBlocksRaycasts.Value));
                    if (!parameters.PreviewOnly)
                    {
                        canvasGroup.blocksRaycasts = parameters.CanvasGroupBlocksRaycasts.Value;
                    }
                }
            }

            Image image = target.GetComponent<Image>();
            if (!string.IsNullOrWhiteSpace(parameters.ImageSpritePath) || (parameters.ImageColor != null && parameters.ImageColor.Type != JTokenType.Null))
            {
                if (image == null)
                {
                    error = $"Target '{UiDiagnosticsHelper.GetHierarchyPath(target.transform)}' does not have an Image.";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(parameters.ImageSpritePath))
                {
                    string spritePath = SanitizeAssetPath(parameters.ImageSpritePath);
                    Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
                    if (sprite == null)
                    {
                        error = $"Image sprite '{spritePath}' could not be loaded.";
                        return false;
                    }

                    changes.Add(BuildChange("image.sprite", image.sprite != null ? image.sprite.name : "null", sprite.name));
                    if (!parameters.PreviewOnly)
                    {
                        image.sprite = sprite;
                    }
                }

                if (parameters.ImageColor != null && parameters.ImageColor.Type != JTokenType.Null)
                {
                    if (!TryParseColor(parameters.ImageColor, out Color color))
                    {
                        error = "ImageColor must be {r,g,b,a} or [r,g,b,a].";
                        return false;
                    }

                    changes.Add(BuildChange("image.color", image.color, color));
                    if (!parameters.PreviewOnly)
                    {
                        image.color = color;
                    }
                }
            }

            Component textComponent = FindTextComponent(target);
            Graphic textGraphic = textComponent as Graphic;
            if (parameters.Text != null || (parameters.TextColor != null && parameters.TextColor.Type != JTokenType.Null))
            {
                if (textComponent == null)
                {
                    error = $"Target '{UiDiagnosticsHelper.GetHierarchyPath(target.transform)}' does not have a Text or TMP_Text component.";
                    return false;
                }

                if (parameters.Text != null)
                {
                    string previousText = GetTextValue(textComponent);
                    changes.Add(BuildChange("text", previousText, parameters.Text));
                    if (!parameters.PreviewOnly)
                    {
                        SetTextValue(textComponent, parameters.Text);
                    }
                }

                if (parameters.TextColor != null && parameters.TextColor.Type != JTokenType.Null)
                {
                    if (textGraphic == null)
                    {
                        error = $"Text component on '{UiDiagnosticsHelper.GetHierarchyPath(target.transform)}' does not expose a Graphic color.";
                        return false;
                    }

                    if (!TryParseColor(parameters.TextColor, out Color color))
                    {
                        error = "TextColor must be {r,g,b,a} or [r,g,b,a].";
                        return false;
                    }

                    changes.Add(BuildChange("text.color", textGraphic.color, color));
                    if (!parameters.PreviewOnly)
                    {
                        textGraphic.color = color;
                    }
                }
            }

            if (parameters.ButtonInteractable.HasValue)
            {
                Button button = target.GetComponent<Button>();
                if (button == null)
                {
                    error = $"Target '{UiDiagnosticsHelper.GetHierarchyPath(target.transform)}' does not have a Button.";
                    return false;
                }

                changes.Add(BuildChange("button.interactable", button.interactable, parameters.ButtonInteractable.Value));
                if (!parameters.PreviewOnly)
                {
                    button.interactable = parameters.ButtonInteractable.Value;
                }
            }

            if (!parameters.PreviewOnly)
            {
                EditorUtility.SetDirty(target);
                if (canvasGroup != null)
                {
                    EditorUtility.SetDirty(canvasGroup);
                }

                if (image != null)
                {
                    EditorUtility.SetDirty(image);
                }

                if (textComponent != null)
                {
                    EditorUtility.SetDirty(textComponent);
                }
            }

            return true;
        }

        static object BuildChange(string property, object previousValue, object newValue)
        {
            return new
            {
                property,
                previousValue,
                newValue
            };
        }

        static Component FindTextComponent(GameObject target)
        {
            var textType = ManageGameObject.FindType("Text");
            if (textType != null)
            {
                Component legacyText = target.GetComponent(textType);
                if (legacyText != null)
                {
                    return legacyText;
                }
            }

            var tmpType = ManageGameObject.FindType("TMP_Text") ?? ManageGameObject.FindType("TextMeshProUGUI");
            if (tmpType != null)
            {
                return target.GetComponent(tmpType);
            }

            return null;
        }

        static string GetTextValue(Component component)
        {
            PropertyInfo property = component.GetType().GetProperty("text", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property != null && property.CanRead)
            {
                return property.GetValue(component)?.ToString() ?? string.Empty;
            }

            FieldInfo field = component.GetType().GetField("text", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return field?.GetValue(component)?.ToString() ?? string.Empty;
        }

        static void SetTextValue(Component component, string value)
        {
            PropertyInfo property = component.GetType().GetProperty("text", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property != null && property.CanWrite)
            {
                property.SetValue(component, value);
                return;
            }

            FieldInfo field = component.GetType().GetField("text", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            field?.SetValue(component, value);
        }

        static bool TryParseVector2(JToken value, out Vector2 vector)
        {
            vector = default;
            if (value == null || value.Type == JTokenType.Null)
            {
                return false;
            }

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

        static bool TryParseColor(JToken value, out Color color)
        {
            color = default;
            if (value == null || value.Type == JTokenType.Null)
            {
                return false;
            }

            if (value is JArray array && array.Count >= 3)
            {
                float a = array.Count > 3 ? array[3].Value<float>() : 1f;
                color = new Color(array[0].Value<float>(), array[1].Value<float>(), array[2].Value<float>(), a);
                return true;
            }

            if (value is JObject obj &&
                obj.TryGetValue("r", StringComparison.OrdinalIgnoreCase, out JToken r) &&
                obj.TryGetValue("g", StringComparison.OrdinalIgnoreCase, out JToken g) &&
                obj.TryGetValue("b", StringComparison.OrdinalIgnoreCase, out JToken b))
            {
                float a = obj.TryGetValue("a", StringComparison.OrdinalIgnoreCase, out JToken alpha) ? alpha.Value<float>() : 1f;
                color = new Color(r.Value<float>(), g.Value<float>(), b.Value<float>(), a);
                return true;
            }

            return false;
        }

        static string SanitizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            string normalized = path.Replace('\\', '/');
            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            return "Assets/" + normalized.TrimStart('/');
        }
    }
}
