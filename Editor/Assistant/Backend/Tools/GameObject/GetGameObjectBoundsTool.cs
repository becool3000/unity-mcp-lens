using System;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Editor.Utils;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    class GetGameObjectBoundsTool
    {
        internal const string k_FunctionId = "Unity.GameObject.GetGameObjectBounds";

        internal static string FormatNoBoundsMessage(int instanceID)
        {
            return $"Could not find bounds for ID: {instanceID}";
        }

        [Serializable]
        public struct Vector3Data
        {
            [JsonProperty("x")]
            public float X;

            [JsonProperty("y")]
            public float Y;

            [JsonProperty("z")]
            public float Z;

            public Vector3Data(Vector3 vector)
            {
                X = (float)Math.Round(vector.x, 2);
                Y = (float)Math.Round(vector.y, 2);
                Z = (float)Math.Round(vector.z, 2);
            }

            public Vector3 ToVector3()
            {
                return new Vector3(X, Y, Z);
            }
        }

        [Serializable]
        public struct WorldBounds
        {
            [JsonProperty("center")]
            public Vector3Data Center;

            [JsonProperty("size")]
            public Vector3Data Size;

            [JsonProperty("min")]
            public Vector3Data Min;

            [JsonProperty("max")]
            public Vector3Data Max;

            public WorldBounds(Transform transform, Bounds localBounds)
            {
                // Convert local bounds to world space
                var worldCenter = transform.position + localBounds.center;

                Center = new Vector3Data(worldCenter);
                Size = new Vector3Data(localBounds.size);

                var worldBounds = new Bounds(worldCenter, localBounds.size);
                Min = new Vector3Data(worldBounds.min);
                Max = new Vector3Data(worldBounds.max);
            }
        }

        [AgentTool(
            "Returns the world bounds of a specific GameObject instance in world space (center, size, min, max).",
            k_FunctionId,
            assistantMode: AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_GameObjectTag)]
        public static WorldBounds GetWorldBounds(
            [Parameter("GameObject instance ID (e.g. 12345). Returns the world bounds for this specific GameObject instance.")]
            int gameObjectInstanceId)
        {
            InternalLog.Log($"[GetGameObjectBoundsTool] Call invoked - instanceID: {gameObjectInstanceId}");

#if UNITY_6000_3_OR_NEWER
            GameObject instance = EditorUtility.EntityIdToObject(gameObjectInstanceId) as GameObject;
#else
            GameObject instance = EditorUtility.InstanceIDToObject(gameObjectInstanceId) as GameObject;
#endif
            if (instance == null)
                throw new Exception($"Cannot find object with ID: {gameObjectInstanceId}");
            if (!TryGetObjectBounds(instance, out var localBounds))
                throw new Exception(FormatNoBoundsMessage(gameObjectInstanceId));

            var worldBounds = new WorldBounds(instance.transform, localBounds);

            InternalLog.Log($"[GetGameObjectBoundsTool] Result: {AssistantJsonHelper.Serialize(worldBounds)}");

            return worldBounds;
        }

        static bool TryGetObjectBounds(GameObject instance, out Bounds bounds)
        {
            bounds = default;

            // Try to compute bounds using the extension method
            if (!instance.ComputeLocalBounds(out bounds))
                return false;

            return true;
        }
    }
}
