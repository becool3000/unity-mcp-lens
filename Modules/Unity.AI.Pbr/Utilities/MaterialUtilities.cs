using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.AI.Pbr.Services.Stores.States;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Pbr.Services.Stores.Selectors;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.AI.Pbr.Srp.Utilities;
using Unity.AI.Toolkit.Asset;
using ShaderUtilities = Unity.AI.Toolkit.Asset.ShaderUtilities;

namespace Unity.AI.Pbr.Services.Utilities
{
    static class MaterialUtilities
    {
        static Material s_BuiltinUnlitMaterial;
        static Material s_BuiltinLitMaterial;

        static readonly Dictionary<Material, Material> k_DefaultLitMaterials = new();
        static readonly Dictionary<Material, Material> k_DefaultUnlitMaterials = new();

        record CacheKey(string url);

        static readonly Dictionary<CacheKey, IMaterialAdapter> k_Cache = new();

        public static bool IsBlank(this IMaterialAdapter material)
        {
            if (!material.AsObject)
                return true;

            var texturePropertyNames = material.GetTexturePropertyNames();
            foreach (var propertyName in texturePropertyNames)
            {
                var texture = material.GetTexture(propertyName);
                if (texture != null)
                    return false;
            }

            return true;
        }

        public static string GetMapsPath(this UnityEngine.Object material) => AssetReferenceExtensions.GetMapsPath(AssetDatabase.GetAssetPath(material));

        public static IMaterialAdapter GetTemporary(this MaterialResult result, IState state)
        {
            if (result.IsMat())
            {
                var cacheKey = new CacheKey(result.uri.GetLocalPath());
                if (k_Cache.TryGetValue(cacheKey, out var material) && material.IsValid)
                    return material;

                material = result.ImportMaterialTemporarily();
                if (!typeof(Material).IsAssignableFrom(material.AsObject.GetType()))
                    material = GetTemporaryMaterialFromMaterialAdapter(material, state);

                k_Cache[cacheKey] = material;
                return material;
            }

            result.Sanitize();
            return GetTemporaryMaterialFromTextures(result.textures, state);
        }

        public static IMaterialAdapter GetTemporaryMaterialFromTextures(this IDictionary<MapType, TextureResult> textures, IState state)
        {
            var material = GetDefaultMaterial(textures.Count == 1);
            var generatedMaterialMapping = MaterialAdapterFactory.Create(material).GetDefaultGeneratedMaterialMapping(state);

            // since we don't go through the asset importer (unlike CopyTo(...)) we need to manually set/unset some properties

            // all RPs
            foreach (var mapping in generatedMaterialMapping)
                material.SetTexture(mapping.Value, null);
            if (material.HasColor("_BaseColor"))
                material.SetColor("_BaseColor", Color.black);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            // builtin and urp
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_EMISSION");
            if (material.HasColor("_EmissionColor"))
                material.SetColor("_EmissionColor", Color.black);

            // hdrp
            material.DisableKeyword("_EMISSIVE_COLOR_MAP");
            material.DisableKeyword("_NORMALMAP");
            material.DisableKeyword("_MASKMAP");
            material.DisableKeyword("_HEIGHTMAP");
            if (material.HasFloat("_EmissiveExposureWeight"))
                material.SetFloat("_EmissiveExposureWeight", 1.0f);

            // set
            foreach (var textureResult in textures)
            {
                var key = textureResult.Key;
                if (textures.Count == 1 && textureResult.Key == MapType.Preview)
                    key = MapType.Delighted;
                if (!generatedMaterialMapping.TryGetValue(key, out var materialProperty))
                    continue;
                if (!material.HasTexture(materialProperty))
                    continue;
                var sourceFilePath = textureResult.Value.uri.GetLocalPath();

                var valid = File.Exists(sourceFilePath) && materialProperty != GenerationResult.noneMapping;
                switch (key)
                {
                    case MapType.Delighted:
                    {
                        material.EnableKeyword("_ALPHATEST_ON");
                        if (material.HasColor("_BaseColor"))
                            material.SetColor("_BaseColor", Color.white);
                        material.SetTexture(materialProperty, valid ? TextureCache.GetTextureUnsafe(textureResult.Value.uri) : null);
                        break;
                    }
                    case MapType.Normal:
                    {
                        material.EnableKeyword("_NORMALMAP");
                        material.SetTexture(materialProperty, valid ? TextureCache.GetNormalMapUnsafe(textureResult.Value.uri) : null);
                        break;
                    }
                    case MapType.Emission:
                        material.EnableKeyword("_EMISSIVE_COLOR_MAP");
                        material.EnableKeyword("_EMISSION");
                        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                        if (material.HasColor("_EmissionColor"))
                            material.SetColor("_EmissionColor", Color.white);
                        if (material.HasFloat("_EmissiveExposureWeight"))
                            material.SetFloat("_EmissiveExposureWeight", 0.5f);
                        material.SetTexture(materialProperty, valid ? TextureCache.GetTextureUnsafe(textureResult.Value.uri) : null);
                        break;
                    case MapType.MaskMap:
                        material.EnableKeyword("_MASKMAP");
                        material.SetTexture(materialProperty, valid ? TextureCache.GetTextureUnsafe(textureResult.Value.uri) : null);
                        break;
                    case MapType.Height:
                        material.EnableKeyword("_HEIGHTMAP");
                        material.SetTexture(materialProperty, valid ? TextureCache.GetTextureUnsafe(textureResult.Value.uri) : null);
                        break;
                    default:
                        material.SetTexture(materialProperty, valid ? TextureCache.GetTextureUnsafe(textureResult.Value.uri) : null);
                        break;
                }
            }

            return MaterialAdapterFactory.Create(material);
        }

