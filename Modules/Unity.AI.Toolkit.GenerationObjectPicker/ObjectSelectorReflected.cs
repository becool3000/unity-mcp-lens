#if OBJECT_SELECTOR_TOOLBAR_DECORATOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Toolkit
{
    /// <summary>
    /// A reflection-based wrapper for Unity's internal ObjectSelector class.
    /// This class provides safe, low-level access to ObjectSelector's internal state and methods.
    /// </summary>
    internal static class ObjectSelectorReflected
    {
        static readonly Type k_ObjectSelectorType;
        static readonly PropertyInfo k_GetProperty;
        static readonly EventInfo k_ShownEvent;
        static readonly PropertyInfo k_AllowedTypesProperty;
        static readonly MethodInfo k_SetSelectionMethod;
        static readonly MethodInfo k_GetCurrentObjectMethod;
        static readonly FieldInfo k_SearchFilterField;
        static readonly FieldInfo k_EditedPropertyField;
        static readonly FieldInfo k_ObjectBeingEditedField;

        const BindingFlags k_BindingFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static ObjectSelectorReflected()
        {
            k_ObjectSelectorType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.ObjectSelector");

            if (k_ObjectSelectorType == null)
            {
                Debug.LogError("ObjectSelectorReflected: Could not find internal class UnityEditor.ObjectSelector. This may be due to a Unity version change.");
                return;
            }

            // Reflection for the 'get' property (static accessor to singleton instance)
            k_GetProperty = k_ObjectSelectorType.GetProperty("get", BindingFlags.Static | BindingFlags.Public);

            // Reflection for the 'shown' event
            k_ShownEvent = k_ObjectSelectorType.GetEvent("shown");

            // Reflection for the 'allowedTypes' property
            k_AllowedTypesProperty = k_ObjectSelectorType.GetProperty("allowedTypes");

            // Reflection for the 'SetSelection' method
            k_SetSelectionMethod = k_ObjectSelectorType.GetMethod("SetSelection");

            // Reflection for the 'GetCurrentObject' method
            k_GetCurrentObjectMethod = k_ObjectSelectorType.GetMethod("GetCurrentObject", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            // Reflection for the 'm_SearchFilter' field
            k_SearchFilterField = k_ObjectSelectorType.GetField("m_SearchFilter", k_BindingFlags);

            // Reflection for the 'm_EditedProperty' field (SerializedProperty being edited)
            k_EditedPropertyField = k_ObjectSelectorType.GetField("m_EditedProperty", k_BindingFlags);

            // Reflection for the 'm_ObjectBeingEdited' field (object containing the field)
            k_ObjectBeingEditedField = k_ObjectSelectorType.GetField("m_ObjectBeingEdited", k_BindingFlags);

            if (k_ShownEvent == null)
                Debug.LogError("ObjectSelectorReflected: Could not find 'shown' event on ObjectSelector. This may be due to a Unity version change.");

            if (k_AllowedTypesProperty == null)
                Debug.LogError("ObjectSelectorReflected: Could not find 'allowedTypes' property on ObjectSelector. This may be due to a Unity version change.");

            if (k_SetSelectionMethod == null)
                Debug.LogError("ObjectSelectorReflected: Could not find 'SetSelection' method on ObjectSelector. This may be due to a Unity version change.");

            if (k_GetCurrentObjectMethod == null)
                Debug.LogError("ObjectSelectorReflected: Could not find 'GetCurrentObject' method on ObjectSelector. This may be due to a Unity version change.");

            if (k_SearchFilterField == null)
                Debug.LogWarning("ObjectSelectorReflected: Could not find 'm_SearchFilter' field on ObjectSelector. Search filter detection will not work. This may be due to a Unity version change.");

            if (k_EditedPropertyField == null)
                Debug.LogWarning("ObjectSelectorReflected: Could not find 'm_EditedProperty' field on ObjectSelector. Property path detection will not work. This may be due to a Unity version change.");
        }

        /// <summary>
        /// Gets the singleton ObjectSelector instance.
        /// </summary>
        /// <returns>The ObjectSelector instance, or null if reflection failed.</returns>
        public static object GetInstance()
        {
            try
            {
                if (k_GetProperty == null)
                    return null;

                return k_GetProperty.GetValue(null);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the currently selected object in the ObjectSelector.
        /// </summary>
        /// <returns>The current object, or null if not found or reflection failed.</returns>
        public static UnityEngine.Object GetCurrentObject()
        {
            try
            {
                if (k_GetCurrentObjectMethod == null)
                    return null;

                return (UnityEngine.Object)k_GetCurrentObjectMethod.Invoke(null, null);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the allowed types filter from the ObjectSelector.
        /// </summary>
        /// <returns>Array of allowed types, or null if reflection failed.</returns>
        public static Type[] GetAllowedTypes()
        {
            try
            {
                if (k_AllowedTypesProperty == null)
                    return null;

                return (Type[])k_AllowedTypesProperty.GetValue(null);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the search filter string from the ObjectSelector.
        /// </summary>
        /// <param name="objectSelectorWindow">The ObjectSelector EditorWindow instance.</param>
        /// <returns>The search filter string, or null if not found or reflection failed.</returns>
        public static string GetSearchFilter(EditorWindow objectSelectorWindow)
        {
            try
            {
                if (k_SearchFilterField == null)
                    return null;

                if (objectSelectorWindow == null)
                    return null;

                return k_SearchFilterField.GetValue(objectSelectorWindow) as string;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the search filter string from the ObjectSelector using the singleton instance.
        /// Use GetSearchFilter(EditorWindow) instead when you have the window instance.
        /// </summary>
        /// <returns>The search filter string, or null if not found or reflection failed.</returns>
        public static string GetSearchFilter()
        {
            try
            {
                if (k_SearchFilterField == null)
                    return null;

                var selector = GetInstance();
                if (selector == null)
                    return null;

                return k_SearchFilterField.GetValue(selector) as string;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the SerializedProperty being edited (the property that opened the ObjectSelector).
        /// </summary>
        /// <param name="objectSelectorWindow">The ObjectSelector EditorWindow instance.</param>
        /// <returns>The SerializedProperty, or null if not found or reflection failed.</returns>
        public static SerializedProperty GetEditedProperty(EditorWindow objectSelectorWindow)
        {
            try
            {
                if (k_EditedPropertyField == null)
                    return null;

                if (objectSelectorWindow == null)
                    return null;

                return k_EditedPropertyField.GetValue(objectSelectorWindow) as SerializedProperty;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the SerializedProperty being edited using the singleton instance.
        /// Use GetEditedProperty(EditorWindow) instead when you have the window instance.
        /// </summary>
        /// <returns>The SerializedProperty, or null if not found or reflection failed.</returns>
        public static SerializedProperty GetEditedProperty()
        {
            try
            {
                if (k_EditedPropertyField == null)
                    return null;

                var selector = GetInstance();
                if (selector == null)
                    return null;

                return k_EditedPropertyField.GetValue(selector) as SerializedProperty;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the object being edited (the object that contains the field that opened the ObjectSelector).
        /// </summary>
        /// <param name="objectSelectorWindow">The ObjectSelector EditorWindow instance.</param>
        /// <returns>The object being edited, or null if not found or reflection failed.</returns>
        public static UnityEngine.Object GetObjectBeingEdited(EditorWindow objectSelectorWindow)
        {
            try
            {
                if (k_ObjectBeingEditedField == null)
                    return null;

                if (objectSelectorWindow == null)
                    return null;

                return k_ObjectBeingEditedField.GetValue(objectSelectorWindow) as UnityEngine.Object;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the object being edited using the singleton instance.
        /// Use GetObjectBeingEdited(EditorWindow) instead when you have the window instance.
        /// </summary>
        /// <returns>The object being edited, or null if not found or reflection failed.</returns>
        public static UnityEngine.Object GetObjectBeingEdited()
        {
            try
            {
                if (k_ObjectBeingEditedField == null)
                    return null;

                var selector = GetInstance();
                if (selector == null)
                    return null;

                return k_ObjectBeingEditedField.GetValue(selector) as UnityEngine.Object;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Registers a handler for the ObjectSelector 'shown' event.
        /// </summary>
        public static void AddShownEventHandler(Action<EditorWindow> handler)
        {
            try
            {
                if (k_ShownEvent == null)
                    return;

                k_ShownEvent.AddEventHandler(null, handler);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to add ObjectSelector shown event handler: {e.Message}");
            }
        }

        /// <summary>
        /// Unregisters a handler from the ObjectSelector 'shown' event.
        /// </summary>
        public static void RemoveShownEventHandler(Action<EditorWindow> handler)
        {
            try
            {
                if (k_ShownEvent == null)
                    return;

                k_ShownEvent.RemoveEventHandler(null, handler);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to remove ObjectSelector shown event handler: {e.Message}");
            }
        }

        /// <summary>
        /// Sets the selection in the ObjectSelector.
        /// </summary>
        public static void SetSelection(int instanceID)
        {
            try
            {
                if (k_SetSelectionMethod == null)
                    return;

#if UNITY_6000_4_OR_NEWER
                // Unity 6.4 and above use EntityId parameter
                k_SetSelectionMethod.Invoke(null, new object[] { (EntityId)instanceID });
#else
                // Unity 6000.3 and below use int parameter
                k_SetSelectionMethod.Invoke(null, new object[] { instanceID });
#endif
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to set ObjectSelector selection: {e.Message}");
            }
        }
    }
}
#endif // OBJECT_SELECTOR_TOOLBAR_DECORATOR
