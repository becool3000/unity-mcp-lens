using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.FunctionCalling;
using UnityEditor;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    static class FunctionCallParameterFormatter
    {
        public static string FormatInstanceID(JToken value)
        {
#if UNITY_6000_3_OR_NEWER
            var obj = EditorUtility.EntityIdToObject(value.Value<int>());
#else
            var obj = EditorUtility.InstanceIDToObject(value.Value<int>());
#endif
            var displayName = obj ? obj.name : null;
            if (displayName != null)
                return $"{value.ConvertToString()} '{displayName}' [{obj.GetType().Name}]";
            return value.ConvertToString();
        }
    }
}
