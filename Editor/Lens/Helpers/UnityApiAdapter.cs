using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Becool.UnityMcpLens.Editor.Helpers
{
    /// <summary>
    /// Provides a compatibility layer for Unity API changes across versions.
    /// Centralizes all version-specific API differences to avoid scattered #if directives.
    /// </summary>
    static class UnityApiAdapter
    {
        public static object GetObjectId(Object obj)
        {
            if (obj == null)
                return null;

#if UNITY_6000_3_OR_NEWER
            return EntityId.ToULong(obj.GetEntityId());
#else
            return obj.GetInstanceID();
#endif
        }

        public static object GetObjectIdOrZero(Object obj)
        {
            return obj == null ? 0 : GetObjectId(obj);
        }

        public static bool ObjectIdEquals(Object obj, string idText)
        {
            if (obj == null || string.IsNullOrWhiteSpace(idText))
                return false;

#if UNITY_6000_3_OR_NEWER
            return ulong.TryParse(idText, out ulong id) &&
                EntityId.ToULong(obj.GetEntityId()) == id;
#else
            return int.TryParse(idText, out int id) && obj.GetInstanceID() == id;
#endif
        }

        public static T[] FindObjectsByType<T>(FindObjectsInactive inactiveMode)
            where T : Object
        {
            return Object.FindObjectsByType<T>(inactiveMode);
        }

        public static Object[] FindObjectsByType(System.Type type, FindObjectsInactive inactiveMode)
        {
            return Object.FindObjectsByType(type, inactiveMode);
        }

        /// <summary>
        /// Gets the ID of the active selected object (EntityId in 6.3+, InstanceID in earlier versions).
        /// </summary>
        /// <returns>The EntityId of the currently selected object.</returns>
        public static object GetActiveSelectionId()
        {
#if UNITY_6000_3_OR_NEWER
            return EntityId.ToULong(Selection.activeEntityId);
#else
            return Selection.activeInstanceID;
#endif
        }

        /// <summary>
        /// Gets the object-reference ID value for a serialized property.
        /// </summary>
        public static object GetObjectReferenceId(SerializedProperty property)
        {
            if (property == null)
                return 0;

#if UNITY_6000_3_OR_NEWER
            return EntityId.ToULong(property.objectReferenceEntityIdValue);
#else
            return property.objectReferenceInstanceIDValue;
#endif
        }

        /// <summary>
        /// Checks whether a serialized object-reference property has a non-zero backing ID.
        /// </summary>
        public static bool HasObjectReferenceId(SerializedProperty property)
        {
            object id = GetObjectReferenceId(property);
            return id switch
            {
                ulong entityId => entityId != 0UL,
                int instanceId => instanceId != 0,
                _ => false
            };
        }

        /// <summary>
        /// Gets the field name for the LogEntry ID field used in reflection.
        /// Unity 6.3+ renamed "instanceID" to "entityId".
        /// </summary>
        /// <returns>The string "instanceID" for Unity versions before 6.3.</returns>
        public static string GetLogEntryIdFieldName()
        {
#if UNITY_6000_3_OR_NEWER
            return "entityId";
#else
            return "instanceID";
#endif
        }
    }
}
