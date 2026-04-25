using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.Adapters.Unity;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Tools.Parameters;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class UiAuthoringTools
    {
        internal static bool TryResolveRoot(JToken target, string searchMethod, bool includeInactive, out GameObject targetRoot, out string error)
        {
            targetRoot = null;
            error = null;
            if (target == null)
            {
                error = "Target is required.";
                return false;
            }

            JObject findParams = new()
            {
                ["search_inactive"] = includeInactive
            };
            targetRoot = ObjectsHelper.FindObject(target, searchMethod, findParams);
            if (targetRoot == null)
            {
                error = "UI root target not found.";
                return false;
            }

            return true;
        }

        internal static bool TryResolveLayoutTarget(string target, string searchMethod, string targetPath, bool includeInactive, out GameObject targetRoot, out Transform targetTransform, out string error)
        {
            targetRoot = null;
            targetTransform = null;
            error = null;
            if (string.IsNullOrWhiteSpace(target))
            {
                error = "Target is required.";
                return false;
            }

            JObject findParams = new()
            {
                ["search_inactive"] = includeInactive
            };
            targetRoot = ObjectsHelper.FindObject(new JValue(target), searchMethod, findParams);
            if (targetRoot == null)
            {
                error = "UI target root not found.";
                return false;
            }

            string normalizedTargetPath = string.IsNullOrWhiteSpace(targetPath) ? "." : targetPath.Trim();
            targetTransform = normalizedTargetPath == "." ? targetRoot.transform : targetRoot.transform.Find(normalizedTargetPath);
            if (targetTransform == null)
            {
                error = $"TargetPath '{normalizedTargetPath}' was not found under '{UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform)}'.";
                return false;
            }

            return true;
        }

        internal static List<UiNamedHierarchyNodeSpec> ParseRootNodeSpecs(JToken nodesToken)
        {
            return EnumerateRootNodeSpecs(nodesToken).ToList();
        }

        internal static bool TryEnsureNamedHierarchy(
            GameObject targetRoot,
            IReadOnlyList<UiNamedHierarchyNodeSpec> nodeSpecs,
            bool previewOnly,
            out List<object> resolvedNodes,
            out bool applied,
            out string error)
        {
            resolvedNodes = new List<object>();
            applied = false;
            error = null;
            if (targetRoot == null)
            {
                error = "Target is required.";
                return false;
            }

            foreach (UiNamedHierarchyNodeSpec node in nodeSpecs ?? Array.Empty<UiNamedHierarchyNodeSpec>())
            {
                if (!EnsureNode(targetRoot.transform, node, previewOnly, resolvedNodes, ref applied, out error))
                    return false;
            }

            return true;
        }

        internal static bool TryApplyLayout(
            GameObject target,
            SetUiLayoutPropertiesParams parameters,
            out List<object> changes,
            out bool wouldModify,
            out string error)
        {
            changes = new List<object>();
            return ApplyLayoutChanges(target, parameters, changes, out wouldModify, out error);
        }

        internal static SetUiLayoutPropertiesParams CreateLayoutParams(UiNodeLayoutSpec layout, bool previewOnly)
        {
            if (layout == null)
                return null;

            return new SetUiLayoutPropertiesParams
            {
                PreviewOnly = previewOnly,
                AnchorMin = layout.AnchorMin,
                AnchorMax = layout.AnchorMax,
                Pivot = layout.Pivot,
                SizeDelta = layout.SizeDelta,
                AnchoredPosition = layout.AnchoredPosition,
                SiblingIndex = layout.SiblingIndex,
                Active = layout.Active,
                CanvasGroupAlpha = layout.CanvasGroupAlpha,
                CanvasGroupInteractable = layout.CanvasGroupInteractable,
                CanvasGroupBlocksRaycasts = layout.CanvasGroupBlocksRaycasts,
                ImageSpritePath = layout.ImageSpritePath,
                ImageColor = layout.ImageColor,
                Text = layout.Text,
                TextColor = layout.TextColor,
                ButtonInteractable = layout.ButtonInteractable
            };
        }

        static bool EnsureNode(
            Transform parent,
            UiNamedHierarchyNodeSpec spec,
            bool previewOnly,
            List<object> resolvedNodes,
            ref bool applied,
            out string error)
        {
            error = null;
            if (spec == null || string.IsNullOrWhiteSpace(spec.Name))
            {
                error = "Each UI node spec must include a Name.";
                return false;
            }

            Transform existing = FindDirectChild(parent, spec.Name);
            string path = UiDiagnosticsHelper.GetHierarchyPath(parent) + "/" + spec.Name;

            if (existing == null)
            {
                applied = true;
                if (previewOnly)
                {
                    AppendSpecRows(UiDiagnosticsHelper.GetHierarchyPath(parent) + "/" + spec.Name, spec, "create", resolvedNodes, existed: false);
                    return true;
                }

                GameObject created = CreateNodeRecursive(parent, spec, out error);
                if (created == null)
                    return false;

                if (!ApplySpecRecursive(created.transform, spec, previewOnly: false, out error))
                    return false;

                AppendSpecRows(UiDiagnosticsHelper.GetHierarchyPath(parent) + "/" + spec.Name, spec, "create", resolvedNodes, existed: false);
                return true;
            }

            if (RequiresRecreate(existing, out string recreateReason))
            {
                applied = true;
                if (previewOnly)
                {
                    AppendSpecRows(UiDiagnosticsHelper.GetHierarchyPath(parent) + "/" + spec.Name, spec, "recreate", resolvedNodes, existed: true, recreateReason);
                    return true;
                }

                Undo.DestroyObjectImmediate(existing.gameObject);
                GameObject recreated = CreateNodeRecursive(parent, spec, out error);
                if (recreated == null)
                    return false;

                if (!ApplySpecRecursive(recreated.transform, spec, previewOnly: false, out error))
                    return false;

                AppendSpecRows(UiDiagnosticsHelper.GetHierarchyPath(parent) + "/" + spec.Name, spec, "recreate", resolvedNodes, existed: true, recreateReason);
                return true;
            }

            if (!TryApplyNodeSpec(existing.gameObject, spec, previewOnly, out var nodeChanges, out var nodeUpdated, out error))
                return false;

            resolvedNodes.Add(new
            {
                path,
                action = nodeUpdated ? "update" : "preserve",
                existed = true,
                instanceId = UnityApiAdapter.GetObjectId(existing.gameObject),
                componentTypes = existing.GetComponents<Component>().Where(component => component != null).Select(component => component.GetType().FullName).ToArray(),
                changes = nodeChanges.ToArray()
            });
            applied |= nodeUpdated;

            foreach (UiNamedHierarchyNodeSpec child in EnumerateChildren(spec))
            {
                if (!EnsureNode(existing, child, previewOnly, resolvedNodes, ref applied, out error))
                    return false;
            }

            return true;
        }

        static void AppendSpecRows(string path, UiNamedHierarchyNodeSpec spec, string action, List<object> rows, bool existed, string reason = null)
        {
            rows.Add(new
            {
                path,
                action,
                existed,
                reason,
                requestedComponents = DescribeRequestedComponentTypes(spec),
                requestedLayout = spec.Layout != null
            });

            foreach (UiNamedHierarchyNodeSpec child in EnumerateChildren(spec))
            {
                AppendSpecRows(path + "/" + child.Name, child, action == "preserve" ? "create" : action, rows, existed: false);
            }
        }

        static bool RequiresRecreate(Transform existing, out string recreateReason)
        {
            recreateReason = null;
            bool shouldHaveRectTransform = existing.parent is RectTransform || existing.GetComponentInParent<Canvas>(true) != null;
            if (shouldHaveRectTransform && existing is not RectTransform)
            {
                recreateReason = "Existing node must use RectTransform in a UI subtree.";
                return true;
            }

            return false;
        }

        static bool ApplySpecRecursive(Transform node, UiNamedHierarchyNodeSpec spec, bool previewOnly, out string error)
        {
            error = null;
            if (node == null || spec == null)
                return true;

            if (!TryApplyNodeSpec(node.gameObject, spec, previewOnly, out _, out _, out error))
                return false;

            foreach (UiNamedHierarchyNodeSpec childSpec in EnumerateChildren(spec))
            {
                Transform child = FindDirectChild(node, childSpec.Name);
                if (!ApplySpecRecursive(child, childSpec, previewOnly, out error))
                    return false;
            }

            return true;
        }

        static bool TryApplyNodeSpec(
            GameObject node,
            UiNamedHierarchyNodeSpec spec,
            bool previewOnly,
            out List<object> changes,
            out bool wouldModify,
            out string error)
        {
            changes = new List<object>();
            wouldModify = false;
            error = null;
            if (node == null || spec == null)
                return true;

            List<Type> desiredComponentTypes = GetDesiredComponentTypes(node, spec, out error);
            if (!string.IsNullOrWhiteSpace(error))
                return false;

            foreach (Type componentType in desiredComponentTypes)
            {
                if (componentType == null || componentType == typeof(Transform) || componentType == typeof(RectTransform))
                    continue;

                if (node.GetComponent(componentType) != null)
                    continue;

                wouldModify = true;
                changes.Add(new
                {
                    property = "component",
                    previousValue = (string)null,
                    newValue = componentType.FullName
                });

                if (!previewOnly)
                {
                    Component added = Undo.AddComponent(node, componentType);
                    InitializeAddedComponent(added);
                    EditorUtility.SetDirty(node);
                }
            }

            SetUiLayoutPropertiesParams layoutParams = CreateLayoutParams(spec.Layout, previewOnly);
            if (layoutParams != null)
            {
                if (!ApplyLayoutChanges(node, layoutParams, changes, out bool layoutWouldModify, out error))
                    return false;

                wouldModify |= layoutWouldModify;
            }

            return true;
        }

        static GameObject CreateNodeRecursive(Transform parent, UiNamedHierarchyNodeSpec spec, out string error)
        {
            error = null;
            List<Type> desiredTypes = GetDesiredComponentTypes(parent?.gameObject, spec, out error);
            if (!string.IsNullOrWhiteSpace(error))
                return null;

            Type[] componentTypes = desiredTypes
                .Where(type => type != null && type != typeof(Transform) && type != typeof(RectTransform))
                .Distinct()
                .ToArray();

            bool useRectTransform = parent is RectTransform || parent.GetComponentInParent<Canvas>(true) != null;
            GameObject created = useRectTransform
                ? new GameObject(spec.Name, typeof(RectTransform))
                : new GameObject(spec.Name);
            Undo.RegisterCreatedObjectUndo(created, $"Create UI node {spec.Name}");
            created.transform.SetParent(parent, false);

            foreach (Type componentType in componentTypes)
            {
                Component added = Undo.AddComponent(created, componentType);
                InitializeAddedComponent(added);
            }

            foreach (UiNamedHierarchyNodeSpec child in EnumerateChildren(spec))
            {
                if (CreateNodeRecursive(created.transform, child, out error) == null)
                    return null;
            }

            EditorUtility.SetDirty(created);
            return created;
        }

        static void InitializeAddedComponent(Component component)
        {
            if (component == null)
                return;

            string typeName = component.GetType().FullName ?? component.GetType().Name;
            if (!typeName.Contains("UnityEngine.UI.Text", StringComparison.Ordinal) &&
                !string.Equals(component.GetType().Name, "Text", StringComparison.Ordinal))
            {
                return;
            }

            PropertyInfo fontProperty = component.GetType().GetProperty("font", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (fontProperty == null || !fontProperty.CanWrite)
                return;

            Font font = LoadDefaultLegacyFont();
            if (font != null)
                fontProperty.SetValue(component, font);
        }

        static Font LoadDefaultLegacyFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
        }

        static List<Type> GetDesiredComponentTypes(GameObject contextNode, UiNamedHierarchyNodeSpec spec, out string error)
        {
            error = null;
            var componentTypes = new List<Type>();
            bool useRectTransform = contextNode == null || contextNode.transform is RectTransform || contextNode.GetComponentInParent<Canvas>(true) != null;
            if (useRectTransform)
                componentTypes.Add(typeof(RectTransform));

            foreach (string componentTypeName in spec.ComponentTypes ?? Array.Empty<string>())
            {
                if (!TryResolveComponentType(componentTypeName, out Type componentType, out error))
                    return componentTypes;

                if (!componentTypes.Contains(componentType))
                    componentTypes.Add(componentType);
            }

            foreach (string componentTypeName in EnumerateRequestedComponentNames(spec))
            {
                if (!TryResolveComponentType(componentTypeName, out Type componentType, out error))
                    return componentTypes;

                if (!componentTypes.Contains(componentType))
                    componentTypes.Add(componentType);
            }

            return componentTypes;
        }

        static IEnumerable<string> EnumerateRequestedComponentNames(UiNamedHierarchyNodeSpec spec)
        {
            UiNodeComponentsSpec components = spec?.Components;
            UiNodeLayoutSpec layout = spec?.Layout;

            if (components?.CanvasGroup == true || layout?.CanvasGroupAlpha != null || layout?.CanvasGroupBlocksRaycasts != null || layout?.CanvasGroupInteractable != null)
                yield return "CanvasGroup";
            if (components?.Image == true || !string.IsNullOrWhiteSpace(layout?.ImageSpritePath) || (layout?.ImageColor != null && layout.ImageColor.Type != JTokenType.Null))
                yield return "Image";
            if (components?.Button == true || layout?.ButtonInteractable != null)
                yield return "Button";

            bool wantsTmpText = components?.TmpText == true;
            bool wantsLegacyText = components?.Text == true;
            bool wantsAnyText = wantsTmpText || wantsLegacyText || layout?.Text != null || (layout?.TextColor != null && layout.TextColor.Type != JTokenType.Null);
            if (wantsAnyText)
                yield return wantsTmpText && !wantsLegacyText ? "TextMeshProUGUI" : "Text";
        }

        static string[] DescribeRequestedComponentTypes(UiNamedHierarchyNodeSpec spec)
        {
            return (spec.ComponentTypes ?? Array.Empty<string>())
                .Concat(EnumerateRequestedComponentNames(spec))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        static bool TryResolveComponentType(string componentTypeName, out Type componentType, out string error)
        {
            error = null;
            componentType = UnityComponentResolver.FindType(componentTypeName);
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                error = $"Component type '{componentTypeName}' could not be resolved.";
                return false;
            }

            return true;
        }

        static Transform FindDirectChild(Transform parent, string childName)
        {
            if (parent == null || string.IsNullOrWhiteSpace(childName))
                return null;

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child != null && string.Equals(child.name, childName, StringComparison.Ordinal))
                    return child;
            }

            return null;
        }

        static IEnumerable<UiNamedHierarchyNodeSpec> EnumerateChildren(UiNamedHierarchyNodeSpec spec)
        {
            if (spec?.Children is not JArray array)
                yield break;

            foreach (JToken childToken in array)
            {
                if (childToken == null || childToken.Type == JTokenType.Null)
                    continue;

                UiNamedHierarchyNodeSpec child = childToken.ToObject<UiNamedHierarchyNodeSpec>();
                if (child != null)
                    yield return child;
            }
        }

        static IEnumerable<UiNamedHierarchyNodeSpec> EnumerateRootNodeSpecs(JToken nodesToken)
        {
            if (nodesToken is not JArray array)
                yield break;

            foreach (JToken nodeToken in array)
            {
                if (nodeToken == null || nodeToken.Type == JTokenType.Null)
                    continue;

                UiNamedHierarchyNodeSpec node = nodeToken.ToObject<UiNamedHierarchyNodeSpec>();
                if (node != null)
                    yield return node;
            }
        }

        internal static bool ApplyLayoutChanges(GameObject target, SetUiLayoutPropertiesParams parameters, List<object> changes, out bool wouldModify, out string error)
        {
            error = null;
            wouldModify = false;
            bool hasChanges = false;
            RectTransform rectTransform = target.transform as RectTransform;
            if (rectTransform == null)
            {
                error = $"Target '{UiDiagnosticsHelper.GetHierarchyPath(target.transform)}' does not have a RectTransform.";
                return false;
            }

            void RecordChange(string property, object previousValue, object newValue)
            {
                if (AreValuesEquivalent(previousValue, newValue))
                    return;

                hasChanges = true;
                changes.Add(new
                {
                    property,
                    previousValue = NormalizeValue(previousValue),
                    newValue = NormalizeValue(newValue)
                });
            }

            if (parameters.AnchorMin != null && parameters.AnchorMin.Type != JTokenType.Null)
            {
                if (!TryParseVector2(parameters.AnchorMin, out Vector2 value))
                {
                    error = "AnchorMin must be {x,y} or [x,y].";
                    return false;
                }

                RecordChange("anchorMin", rectTransform.anchorMin, value);
                if (!parameters.PreviewOnly)
                    rectTransform.anchorMin = value;
            }

            if (parameters.AnchorMax != null && parameters.AnchorMax.Type != JTokenType.Null)
            {
                if (!TryParseVector2(parameters.AnchorMax, out Vector2 value))
                {
                    error = "AnchorMax must be {x,y} or [x,y].";
                    return false;
                }

                RecordChange("anchorMax", rectTransform.anchorMax, value);
                if (!parameters.PreviewOnly)
                    rectTransform.anchorMax = value;
            }

            if (parameters.Pivot != null && parameters.Pivot.Type != JTokenType.Null)
            {
                if (!TryParseVector2(parameters.Pivot, out Vector2 value))
                {
                    error = "Pivot must be {x,y} or [x,y].";
                    return false;
                }

                RecordChange("pivot", rectTransform.pivot, value);
                if (!parameters.PreviewOnly)
                    rectTransform.pivot = value;
            }

            if (parameters.SizeDelta != null && parameters.SizeDelta.Type != JTokenType.Null)
            {
                if (!TryParseVector2(parameters.SizeDelta, out Vector2 value))
                {
                    error = "SizeDelta must be {x,y} or [x,y].";
                    return false;
                }

                RecordChange("sizeDelta", rectTransform.sizeDelta, value);
                if (!parameters.PreviewOnly)
                    rectTransform.sizeDelta = value;
            }

            if (parameters.AnchoredPosition != null && parameters.AnchoredPosition.Type != JTokenType.Null)
            {
                if (!TryParseVector2(parameters.AnchoredPosition, out Vector2 value))
                {
                    error = "AnchoredPosition must be {x,y} or [x,y].";
                    return false;
                }

                RecordChange("anchoredPosition", rectTransform.anchoredPosition, value);
                if (!parameters.PreviewOnly)
                    rectTransform.anchoredPosition = value;
            }

            if (parameters.SiblingIndex.HasValue)
            {
                RecordChange("siblingIndex", rectTransform.GetSiblingIndex(), parameters.SiblingIndex.Value);
                if (!parameters.PreviewOnly)
                    rectTransform.SetSiblingIndex(parameters.SiblingIndex.Value);
            }

            if (parameters.Active.HasValue)
            {
                RecordChange("activeSelf", target.activeSelf, parameters.Active.Value);
                if (!parameters.PreviewOnly)
                    target.SetActive(parameters.Active.Value);
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
                    RecordChange("canvasGroup.alpha", canvasGroup.alpha, parameters.CanvasGroupAlpha.Value);
                    if (!parameters.PreviewOnly)
                        canvasGroup.alpha = parameters.CanvasGroupAlpha.Value;
                }

                if (parameters.CanvasGroupInteractable.HasValue)
                {
                    RecordChange("canvasGroup.interactable", canvasGroup.interactable, parameters.CanvasGroupInteractable.Value);
                    if (!parameters.PreviewOnly)
                        canvasGroup.interactable = parameters.CanvasGroupInteractable.Value;
                }

                if (parameters.CanvasGroupBlocksRaycasts.HasValue)
                {
                    RecordChange("canvasGroup.blocksRaycasts", canvasGroup.blocksRaycasts, parameters.CanvasGroupBlocksRaycasts.Value);
                    if (!parameters.PreviewOnly)
                        canvasGroup.blocksRaycasts = parameters.CanvasGroupBlocksRaycasts.Value;
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

                    RecordChange("image.sprite", image.sprite != null ? image.sprite.name : "null", sprite.name);
                    if (!parameters.PreviewOnly)
                        image.sprite = sprite;
                }

                if (parameters.ImageColor != null && parameters.ImageColor.Type != JTokenType.Null)
                {
                    if (!TryParseColor(parameters.ImageColor, out Color color))
                    {
                        error = "ImageColor must be {r,g,b,a} or [r,g,b,a].";
                        return false;
                    }

                    RecordChange("image.color", image.color, color);
                    if (!parameters.PreviewOnly)
                        image.color = color;
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
                    RecordChange("text", previousText, parameters.Text);
                    if (!parameters.PreviewOnly)
                        SetTextValue(textComponent, parameters.Text);
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

                    RecordChange("text.color", textGraphic.color, color);
                    if (!parameters.PreviewOnly)
                        textGraphic.color = color;
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

                RecordChange("button.interactable", button.interactable, parameters.ButtonInteractable.Value);
                if (!parameters.PreviewOnly)
                    button.interactable = parameters.ButtonInteractable.Value;
            }

            wouldModify = hasChanges;

            if (!parameters.PreviewOnly && wouldModify)
            {
                EditorUtility.SetDirty(target);
                if (canvasGroup != null)
                    EditorUtility.SetDirty(canvasGroup);
                if (image != null)
                    EditorUtility.SetDirty(image);
                if (textComponent != null)
                    EditorUtility.SetDirty(textComponent);
            }

            return true;
        }

        static bool AreValuesEquivalent(object left, object right)
        {
            JToken leftToken = left == null ? JValue.CreateNull() : JToken.FromObject(NormalizeValue(left));
            JToken rightToken = right == null ? JValue.CreateNull() : JToken.FromObject(NormalizeValue(right));
            return JToken.DeepEquals(leftToken, rightToken);
        }

        static object NormalizeValue(object value)
        {
            return value switch
            {
                null => null,
                Vector2 vector2 => new { x = vector2.x, y = vector2.y },
                Vector3 vector3 => new { x = vector3.x, y = vector3.y, z = vector3.z },
                Vector4 vector4 => new { x = vector4.x, y = vector4.y, z = vector4.z, w = vector4.w },
                Color color => new { r = color.r, g = color.g, b = color.b, a = color.a },
                _ => value
            };
        }

        internal static Component FindTextComponent(GameObject target)
        {
            Type textType = UnityComponentResolver.FindType("Text");
            if (textType != null)
            {
                Component legacyText = target.GetComponent(textType);
                if (legacyText != null)
                    return legacyText;
            }

            Type tmpType = UnityComponentResolver.FindType("TMP_Text") ?? UnityComponentResolver.FindType("TextMeshProUGUI");
            return tmpType != null ? target.GetComponent(tmpType) : null;
        }

        static string GetTextValue(Component component)
        {
            PropertyInfo property = component.GetType().GetProperty("text", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property != null && property.CanRead)
                return property.GetValue(component)?.ToString() ?? string.Empty;

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

        internal static bool TryParseVector2(JToken value, out Vector2 vector)
        {
            vector = default;
            if (value == null || value.Type == JTokenType.Null)
                return false;

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

        internal static bool TryParseColor(JToken value, out Color color)
        {
            color = default;
            if (value == null || value.Type == JTokenType.Null)
                return false;

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

        internal static string SanitizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

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
