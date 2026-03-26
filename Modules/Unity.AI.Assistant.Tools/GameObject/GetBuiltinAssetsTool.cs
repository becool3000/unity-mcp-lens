using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class GetBuiltinAssetsTool
    {
        internal const string k_FunctionId = "Unity.GameObject.GetBuiltinAssets";
        const string k_PrimitiveMeshKey = "PrimitiveMesh";
        const string k_DefaultMaterialKey = "DefaultMaterial";
        internal const string k_AssetTypeRequiredMessage = "assetType is required.";
        internal const string k_InvalidAssetTypeMessage = "Invalid assetType '{0}'. Valid values are: {1}";
        internal const string k_DefaultMaterialFailedMessage = "Failed to retrieve default material.";
        internal const string k_PrimitiveMeshFailedMessage = "Failed to retrieve primitive mesh '{0}'.";

        static readonly Dictionary<string, PrimitiveType> s_PrimitiveNameMap = new Dictionary<string, PrimitiveType>(StringComparer.OrdinalIgnoreCase)
        {
            {"Sphere", PrimitiveType.Sphere},
            {"Capsule", PrimitiveType.Capsule},
            {"Cylinder", PrimitiveType.Cylinder},
            {"Cube", PrimitiveType.Cube},
            {"Plane", PrimitiveType.Plane},
            {"Quad", PrimitiveType.Quad}
        };

         static readonly Dictionary<PrimitiveType, Mesh> s_MeshCache = new Dictionary<PrimitiveType, Mesh>();
        static Material s_DefaultMaterialCache;

        [Serializable]
        public struct BuiltinAssetInfo
        {
            [JsonProperty("name")]
            public string Name;

            [JsonProperty("type")]
            public string Type;

            [JsonProperty("instanceID")]
            public int InstanceID;
        }

        [Serializable]
        public struct GetBuiltinAssetsOutput
        {
            [JsonProperty("message")]
            public string Message;

            [JsonProperty("assets")]
            public BuiltinAssetInfo[] Assets;
        }

        [AgentTool(
            "Get built-in Unity assets such as primitive meshes (Cube, Sphere, Capsule, Cylinder, Plane, Quad) or the default material as a fallback if we only need simple geometry and a default material, no specific asset from the current project. " +
            "Use this tool to get references to Unity's built-in primitive meshes or default materials that can be used with components like MeshFilter or MeshRenderer. " +
            "Returns instance IDs that can be used with SetComponentProperty tool in {\"fileID\": instanceId} format.",
            k_FunctionId,
            assistantMode: AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_GameObjectTag)]
        public static GetBuiltinAssetsOutput GetBuiltinAssets(
            [Parameter("Type of built-in asset to retrieve. Valid values: 'PrimitiveMesh' to get all primitive meshes, 'DefaultMaterial' to get the default material, or a specific primitive name ('Cube', 'Sphere', 'Capsule', 'Cylinder', 'Plane', 'Quad') to get just that primitive mesh.")]
            string assetType)
        {
            if (string.IsNullOrWhiteSpace(assetType))
                throw new ArgumentException(k_AssetTypeRequiredMessage);

            var normalizedType = assetType.Trim();
            var assets = new List<BuiltinAssetInfo>();

            if (normalizedType.Equals(k_DefaultMaterialKey, StringComparison.OrdinalIgnoreCase))
            {
                var material = GetDefaultMaterial();
                if (material == null)
                    throw new InvalidOperationException(k_DefaultMaterialFailedMessage);

                assets.Add(new BuiltinAssetInfo
                {
                    Name = material.name,
                    Type = "Material",
                    InstanceID = material.GetInstanceID()
                });

                return new GetBuiltinAssetsOutput
                {
                    Message = "Retrieved default material.",
                    Assets = assets.ToArray()
                };
            }

            if (normalizedType.Equals(k_PrimitiveMeshKey, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var kvp in s_PrimitiveNameMap)
                {
                    var mesh = GetPrimitiveMesh(kvp.Key);
                    if (mesh == null)
                        throw new InvalidOperationException(string.Format(k_PrimitiveMeshFailedMessage, kvp.Key));

                    assets.Add(new BuiltinAssetInfo
                    {
                        Name = kvp.Key,
                        Type = "Mesh",
                        InstanceID = mesh.GetInstanceID()
                    });
                }

                return new GetBuiltinAssetsOutput
                {
                    Message = $"Retrieved {assets.Count} primitive meshes.",
                    Assets = assets.ToArray()
                };
            }

            if (s_PrimitiveNameMap.ContainsKey(normalizedType))
            {
                var mesh = GetPrimitiveMesh(normalizedType);
                if (mesh == null)
                    throw new InvalidOperationException(string.Format(k_PrimitiveMeshFailedMessage, normalizedType));

                assets.Add(new BuiltinAssetInfo
                {
                    Name = normalizedType,
                    Type = "Mesh",
                    InstanceID = mesh.GetInstanceID()
                });

                return new GetBuiltinAssetsOutput
                {
                    Message = $"Retrieved primitive mesh '{normalizedType}'.",
                    Assets = assets.ToArray()
                };
            }

            var validTypes = string.Join(", ", new[] { k_PrimitiveMeshKey, k_DefaultMaterialKey, "Cube", "Sphere", "Capsule", "Cylinder", "Plane", "Quad" });
            throw new ArgumentException(string.Format(k_InvalidAssetTypeMessage, assetType, validTypes));
        }

        [ToolPermissionIgnore]  // To ignore DestroyImmediate permission
        static Mesh GetPrimitiveMesh(string primitiveName)
        {
            if (!s_PrimitiveNameMap.TryGetValue(primitiveName, out PrimitiveType primitiveType))
            {
                Debug.LogError($"Unknown primitive name '{primitiveName}'.");
                return null;
            }

            if (s_MeshCache.TryGetValue(primitiveType, out Mesh cachedMesh))
            {
                return cachedMesh;
            }

            var temp = GameObject.CreatePrimitive(primitiveType);
            var mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            s_MeshCache[primitiveType] = mesh;
            UnityEngine.Object.DestroyImmediate(temp);
            return mesh;
        }

        [ToolPermissionIgnore]  // To ignore DestroyImmediate permission
        static Material GetDefaultMaterial()
        {
            if (s_DefaultMaterialCache != null)
                return s_DefaultMaterialCache;

            // Create a temporary primitive to get the default material
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            s_DefaultMaterialCache = temp.GetComponent<Renderer>().sharedMaterial;
            UnityEngine.Object.DestroyImmediate(temp);
            return s_DefaultMaterialCache;
        }
    }
}