        public static IMaterialAdapter GetTemporaryMaterialFromMaterialAdapter(this IMaterialAdapter source, IState state)
        {
            if (source is MaterialAdapter)
                return source; // Already a material adapter, no conversion needed

            if (!source.IsValid)
                return MaterialAdapterFactory.Create(GetDefaultMaterial());

            // Create a default material that will receive the textures
            var material = GetDefaultMaterial();
            var generatedMaterialMapping = MaterialAdapterFactory.Create(material).GetDefaultGeneratedMaterialMapping(state);

            // Initialize material with default settings
            // all RPs
            foreach (var mapping in generatedMaterialMapping)
                material.SetTexture(mapping.Value, null);
            if (material.HasColor("_BaseColor"))
                material.SetColor("_BaseColor", Color.white);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            // builtin and urp
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_EMISSION");
            if (material.HasColor("_EmissionColor"))
                material.SetColor("_EmissionColor", Color.black);

            // hdrp
            material.DisableKeyword("_EMISSIVE_COLOR_MAP");
            material.DisableKeyword("_NORMALMAP");
            material.DisableKeyword("_MASKMAP");
            material.DisableKeyword("_HEIGHTMAP");
            if (material.HasFloat("_EmissiveExposureWeight"))
                material.SetFloat("_EmissiveExposureWeight", 1.0f);

            // Copy textures from source to the appropriate material properties
            var texturePropertyNames = source.GetTexturePropertyNames();
            foreach (var propertyName in texturePropertyNames)
            {
                var texture = source.GetTexture(propertyName);
                if (texture == null)
                    continue;

                // Map TerrainLayer properties to standard material properties
                MapType mapType;
                switch (propertyName)
                {
                    case "_Diffuse":
                        mapType = MapType.Delighted;
                        break;
                    case "_NormalMap":
                        mapType = MapType.Normal;
                        break;
                    case "_MaskMap":
                        mapType = MapType.MaskMap;
                        break;
                    default:
                        continue; // Skip unknown property
                }

                if (!generatedMaterialMapping.TryGetValue(mapType, out var materialProperty))
                    continue;
                if (!material.HasTexture(materialProperty))
                    continue;

                // Apply texture and enable related keywords
                switch (mapType)
                {
                    case MapType.Delighted:
                        material.EnableKeyword("_ALPHATEST_ON");
                        if (material.HasColor("_BaseColor"))
                            material.SetColor("_BaseColor", Color.white);
                        material.SetTexture(materialProperty, texture);
                        break;
                    case MapType.Normal:
                        material.EnableKeyword("_NORMALMAP");
                        material.SetTexture(materialProperty, texture);
                        break;
                    case MapType.MaskMap:
                        material.EnableKeyword("_MASKMAP");
                        material.SetTexture(materialProperty, texture);
                        break;
                    default:
                        material.SetTexture(materialProperty, texture);
                        break;
                }
            }

            return MaterialAdapterFactory.Create(material);
        }

