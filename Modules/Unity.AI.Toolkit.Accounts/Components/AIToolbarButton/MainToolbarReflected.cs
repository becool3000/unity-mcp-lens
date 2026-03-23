using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts
{
    /// <summary>
    /// A reflection-based wrapper for accessing the Unity Editor's main toolbar.
    /// This provides access to the toolbar's visual tree for Unity versions prior to 6000.3.
    /// </summary>
    static class MainToolbarReflected
    {
        // Types
        static readonly Type k_ToolbarType;

        // Fields/Properties for toolbar access
        static readonly FieldInfo k_GetField;
        static readonly PropertyInfo k_WindowBackendProperty;

        const BindingFlags k_BindingFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static MainToolbarReflected()
        {
            // Find the Toolbar type
            k_ToolbarType = Type.GetType("UnityEditor.Toolbar, UnityEditor.CoreModule");
            if (k_ToolbarType == null)
                k_ToolbarType = Type.GetType("UnityEditor.Toolbar, UnityEditor");

            if (k_ToolbarType == null)
            {
                Debug.LogWarning("MainToolbarReflected: Could not find Toolbar type.");
                return;
            }

            // Get the static 'get' field that holds the toolbar instance
            k_GetField = k_ToolbarType.GetField("get", BindingFlags.Static | BindingFlags.Public);

            // Get windowBackend property (may be on base class)
            k_WindowBackendProperty = k_ToolbarType.GetProperty("windowBackend", k_BindingFlags);
            if (k_WindowBackendProperty == null)
            {
                k_WindowBackendProperty = k_ToolbarType.BaseType?.GetProperty("windowBackend", k_BindingFlags);
            }
        }

        /// <summary>
        /// Returns true if reflection succeeded and this class can be used.
        /// </summary>
        public static bool IsAvailable =>
            k_ToolbarType != null &&
            k_GetField != null &&
            k_WindowBackendProperty != null;

        /// <summary>
        /// Returns the Toolbar type.
        /// </summary>
        internal static Type ToolbarType => k_ToolbarType;

        /// <summary>
        /// Returns the static 'get' field info.
        /// </summary>
        internal static FieldInfo GetField => k_GetField;

        /// <summary>
        /// Returns the windowBackend property info.
        /// </summary>
        internal static PropertyInfo WindowBackendProperty => k_WindowBackendProperty;

        /// <summary>
        /// Gets the current toolbar instance.
        /// </summary>
        public static object GetToolbarInstance()
        {
            if (k_GetField == null)
                return null;

            return k_GetField.GetValue(null);
        }

        /// <summary>
        /// Gets the visual tree root from the toolbar.
        /// </summary>
        public static VisualElement GetToolbarVisualTree()
        {
            if (!IsAvailable)
                return null;

            try
            {
                var toolbarInstance = GetToolbarInstance();
                if (toolbarInstance == null)
                    return null;

                var windowBackend = k_WindowBackendProperty.GetValue(toolbarInstance);
                if (windowBackend == null)
                    return null;

                var visualTreeProp = windowBackend.GetType().GetProperty("visualTree",
                    BindingFlags.Instance | BindingFlags.Public);
                if (visualTreeProp == null)
                    return null;

                return visualTreeProp.GetValue(windowBackend) as VisualElement;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"MainToolbarReflected: Exception getting visual tree: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Requests a repaint of the main toolbar.
        /// </summary>
        public static void RepaintToolbar()
        {
            if (k_ToolbarType == null)
                return;

            try
            {
                var repaintMethod = k_ToolbarType.GetMethod("RepaintToolbar", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                repaintMethod?.Invoke(null, null);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"MainToolbarReflected: Exception repainting toolbar: {e.Message}");
            }
        }
    }
}
