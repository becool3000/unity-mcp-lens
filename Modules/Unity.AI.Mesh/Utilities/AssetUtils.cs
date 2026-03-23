using System;
using System.IO;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Mesh.Services.Utilities
{
    static class AssetUtils
    {
        public const string defaultNewAssetName = "New Mesh";
        public const string defaultAssetExtension = ".prefab";
        public const string fbxAssetExtension = ".fbx";
        public const string glbAssetExtension = ".glb";
        public const string blankCubeMesh = "Packages/com.unity.ai.assistant/Modules/Unity.AI.Mesh/Meshes/cube.fbx";

        // Known mesh file extensions for file system watching
        public static readonly string[] knownExtensions = { defaultAssetExtension, fbxAssetExtension, glbAssetExtension };

        public static string CreateBlankPrefab(string path) => CreateBlankPrefab(path, false);

        public static string CreateBlankPrefab(string path, bool force)
        {
            path = Path.ChangeExtension(path, defaultAssetExtension);
            if (force || !File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var emptyGameObject = new GameObject("Generated Mesh");
                try
                {
                    // Add MeshFilter and MeshRenderer components
                    var meshFilter = emptyGameObject.AddComponent<MeshFilter>();
                    var meshRenderer = emptyGameObject.AddComponent<MeshRenderer>();

                    // Set a default cube mesh and material from our own cube.fbx
                    var cubePrefabPath = blankCubeMesh;
                    var cubePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(cubePrefabPath);
                    if (cubePrefab != null)
                    {
                        var cubeMeshFilter = cubePrefab.GetComponent<MeshFilter>();
                        if (cubeMeshFilter != null)
                        {
                            meshFilter.sharedMesh = cubeMeshFilter.sharedMesh;
                        }

                        var cubeMeshRenderer = cubePrefab.GetComponent<MeshRenderer>();
                        if (cubeMeshRenderer != null)
                        {
                            meshRenderer.sharedMaterial = cubeMeshRenderer.sharedMaterial;
                        }
                    }

                    PrefabUtility.SaveAsPrefabAsset(emptyGameObject, path);
                }
                finally
                {
                    emptyGameObject.SafeDestroy();
                }
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }

            new AssetReference {guid = AssetDatabase.AssetPathToGUID(path)}.EnableGenerationLabel();

            return path;
        }

        static GameObject CreatePrefab(string name, bool force = true)
        {
            var basePath = AssetUtilities.GetSelectionPath();
            var path = $"{basePath}/{name}{defaultAssetExtension}";
            if (force || !File.Exists(path))
            {
                path = AssetDatabase.GenerateUniqueAssetPath(path);
                path = CreateBlankPrefab(path);
                if (string.IsNullOrEmpty(path))
                    return null;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Selection.activeObject = prefab;
            return prefab;
        }

        public static GameObject CreateAndSelectBlankPrefab(bool force = true) => CreatePrefab(defaultNewAssetName, force);
    }
}
