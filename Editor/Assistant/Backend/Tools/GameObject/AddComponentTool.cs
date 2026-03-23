using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Unity.AI.Assistant.Editor.Backend.Socket.Utilities;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class AddComponentTool
    {
        internal const string k_FunctionId = "Unity.GameObject.AddComponent";
        internal const string k_GameObjectInstanceIdRequiredMessage = "gameObjectInstanceId is required.";

        internal static string FormatFailedToAddComponentMessage(string componentName, string gameObjectName)
        {
            return $"Failed to add component '{componentName}' to '{gameObjectName}'.";
        }

        [Serializable]
        public struct AddComponentOutput
        {
            [JsonProperty("message")]
            public string Message;

            [JsonProperty("componentInstanceId")]
            public int ComponentInstanceId;
        }

        [AgentTool(
            "Add a component to a GameObject in the Unity scene. CRITICAL: Before setting ANY properties, you MUST first discover the exact property names available for this component type - property names vary between components and using incorrect names will fail. Only set properties after retrieving available property names.",
            k_FunctionId,
            ToolCallEnvironment.EditMode,
            tags: FunctionCallingUtilities.k_GameObjectTag)]
        public static async Task<AddComponentOutput> AddComponent(
            ToolExecutionContext context,
            [Parameter("GameObject instance ID (e.g. 12345). Use the ID returned from CreateGameObject or other GameObject operations.")]
            int gameObjectInstanceId,
            [Parameter("Component type name to add (e.g., 'Rigidbody', 'BoxCollider', 'MyScript')")]
            string componentName,
            [Parameter("Optional object with property names as direct keys (NOT nested under 'Item' or any wrapper). Each key is a component property name, each value is the property value. For ObjectReference properties, use {\"fileID\": instanceId}. Pass null to skip setting properties. Examples: {\"volume\": 0.5, \"pitch\": 1.0} for AudioSource, {\"mass\": 2.0, \"useGravity\": false} for Rigidbody, {\"m_Materials\": [{\"fileID\": 67890}]} for Renderer.")]
            JObject componentProperties = null)
        {
            var (targetGo, gameObjectError) = GameObjectToolsHelper.ValidateGameObject(gameObjectInstanceId);
            if (targetGo == null)
                throw new InvalidOperationException(gameObjectError ?? GameObjectToolsHelper.FormatGameObjectNotFoundMessage(gameObjectInstanceId));

            GameObjectToolsHelper.ValidateComponentName(componentName);

            await context.Permissions.CheckUnityObjectAccess(IToolPermissions.ItemOperation.Modify, typeof(GameObject), targetGo);
            var addedComponent = GameObjectToolsHelper.AddComponentByName(targetGo, componentName);
            if (addedComponent == null)
                throw new InvalidOperationException(FormatFailedToAddComponentMessage(componentName, targetGo.name));

            context.Permissions.IgnoreUnityObject(addedComponent);

            if (componentProperties != null && componentProperties.Count > 0)
            {
                try
                {
                    await context.Permissions.CheckUnityObjectAccess(IToolPermissions.ItemOperation.Modify, addedComponent.GetType(), addedComponent);
                    GameObjectToolsHelper.SetComponentProperties(addedComponent, componentProperties);
                }
                catch
                {
                    DeleteComponent(addedComponent);
                    throw;
                }
            }
            else
            {
                EditorUtility.SetDirty(targetGo);
            }

            return new AddComponentOutput
            {
                Message = $"Component '{componentName}' added to '{targetGo.name}'.",
                ComponentInstanceId = addedComponent.GetInstanceID()
            };
        }

        [ToolPermissionIgnore]
        static void DeleteComponent(Component component)
        {
            UnityEngine.Object.DestroyImmediate(component);
        }
    }
}
