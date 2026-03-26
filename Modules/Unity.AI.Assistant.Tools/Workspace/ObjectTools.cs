using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using Unity.AI.Search.Editor.Knowledge;
using AssetKnowledge = Unity.AI.Assistant.Editor.AssetKnowledge;

namespace Unity.AI.Assistant.Tools.Editor
{
    class ObjectTools
    {
        const string k_GetObjectDataFunctionId = "Unity.GetObjectData";

        const int k_MaxArrayElements = 20;
        const int k_MaxDepth = 6;
        const int k_MaxCharacters = 8192;

        [Serializable]
        public class GetObjectDataOutput
        {
            public string Data = string.Empty;

            [Description(
                "True if some properties were truncated because serialization depth was exceeded, false if the data is fully complete.")]
            public bool Truncated = false;

            public string Description = string.Empty;

            public override string ToString()
            {
                return $"{Data}\n\nTruncated: {Truncated} Description:{Description}";
            }
        }

        [AgentTool(
            "Get the data of the object or asset with the given instance ID. " +
            "Properties with value '...' indicate they were truncated because of their depth. " +
            "Use the property path parameter to get the data of truncated properties.",
            k_GetObjectDataFunctionId,
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_SmartContextTag)]
        internal static async Task<GetObjectDataOutput> GetObjectData(
            [Parameter("The instance ID of the object or asset to extract data from (can be negative).")]
            int instanceID,

            [Parameter("A specific property path to get data from, like 'm_Shapes/m_IndexBuffer'. " +
                "Use brackets to indicate an index in an array property, like 'm_Shapes/m_IndexBuffer[17]'. " +
                "Use this ONLY to get the value of truncated properties. Never guess the path. " +
                "Leave empty or null to get all the object properties instead.")]
            string propertyPath = null
            )
        {
#if UNITY_6000_3_OR_NEWER
            var instance = EditorUtility.EntityIdToObject(instanceID);
#else
            var instance = EditorUtility.InstanceIDToObject(instanceID);
#endif
            if (instance == null)
                throw new ArgumentException($"Cannot find object with ID: {instanceID}");

            var serializedObject = new SerializedObject(instance);
            var (json, serializationDepth) = serializedObject.ToJson(
                propertyPath: propertyPath,
                maxDepth: k_MaxDepth,
                maxArrayElements: k_MaxArrayElements,
                maxLength: k_MaxCharacters
            );

            var assetKnowledgeTags = string.Empty;

            if (AssetKnowledge.AssetKnowledgeUsable)
            {
                await KnowledgeSearchProvider.WaitForReadinessAsync();
                assetKnowledgeTags = KnowledgeSearchProvider.GetTags(instance);
            }

            var truncated = k_MaxDepth >= 0 && serializationDepth != k_MaxDepth;
            var formattedOutput = new GetObjectDataOutput
            {
                Data = json,
                Truncated = truncated,
                Description = assetKnowledgeTags
            };

            InternalLog.Log(formattedOutput.ToString());

            return formattedOutput;
        }
    }
}
