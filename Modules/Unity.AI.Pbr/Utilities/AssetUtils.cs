using System;
using System.IO;
using System.Linq;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Pbr.Services.Stores.States;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Pbr.Services.Utilities
{
    static class AssetUtils
    {
        public const string defaultNewAssetName = "New Material";
        public const string materialExtension = ".mat";
        public const string defaultTerrainLayerName = "New Terrain Layer";
        public const string terrainLayerExtension = ".terrainlayer";
        public static readonly string[] supportedExtensions = { materialExtension, terrainLayerExtension };
        public static string[] supportedGeneratedExtensions => ImageFileUtilities.knownExtensions.Select(ext => $"_{MapType.Preview}{ext}").Concat(supportedExtensions).ToArray();

        public static string CreateBlankMaterial(string path) => CreateBlankMaterial(path, false);

        public static string CreateBlankMaterial(string path, bool force) => CreateBlankMaterial(path, null, force);

        public static string CreateBlankMaterial(string path, Shader defaultShader, bool force = true)
        {
            if (!defaultShader)
                defaultShader = MaterialUtilities.GetDefaultMaterial().shader;

            path = Path.ChangeExtension(path, materialExtension);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var newMaterial = new Material(defaultShader);
            var assetPath = AssetDatabase.GenerateUniqueAssetPath(path);
            AssetDatabase.CreateAsset(newMaterial, assetPath);
            return assetPath;
        }

        public static Material CreateAndSelectBlankMaterial(bool force = true)
        {
            var newAssetName = defaultNewAssetName;
            var defaultShader = Selection.activeObject as Shader;
            if (defaultShader)
                newAssetName = Path.GetFileNameWithoutExtension(defaultShader.name);

            var basePath = AssetUtilities.GetSelectionPath();
            var path = $"{basePath}/{newAssetName}{materialExtension}";
            if (force || !File.Exists(path))
            {
                path = AssetDatabase.GenerateUniqueAssetPath(path);
                path = CreateBlankMaterial(path, defaultShader);
                if (string.IsNullOrEmpty(path))
                    Debug.Log($"Failed to create material for '{path}'.");
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Selection.activeObject = material;
            return material;
        }

        public static Material CreateMaterialFromShaderGraph(string shaderGraphPath)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderGraphPath);
            if (shader == null)
            {
                Debug.LogError($"Could not load shader from {shaderGraphPath}");
                return null;
            }

            var shaderGraphName = Path.GetFileNameWithoutExtension(shaderGraphPath);
            var materialName = $"{shaderGraphName} Material";

            var directory = Path.GetDirectoryName(shaderGraphPath);
            var materialPath = Path.Combine(directory, materialName + ".mat");
            materialPath = AssetDatabase.GenerateUniqueAssetPath(materialPath);

            var material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
            AssetDatabase.SaveAssets();

            EditorGUIUtility.PingObject(material);
            Selection.activeObject = material;

            return material;
        }

        public static bool IsShaderGraph(Object obj)
        {
            if (obj == null)
                return false;

            var path = AssetDatabase.GetAssetPath(obj);
            return !string.IsNullOrEmpty(path) && path.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase);
        }

        public static string CreateBlankTerrainLayer(string path) => CreateBlankTerrainLayer(path, false);

        public static string CreateBlankTerrainLayer(string path, bool force)
        {
            path = Path.ChangeExtension(path, terrainLayerExtension);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var newTerrainLayer = new TerrainLayer();
            var assetPath = AssetDatabase.GenerateUniqueAssetPath(path);
            AssetDatabase.CreateAsset(newTerrainLayer, assetPath);
            return assetPath;
        }

        public static TerrainLayer CreateBlankTerrainLayer(bool force = true)
        {
            var basePath = AssetUtilities.GetSelectionPath();
            var path = $"{basePath}/{defaultTerrainLayerName}{terrainLayerExtension}";
            if (force || !File.Exists(path))
            {
                path = AssetDatabase.GenerateUniqueAssetPath(path);
                path = CreateBlankTerrainLayer(path);
                if (string.IsNullOrEmpty(path))
                    Debug.Log($"Failed to create terrain layer for '{path}'.");
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            var terrainLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            return terrainLayer;
        }

        public static TerrainLayer CreateAndSelectBlankTerrainLayer(bool force = true)
        {
            var terrainLayer = CreateBlankTerrainLayer(force);
            Selection.activeObject = terrainLayer;
            return terrainLayer;
        }

        public static TerrainData CreateTerrainDataForTerrain(Terrain terrain)
        {
            var terrainData = new TerrainData
            {
                heightmapResolution = 513,
                baseMapResolution = 1024,
                size = new Vector3(1000, 600, 1000)
            };

            var terrainDataPath = AssetDatabase.GenerateUniqueAssetPath("Assets/New Terrain.asset");

            AssetDatabase.CreateAsset(terrainData, terrainDataPath);
            AssetDatabase.SaveAssets();

            terrain.terrainData = terrainData;

            return terrainData;
        }
    }
}
