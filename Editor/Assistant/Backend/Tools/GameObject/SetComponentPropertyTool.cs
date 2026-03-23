using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class SetComponentPropertyTool
    {
        internal const string k_FunctionId = "Unity.GameObject.SetComponentProperty";
        internal const string k_ComponentInstanceIdRequiredMessage = "componentInstanceId is required.";

        [AgentTool(
            "Set properties on a specific component instance. CRITICAL: You MUST first discover the exact property names and types for this component - NEVER guess property names. Property names vary between components (e.g., 'm_Volume' vs 'volume') and incorrect names will fail. Always retrieve available properties before setting them.",
            k_FunctionId,
            ToolCallEnvironment.EditMode,
            tags: FunctionCallingUtilities.k_GameObjectTag)]
        public static async Task SetComponentProperty(
            ToolExecutionContext context,
            [Parameter("Component instance ID (e.g. 67890). Sets properties on this specific component instance.")]
            int componentInstanceId,
            [Parameter("Component type name to modify (e.g., 'Rigidbody', 'Transform'). Used for validation to ensure the component instance matches the expected type.")]
            string componentName,
            [Parameter("Object with property names as direct keys (NOT nested under 'Item' or any wrapper). Each key is a component property name, each value is the property value. For ObjectReference properties, use {\"fileID\": instanceId}. Examples: {\"volume\": 0.5, \"pitch\": 1.0} for AudioSource, {\"mass\": 2.0, \"useGravity\": false} for Rigidbody, {\"m_Materials\": [{\"fileID\": 12345}]} for Renderer.")]
            JObject componentProperties)
        {
            var targetComponent = GameObjectToolsHelper.ValidateComponentName(componentName, componentInstanceId);

            await context.Permissions.CheckUnityObjectAccess(IToolPermissions.ItemOperation.Modify, targetComponent.GetType(), targetComponent);
            GameObjectToolsHelper.SetComponentProperties(targetComponent, componentProperties);
        }
    }
}
