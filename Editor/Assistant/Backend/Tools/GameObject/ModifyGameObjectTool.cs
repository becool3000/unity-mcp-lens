using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Unity.AI.Assistant.Editor.Backend.Socket.Utilities;
using Unity.AI.Assistant.FunctionCalling;
using UnityEditorInternal;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class ModifyGameObjectTool
    {
        internal const string k_FunctionId = "Unity.GameObject.ModifyGameObject";
        internal const string k_TagNotFoundMessage = "Tag '{0}' does not exist. Use ModifyTag tool with action 'list_tags' to see available tags or 'add' to create new tags.";
        internal const string k_LayerNotFoundMessage = "Layer '{0}' does not exist. Use ModifyLayer tool with action 'list_layers' to see available layers or 'add' to create new layers.";
        internal const string k_ParentNotFoundMessage = "Parent GameObject '{0}' not found in the scene.";

        [Serializable]
        public struct ModifyGameObjectOutput
        {
            [JsonProperty("message")]
            public string Message;

            [JsonProperty("gameObjectId")]
            public int GameObjectId;
        }

        [AgentTool(
            "Edit properties, tag, layer, or parent of an existing GameObject in the Unity scene. Note: This doesn't add or change components. Use AddComoponentTool to add componentes and SetComponentPropertyTool to change properties.",
            k_FunctionId,
            ToolCallEnvironment.EditMode,
            tags: FunctionCallingUtilities.k_GameObjectTag)]
        public static async Task<ModifyGameObjectOutput> ModifyGameObject(
            ToolExecutionContext context,
            [Parameter("GameObject instance ID (e.g. 12345). Use the ID returned from CreateGameObject or other GameObject operations.")]
            int gameObjectInstanceId,
            [Parameter("New name for the GameObject. Only required if we want to change the name.")]
            string name = null,
            [Parameter("New GameObject tag. Only required if we want to change the tag.")]
            string tag = null,
            [Parameter("New GameObject layer name. Only required if we want to change the layer.")]
            string layer = null,
            [Parameter("New Parent GameObject name. Only required if we want to change the parent.")]
            string parent = null,
            [Parameter("If true, the local transform (position, rotation, scale) of the GameObject will be reset to identity after reparenting. Defaults to true. Only relevant when 'parent' is also provided.")]
            bool resetLocalTransformOnReparent = true)
        {
            Debug.Log($"[ModifyGameObjectTool] Call invoked - gameObjectInstanceId: '{gameObjectInstanceId}', name: {name}, tag: '{tag}', layer: '{layer}', parent: '{parent}', resetLocalTransformOnReparent: {resetLocalTransformOnReparent}");

            var (targetGo, gameObjectError) = GameObjectToolsHelper.ValidateGameObject(gameObjectInstanceId);
            if (targetGo == null)
                throw new InvalidOperationException(gameObjectError ?? GameObjectToolsHelper.FormatGameObjectNotFoundMessage(gameObjectInstanceId));

            await context.Permissions.CheckUnityObjectAccess(IToolPermissions.ItemOperation.Modify, typeof(GameObject), targetGo);

            var oldName = targetGo.name;

            var validatedTag = ValidateTag(tag);
            var validatedLayer = ValidateLayer(layer);
            var parentGo = ValidateParent(parent);

            if (!string.IsNullOrEmpty(name))
                targetGo.name = name;

            if (!string.IsNullOrEmpty(validatedTag))
                targetGo.tag = validatedTag;

            if (validatedLayer.HasValue)
                targetGo.layer = validatedLayer.Value;

            if (parentGo != null)
            {
                targetGo.transform.SetParent(parentGo.transform);
                if (resetLocalTransformOnReparent)
                {
                    targetGo.transform.localPosition = Vector3.zero;
                    targetGo.transform.localRotation = Quaternion.identity;
                    targetGo.transform.localScale = Vector3.one;
                }
            }

            Selection.activeGameObject = targetGo;
            EditorUtility.SetDirty(targetGo);

            var message = !string.IsNullOrEmpty(name) && name != oldName
                ? $"GameObject '{oldName}' (ID {targetGo.GetInstanceID()}) renamed to '{targetGo.name}' successfully."
                : $"GameObject '{targetGo.name}' (ID {targetGo.GetInstanceID()}) edited successfully.";

            return new ModifyGameObjectOutput
            {
                Message = message,
                GameObjectId = targetGo.GetInstanceID(),
            };
        }

        static string ValidateTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return null;

            foreach (var existingTag in InternalEditorUtility.tags)
            {
                if (string.Equals(existingTag, tag, StringComparison.Ordinal))
                    return tag;
            }

            throw new InvalidOperationException(string.Format(k_TagNotFoundMessage, tag));
        }

        static int? ValidateLayer(string layer)
        {
            if (string.IsNullOrEmpty(layer))
                return null;

            var layerId = LayerMask.NameToLayer(layer);
            if (layerId == -1)
                throw new InvalidOperationException(string.Format(k_LayerNotFoundMessage, layer));

            return layerId;
        }

        static GameObject ValidateParent(string parentName)
        {
            if (string.IsNullOrEmpty(parentName))
                return null;

            var parentGo = GameObjectToolsHelper.FindGameObjectByName(parentName);
            if (parentGo == null)
                throw new InvalidOperationException(string.Format(k_ParentNotFoundMessage, parentName));

            return parentGo;
        }
    }
}