        public static Material GetDefaultMaterial(bool unlit = false)
        {
            var renderPipeline = GraphicsSettings.currentRenderPipeline ?? GraphicsSettings.defaultRenderPipeline;
            if (!renderPipeline || !renderPipeline.defaultMaterial)
            {
                // builtin
                if (unlit)
                {
                    if (!s_BuiltinUnlitMaterial)
                        s_BuiltinUnlitMaterial = new Material(Shader.Find("Unlit/Texture"));
                    return s_BuiltinUnlitMaterial;
                }

                if (!s_BuiltinLitMaterial)
                    s_BuiltinLitMaterial = new Material(Shader.Find("Standard"));
                return s_BuiltinLitMaterial;
            }

            Material material;
            if (!unlit)
            {
                // urp and hdrp (lit) and maybe builtin or custom rps
                if (k_DefaultLitMaterials.TryGetValue(renderPipeline.defaultMaterial, out material) && material)
                    return material;
                material = new Material(renderPipeline.defaultMaterial);
                k_DefaultLitMaterials[renderPipeline.defaultMaterial] = material;
                return material;
            }

            // unlit
            if (k_DefaultUnlitMaterials.TryGetValue(renderPipeline.defaultMaterial, out material) && material)
                return material;

            // urp and hdrp (unlit) and maybe builtin or custom rps
            if (renderPipeline.defaultMaterial.shader.name.EndsWith("/Lit"))
            {
                var unlitShaderName = renderPipeline.defaultMaterial.shader.name.Replace("/Lit", "/Unlit");
                var unlitShader = Shader.Find(unlitShaderName);
                if (unlitShader)
                {
                    material = new Material(unlitShader);
                    k_DefaultUnlitMaterials[renderPipeline.defaultMaterial] = material;
                    return material;
                }
            }

            // fallback to urp
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            k_DefaultUnlitMaterials[renderPipeline.defaultMaterial] = material;
            return material;
        }

        public static void CopyTo(this Material from, Material to)
        {
            if (!from || !to)
                return;

            var texturePropertyNames = from.GetTexturePropertyNames();
            foreach (var propertyName in texturePropertyNames)
            {
                var texture = from.GetTexture(propertyName);
                if (to.HasTexture(propertyName))
                    to.SetTexture(propertyName, texture);
            }

            EditorUtility.SetDirty(to);
        }

        public static bool CopyTo(this MaterialResult from, IMaterialAdapter to, IState state, Dictionary<MapType, string> generatedMaterialMapping) =>
            CopyToInternal(from, to, state, generatedMaterialMapping, (source, dest) => {
                FileIO.CopyFile(source, dest, overwrite: true);
                return Task.CompletedTask;
            }).GetAwaiter().GetResult();

        public static async Task<bool> CopyToAsync(this MaterialResult from, IMaterialAdapter to, IState state, Dictionary<MapType, string> generatedMaterialMapping) =>
            await CopyToInternal(from, to, state, generatedMaterialMapping, (source, dest) =>
                FileIO.CopyFileAsync(source, dest, overwrite: true));

        static async Task<bool> CopyToInternal(MaterialResult from, IMaterialAdapter to, IState state, Dictionary<MapType, string> generatedMaterialMapping, Func<string, string, Task> fileCopyFunc)
        {
            from.Sanitize();

            var mapsPath = to.AsObject.GetMapsPath();
            if (!AssetDatabase.IsValidFolder(mapsPath))
            {
                mapsPath = AssetDatabase.GUIDToAssetPath(AssetDatabase.CreateFolder(Path.GetDirectoryName(mapsPath), Path.GetFileName(mapsPath)));
                if (string.IsNullOrEmpty(mapsPath))
                {
                    Debug.LogError("Failed to create new folder for material maps.");
                    return false;
                }
            }

            // the material's AI tag
            to.AsObject.EnableGenerationLabel();

            // clear
            foreach (var mapping in generatedMaterialMapping)
                to.SetTexture(mapping.Value, null);

            // set
            foreach (var textureResult in from.textures)
            {
                var key = textureResult.Key;
                if (from.textures.Count == 1 && key == MapType.Preview)
                    key = MapType.Delighted;
                if (!generatedMaterialMapping.TryGetValue(key, out var materialProperty))
                    continue;
                if (!to.HasTexture(materialProperty))
                    continue;
                var sourceFilePath = textureResult.Value.uri.GetLocalPath();
                if (!File.Exists(sourceFilePath))
                {
                    Debug.LogWarning("Source file does not exist: " + sourceFilePath);
                    continue;
                }

                Texture2D importedTexture = null;
                if (materialProperty != GenerationResult.noneMapping)
                {
                    var extension = Path.GetExtension(sourceFilePath);
                    var destFilePath = Path.Combine(mapsPath, $"{materialProperty.TrimStart('_')}{extension}");
                    await fileCopyFunc(sourceFilePath, destFilePath);
                    AssetDatabase.ImportAsset(destFilePath, ImportAssetOptions.ForceUpdate);
                    importedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(destFilePath);

                    // each texture's AI tag
                    importedTexture.EnableGenerationLabel();
                }

                switch (key)
                {
                    case MapType.Normal:
                    {
                        if (importedTexture)
                        {
                            var textureImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(importedTexture)) as TextureImporter;
                            if (textureImporter != null)
                            {
                                textureImporter.textureType = TextureImporterType.NormalMap;
                                textureImporter.sRGBTexture = false;
                                textureImporter.SaveAndReimport();
                            }
                        }

                        break;
                    }
                }
                to.SetTexture(materialProperty, importedTexture);
            }

