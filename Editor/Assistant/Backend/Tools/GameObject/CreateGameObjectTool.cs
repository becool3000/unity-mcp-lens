using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class CreateGameObjectTool
    {
        internal const string k_FunctionId = "Unity.GameObject.CreateGameObject";
        internal const string k_TagNotFoundMessage = "Tag '{0}' does not exist. Use ModifyTag tool to add it before creating the GameObject.";
        internal const string k_LayerNotFoundMessage = "Layer '{0}' does not exist. Use ModifyLayer tool to add it before creating the GameObject.";
        internal const string k_ParentNotFoundMessage = "Parent GameObject '{0}' not found in the scene.";

        [Serializable]
        public struct CreateGameObjectOutput
        {
            [JsonProperty("message")]
            public string Message;

            [JsonProperty("gameObjectId")]
            public int GameObjectId;
        }

        [AgentTool("Create a new GameObject in the Unity scene.", k_FunctionId, ToolCallEnvironment.EditMode, tags: FunctionCallingUtilities.k_GameObjectTag)]
        public static async Task<CreateGameObjectOutput> CreateGameObject(
            ToolExecutionContext context,
            [Parameter("Name for the new GameObject")]
            string name,
            [Parameter("GameObject tag")]
            string tag = null,
            [Parameter("GameObject layer name")]
            string layer = null,
            [Parameter("Parent GameObject name")]
            string parent = null)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("'name' parameter is required.");

            await context.Permissions.CheckUnityObjectAccess(IToolPermissions.ItemOperation.Create, typeof(GameObject), null);
            var newGo = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(newGo, $"Create GameObject '{name}'");
            context.Permissions.IgnoreUnityObject(newGo);

            if (!string.IsNullOrEmpty(tag) && !GameObjectToolsHelper.SetGameObjectTag(newGo, tag))
            {
                await context.Permissions.CheckUnityObjectAccess(IToolPermissions.ItemOperation.Delete, typeof(GameObject), newGo);
                Undo.DestroyObjectImmediate(newGo);
                throw new InvalidOperationException(string.Format(k_TagNotFoundMessage, tag));
            }

            if (!string.IsNullOrEmpty(layer) && !GameObjectToolsHelper.SetGameObjectLayer(newGo, layer))
            {
                await context.Permissions.CheckUnityObjectAccess(IToolPermissions.ItemOperation.Delete, typeof(GameObject), newGo);
                Undo.DestroyObjectImmediate(newGo);
                throw new InvalidOperationException(string.Format(k_LayerNotFoundMessage, layer));
            }

            if (!string.IsNullOrEmpty(parent))
            {
                var parentGo = GameObjectToolsHelper.FindGameObjectByName(parent);
                if (parentGo != null)
                {
                    newGo.transform.SetParent(parentGo.transform);
                }
                else
                {
                    await context.Permissions.CheckUnityObjectAccess(IToolPermissions.ItemOperation.Delete, typeof(GameObject), newGo);
                    Undo.DestroyObjectImmediate(newGo);
                    throw new InvalidOperationException(string.Format(k_ParentNotFoundMessage, parent));
                }
            }

            Selection.activeGameObject = newGo;
            EditorUtility.SetDirty(newGo);

            var baseMessage = $"GameObject '{name}' created successfully with ID {newGo.GetInstanceID()}.";
            baseMessage += " Note: Consider adding components (like MeshRenderer, Collider, Rigidbody, etc.) to this GameObject using the AddComponent tool depending on your task requirements.";

            return new CreateGameObjectOutput
            {
                Message = baseMessage,
                GameObjectId = newGo.GetInstanceID(),
            };
        }

    }
}
