using UnityEditor;
using UnityEngine;

namespace Unity.AI.MCP.Editor.Helpers
{
    /// <summary>
    /// Provides a compatibility layer for Unity API changes across versions.
    /// Centralizes all version-specific API differences to avoid scattered #if directives.
    /// </summary>
    static class UnityApiAdapter
    {
#if UNITY_6000_3_OR_NEWER
        /// <summary>
        /// Gets a Unity Object from its ID (EntityId in 6.3+, InstanceID in earlier versions).
        /// </summary>
        /// <param name="id">The EntityId of the object.</param>
        /// <returns>The Unity Object associated with the EntityId, or null if not found.</returns>
        public static UnityEngine.Object GetObjectFromId(int id)
        {
            return EditorUtility.EntityIdToObject(id);
        }

        /// <summary>
        /// Gets the ID of the active selected object (EntityId in 6.3+, InstanceID in earlier versions).
        /// </summary>
        /// <returns>The EntityId of the currently selected object.</returns>
        public static int GetActiveSelectionId()
        {
            return Selection.activeEntityId;
        }

        /// <summary>
        /// Gets the field name for the LogEntry ID field used in reflection.
        /// Unity 6.3+ renamed "instanceID" to "entityId".
        /// </summary>
        /// <returns>The string "entityId" for Unity 6.3+.</returns>
        public static string GetLogEntryIdFieldName()
        {
            return "entityId";
        }
#else
        /// <summary>
        /// Gets a Unity Object from its ID (EntityId in 6.3+, InstanceID in earlier versions).
        /// </summary>
        /// <param name="id">The InstanceID of the object.</param>
        /// <returns>The Unity Object associated with the InstanceID, or null if not found.</returns>
        public static UnityEngine.Object GetObjectFromId(int id)
        {
            return EditorUtility.InstanceIDToObject(id);
        }

        /// <summary>
        /// Gets the ID of the active selected object (EntityId in 6.3+, InstanceID in earlier versions).
        /// </summary>
        /// <returns>The InstanceID of the currently selected object.</returns>
        public static int GetActiveSelectionId()
        {
            return Selection.activeInstanceID;
        }

        /// <summary>
        /// Gets the field name for the LogEntry ID field used in reflection.
        /// Unity 6.3+ renamed "instanceID" to "entityId".
        /// </summary>
        /// <returns>The string "instanceID" for Unity versions before 6.3.</returns>
        public static string GetLogEntryIdFieldName()
        {
            return "instanceID";
        }
#endif
    }
}
