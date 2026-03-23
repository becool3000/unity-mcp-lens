using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class GetSelectionTool
    {
        const string k_FunctionId = "Unity.GameObject.GetSelection";
        internal const string k_InvalidSelectionTypeMessage = "Invalid selectionType '{0}'. Must be 'all', 'gameobjects', or 'assets'.";

        [Serializable]
        public struct GetSelectionOutput
        {
            [JsonProperty("message")]
            public string Message;

            [JsonProperty("gameObjects")]
            public GameObjectInfo[] GameObjects;

            [JsonProperty("projectAssets")]
            public ProjectAssetInfo[] ProjectAssets;
        }

        [Serializable]
        public struct GameObjectInfo
        {
            [JsonProperty("name")]
            public string Name;

            [JsonProperty("path")]
            public string Path;

            [JsonProperty("instanceId")]
            public int InstanceId;
        }

        [Serializable]
        public struct ProjectAssetInfo
        {
            [JsonProperty("name")]
            public string Name;

            [JsonProperty("path")]
            public string Path;

            [JsonProperty("type")]
            public string Type;

            [JsonProperty("instanceId")]
            public int InstanceId;
        }

        [AgentTool(
            "Get the current Editor selection, returning both GameObjects in the scene and Project assets that are selected.",
            k_FunctionId,
            assistantMode: AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_GameObjectTag)]
        public static GetSelectionOutput GetSelection(
            [Parameter("Type of selection to return: 'all' for both GameObjects and assets, 'gameobjects' for only scene GameObjects, 'assets' for only project assets.")]
            string selectionType = "all"
            )
        {
            Debug.Log($"[GetSelectionTool] Call invoked - selectionType: '{selectionType}'");

            var type = selectionType?.ToLowerInvariant() ?? "all";
            bool includeGameObjects = type is "all" or "gameobjects";
            bool includeAssets = type is "all" or "assets";

            if (!includeGameObjects && !includeAssets)
                throw new ArgumentException(string.Format(k_InvalidSelectionTypeMessage, selectionType));

            var selectedObjects = Selection.objects;
            var gameObjectsList = new List<GameObjectInfo>();
            var projectAssetsList = new List<ProjectAssetInfo>();

            foreach (var obj in selectedObjects)
            {
                if (obj is GameObject gameObject)
                {
                    var assetPath = AssetDatabase.GetAssetPath(gameObject);
                    var isSceneGameObject = string.IsNullOrEmpty(assetPath);

                    if (isSceneGameObject && includeGameObjects)
                    {
                        gameObjectsList.Add(new GameObjectInfo
                        {
                            Name = gameObject.name,
                            Path = GetSceneHierarchyPath(gameObject),
                            InstanceId = gameObject.GetInstanceID()
                        });
                    }
                    else if (!isSceneGameObject && includeAssets)
                    {
                        projectAssetsList.Add(new ProjectAssetInfo
                        {
                            Name = gameObject.name,
                            Path = assetPath,
                            Type = "Prefab",
                            InstanceId = gameObject.GetInstanceID()
                        });
                    }
                }
                else if (includeAssets)
                {
                    var assetPath = AssetDatabase.GetAssetPath(obj);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        projectAssetsList.Add(new ProjectAssetInfo
                        {
                            Name = obj.name,
                            Path = assetPath,
                            Type = obj.GetType().Name,
                            InstanceId = obj.GetInstanceID()
                        });
                    }
                }
            }

            var messageParts = new List<string>();

            if (includeGameObjects && gameObjectsList.Count > 0)
                messageParts.Add($"{gameObjectsList.Count} GameObjects (see GameObjects array)");

            if (includeAssets && projectAssetsList.Count > 0)
                messageParts.Add($"{projectAssetsList.Count} Project Assets (see ProjectAssets array)");

            var message = messageParts.Count == 0
                ? "No objects of the specified type are currently selected in the Editor."
                : string.Join(", ", messageParts) + ".";

            return new GetSelectionOutput
            {
                Message = message,
                GameObjects = gameObjectsList.ToArray(),
                ProjectAssets = projectAssetsList.ToArray()
            };
        }

        static string GetSceneHierarchyPath(GameObject gameObject)
        {
            var pathBuilder = new StringBuilder();
            var current = gameObject.transform;

            // Build path from bottom up
            var pathParts = new List<string>();
            while (current != null)
            {
                pathParts.Add(current.name);
                current = current.parent;
            }

            // Reverse to get root-to-leaf path
            pathParts.Reverse();

            // Join with "/" to create hierarchy path
            return string.Join("/", pathParts);
        }
    }
}