            if (to.AsObject is Material materialTo)
            {
                // when we set our map and the related color is black, set it to white
                var (foundDelighted, delightedPropertyName) = state.GetTexturePropertyName(to, MapType.Delighted);
                if (foundDelighted && generatedMaterialMapping.ContainsValue(delightedPropertyName) && to.HasTexture(delightedPropertyName) &&
                    to.GetTexture(delightedPropertyName))
                {
                    if (materialTo.HasColor("_BaseColor") && materialTo.GetColor("_BaseColor") == Color.black)
                        materialTo.SetColor("_BaseColor", Color.white);
                }

                // if we are not setting our map and the material doesn't have a map in that property, set the color to black if it was fully white
                else if (generatedMaterialMapping.ContainsValue(delightedPropertyName) && to.HasTexture(delightedPropertyName) &&
                         !to.GetTexture(delightedPropertyName))
                {
                    if (materialTo.HasColor("_BaseColor") && materialTo.GetColor("_BaseColor") == Color.white)
                        materialTo.SetColor("_BaseColor", Color.black);
                }

                // when we set our map and the related color is black, set it to white
                var (foundEmission, emissionPropertyName) = state.GetTexturePropertyName(to, MapType.Emission);
                if (foundEmission && generatedMaterialMapping.ContainsValue(emissionPropertyName) && to.HasTexture(emissionPropertyName) &&
                    to.GetTexture(emissionPropertyName))
                {
                    materialTo.EnableKeyword("_EMISSION");
                    materialTo.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                    if (materialTo.HasColor("_EmissionColor") && materialTo.GetColor("_EmissionColor") == Color.black)
                        materialTo.SetColor("_EmissionColor", Color.white);
                    if (materialTo.HasFloat("_EmissiveExposureWeight"))
                        materialTo.SetFloat("_EmissiveExposureWeight", 0.5f);
                }

                // if we are not setting our map and the material doesn't have a map in that property, set the color to black if it was fully white
                else if (generatedMaterialMapping.ContainsValue(emissionPropertyName) && to.HasTexture(emissionPropertyName) &&
                         !to.GetTexture(emissionPropertyName))
                {
                    if (materialTo.HasColor("_EmissionColor") && materialTo.GetColor("_EmissionColor") == Color.white)
                        materialTo.SetColor("_EmissionColor", Color.black);
                    if (materialTo.HasFloat("_EmissiveExposureWeight"))
                        materialTo.SetFloat("_EmissiveExposureWeight", 1.0f);
                    materialTo.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                    materialTo.DisableKeyword("_EMISSION");
                }
            }

            EditorUtility.SetDirty(to.AsObject);

            return true;
        }

        public static Dictionary<MapType, string> GetDefaultGeneratedMaterialMapping(this IMaterialAdapter material, IState state)
        {
            var mapping = new Dictionary<MapType, string>();
            foreach (MapType mapType in Enum.GetValues(typeof(MapType)))
            {
                if (mapType == MapType.Preview)
                    continue;

                var (found, texturePropertyName) = state.GetTexturePropertyName(material, mapType);
                if (found)
                    mapping[mapType] = texturePropertyName;
            }
            return mapping;
        }

        public static Shader GetCubemapShader() => ShaderUtilities.GetCubemapShader();
    }
}
