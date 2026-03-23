using System;
using System.IO;
using System.Threading.Tasks;
using Unity.AI.Mesh.Services.Stores.States;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Mesh.Services.Utilities
{
    static class AssetReferenceExtensions
    {
        public static async Task<Stream> GetCompatibleImageStreamAsync(this AssetReference asset) =>
            asset.Exists() ? await ImageFileUtilities.GetCompatibleImageStreamAsync(new Uri(Path.GetFullPath(asset.GetPath()))) : null;

        public static async Task<bool> Replace(this AssetReference asset, MeshResult generatedMesh)
        {
            if (await generatedMesh.CopyToAsync(asset))
            {
                return true; // EnableGenerationLabel is already called in CopyToAsync
            }

            return false;
        }

        public static Task<bool> IsBlank(this AssetReference asset)
        {
            var prefab = asset.GetObject<GameObject>();
            if (prefab == null)
                return Task.FromResult(false);

            var meshFilter = prefab.GetComponent<MeshFilter>();
            var meshRenderer = prefab.GetComponent<MeshRenderer>();

            if (meshFilter == null || meshRenderer == null)
                return Task.FromResult(false);

            var cubePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetUtils.blankCubeMesh);
            if (cubePrefab == null)
                return Task.FromResult(false);

            var cubeMeshFilter = cubePrefab.GetComponent<MeshFilter>();
            var cubeMeshRenderer = cubePrefab.GetComponent<MeshRenderer>();

            if (cubeMeshFilter == null || cubeMeshRenderer == null)
                return Task.FromResult(false);

            var isBlank = meshFilter.sharedMesh == cubeMeshFilter.sharedMesh &&
                meshRenderer.sharedMaterial == cubeMeshRenderer.sharedMaterial;

            return Task.FromResult(isBlank);
        }

        public static MeshResult ToResult(this AssetReference asset) => MeshResult.FromPath(asset.GetPath());

        public static async Task<bool> SaveToGeneratedAssets(this AssetReference asset)
        {
            try
            {
                await asset.ToResult().CopyToProject(new GenerationSetting().MakeMetadata(asset), asset.GetGeneratedAssetsPath());
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
