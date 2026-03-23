using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.UI.Utilities
{
    static class VisualElementExtensions
    {
        public static bool IsSelected(this VisualElement element) => element.ClassListContains("is-selected");

        public static void SetSelected(this VisualElement element, bool selected = true) => element.EnableInClassList("is-selected", selected);

        public static void ToggleSelected(this VisualElement element) => element.ToggleInClassList("is-selected");
        public static void SetShown(this VisualElement element, bool show = true) => element.EnableInClassList("hide", !show);

        public static void SafeCloneTree(this VisualTreeAsset tree, VisualElement element)
        {
            try { tree.CloneTree(element); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public static void Click(this VisualElement button)
        {
            using (var mouseDownEvent = MakeMouseEvent(EventType.MouseDown, button.layout.center))
            {
                mouseDownEvent.target = button;
                button.parent.SendEvent(mouseDownEvent);
            }

            using (var mouseUpEvent = MakeMouseEvent(EventType.MouseUp, button.layout.center))
            {
                mouseUpEvent.target = button;
                button.parent.SendEvent(mouseUpEvent);
            }
        }

        static EventBase MakeMouseEvent(EventType type, Vector2 position, MouseButton button = MouseButton.LeftMouse, EventModifiers modifiers = EventModifiers.None, int clickCount = 1)
        {
            var evt = new Event
            {
                type = type,
                mousePosition = position,
                button = (int)button,
                modifiers = modifiers,
                clickCount = clickCount
            };

            return type switch
            {
                EventType.MouseUp => PointerUpEvent.GetPooled(evt),
                EventType.MouseDown => PointerDownEvent.GetPooled(evt),
                _ => null
            };
        }

        public static bool IsElementShown(this VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
            {
                if (current.resolvedStyle.display == DisplayStyle.None)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Returns an identifier for this VisualElement that is based on its hierarchical path.
        /// The identifier is computed as a SHA‑1 hash of the element path and then truncated to
        /// create a GUID.
        /// </summary>
        public static Guid GetElementIdentifier(this VisualElement element)
        {
            // Build the element path using a stack so that the order is from root to leaf.
            var names = new Stack<string>();
            for (var current = element; current != null; current = current.parent)
            {
                // Use the element's name if provided; otherwise, use its type name.
                var name = !string.IsNullOrEmpty(current.name)
                    ? current.name
                    : current.GetType().Name;
                names.Push(name);
            }
            var path = string.Join("/", names);

            // Compute the SHA1 hash for the path.
            using var sha1 = SHA1.Create();
            var pathBytes = Encoding.UTF8.GetBytes(path);
            var hashBytes = sha1.ComputeHash(pathBytes);

            // Create a GUID from the first 16 bytes of the hash.
            // SHA1 produces 20 bytes, so we "truncate" to 16 bytes.
            var guidBytes = new byte[16];
            Array.Copy(hashBytes, guidBytes, 16);
            return new Guid(guidBytes);
        }

        public static void RegisterTabEvent(this TextField textField)
        {
            textField.RegisterCallback<KeyDownEvent>(HandleTab, TrickleDown.TrickleDown);
        }

        static void HandleTab(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Tab) return;

            var root = ((VisualElement)evt.target).panel.visualTree;
            var elements = root.Query<VisualElement>().Where(x => x.focusable
                && x.resolvedStyle.visibility == Visibility.Visible
                && x.worldBound is { width: > 0, height: > 0 }).ToList();
            var currentElement = (VisualElement)evt.target;
            var currentIndex = elements.IndexOf(currentElement);
            if (currentIndex == -1) return;

            int nextIndex;
            if (evt.shiftKey)
                nextIndex = (currentIndex - 1) % elements.Count;
            else
            {
                evt.StopImmediatePropagation();
                nextIndex = (currentIndex + 1) % elements.Count;
            }

            elements[nextIndex].Focus();
        }

        public static void AddStyleSheetBasedOnEditorSkin(this VisualElement element)
        {
            var styleSheetPath = "Packages/com.unity.ai.assistant/Modules/Unity.AI.Generators.UI/StyleSheets/ColorsUtilitiesLight.uss";
            if (EditorGUIUtility.isProSkin)
                styleSheetPath = "Packages/com.unity.ai.assistant/Modules/Unity.AI.Generators.UI/StyleSheets/ColorsUtilitiesDark.uss";

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(styleSheetPath);
            if (!element.styleSheets.Contains(styleSheet))
            {
                element.styleSheets.Add(styleSheet);
            }
        }

        public static void SetupInfoIcon(this VisualElement visualElement) => visualElement.Q<VisualElement>(className: "info-icon").style.backgroundImage =
            new StyleBackground(EditorGUIUtility.IconContent($"Icons/PackageManager/{(EditorGUIUtility.isProSkin ? "Dark" : "Light")}/Info.png").image as Texture2D);
    }
}
