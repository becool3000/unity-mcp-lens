using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Unity.AI.Assistant.Editor.Backend.Socket.Utilities;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class RemoveGameObjectTool
    {
        internal const string k_FunctionId = "Unity.GameObject.RemoveGameObject";
        internal const string k_GameObjectInstanceIdRequiredMessage = "gameObjectInstanceId is required.";

        [AgentTool(
            "Remove a GameObject from the Unity scene. Please prioritize deleting by instance ID to avoid ambiguity.",
            k_FunctionId,
            ToolCallEnvironment.EditMode,
            tags: FunctionCallingUtilities.k_GameObjectTag)]
        public static async Task RemoveGameObject(
            ToolExecutionContext context,
            [Parameter("GameObject instance ID (e.g. 12345). Use the ID returned from CreateGameObject or other GameObject operations.")]
            int gameObjectInstanceId)
        {
            var (targetGo, gameObjectError) = GameObjectToolsHelper.ValidateGameObject(gameObjectInstanceId);
            if (targetGo == null)
                throw new InvalidOperationException(gameObjectError ?? $"GameObject '{gameObjectInstanceId}' not found.");

            await context.Permissions.CheckUnityObjectAccess(IToolPermissions.ItemOperation.Delete, typeof(GameObject), targetGo);
            Undo.DestroyObjectImmediate(targetGo);
        }
    }
}
