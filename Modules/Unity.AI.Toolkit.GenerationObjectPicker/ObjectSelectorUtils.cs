#if OBJECT_SELECTOR_TOOLBAR_DECORATOR
using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit
{
    static class ObjectSelectorUtils
    {
        const string k_SkipHiddenPackagesToggleName = "unity-object-selector__skip-hidden-packages-toggle";

        const string k_AdvancedObjectSelectorFirstRightElement = "ResultViewButtonContainer";

        internal const string classicObjectSelector = "Classic";

        internal const string advancedObjectSelector = "Advanced";

        internal static void SetupShownEventHandler(Action<EditorWindow> shownHandler)
        {
            ObjectSelectorReflected.AddShownEventHandler(shownHandler);
        }

        internal static void RemoveShownEventHandler(Action<EditorWindow> shownHandler)
        {
            ObjectSelectorReflected.RemoveShownEventHandler(shownHandler);
        }

        internal static VisualElement GetTargetElement(EditorWindow window)
        {
            var element = window.rootVisualElement.Q<ToolbarToggle>(k_SkipHiddenPackagesToggleName);
            if (element == null)
            {
                // Classic object selector element not found.
                // Try to find the Advanced Object Selector element.
                return window.rootVisualElement.Q<VisualElement>(k_AdvancedObjectSelectorFirstRightElement);
            }
            return element;
        }

        internal static void SetSelection(int instanceID)
        {
            ObjectSelectorReflected.SetSelection(instanceID);
        }

        internal static Type[] GetAllowedTypes()
        {
            return ObjectSelectorReflected.GetAllowedTypes();
        }

        internal static UnityEngine.Object GetCurrentObject()
        {
            return ObjectSelectorReflected.GetCurrentObject();
        }

        internal static bool IsSkyboxMaterial(Material material)
        {
            if (material == null || material.shader == null)
                return false;

            var shaderName = material.shader.name;
            return shaderName.StartsWith("Skybox/") ||
                   shaderName.StartsWith("HDRP/Sky/");
        }

        internal static bool IsSkyboxContext(EditorWindow objectSelectorWindow)
        {
            // First check: Is there a skybox material currently selected?
            var currentObject = GetCurrentObject();
            if (currentObject is Material material && IsSkyboxMaterial(material))
                return true;

            // Second check: Look at the property path being edited
            // This detects skybox fields even when empty (e.g., Lighting window's skybox material slot)
            var editedProperty = ObjectSelectorReflected.GetEditedProperty(objectSelectorWindow);
            if (editedProperty != null)
            {
                var propertyPath = editedProperty.propertyPath;
                if (!string.IsNullOrEmpty(propertyPath) &&
                    propertyPath.Contains("skybox", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Third check: Look at the search filter for skybox-related hints
            var searchFilter = ObjectSelectorReflected.GetSearchFilter(objectSelectorWindow);
            if (!string.IsNullOrEmpty(searchFilter))
            {
                if (searchFilter.Contains("Skybox", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
#endif // OBJECT_SELECTOR_TOOLBAR_DECORATOR
