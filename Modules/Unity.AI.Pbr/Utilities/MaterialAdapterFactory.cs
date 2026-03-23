using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

namespace Unity.AI.Pbr.Services.Utilities
{
    static class MaterialAdapterFactory
    {
        public static IMaterialAdapter Create(Material material) => new MaterialAdapter(material);

        public static IMaterialAdapter Create(TerrainLayer terrainLayer) => new TerrainLayerAdapter(terrainLayer);

        public static IMaterialAdapter Create(UnityEngine.Object obj)
        {
            return obj switch
            {
                Material material => Create(material),
                TerrainLayer terrainLayer => Create(terrainLayer),
                _ => new InvalidMaterialAdapter()
            };
        }

        public static bool IsSupportedAssetType(Type assetType)
        {
            return assetType != null &&
                  (typeof(Material).IsAssignableFrom(assetType) ||
                   typeof(TerrainLayer).IsAssignableFrom(assetType));
        }

        public static bool IsSupportedAssetAtPath(string path)
        {
            var assetType = AssetDatabase.GetMainAssetTypeAtPath(path);
            return IsSupportedAssetType(assetType);
        }
    }

    interface IMaterialAdapter
    {
        bool HasTexture(string propertyName);
        Texture GetTexture(string propertyName);
        string[] GetTexturePropertyNames();
        void SetTexture(string propertyName, Texture texture);
        UnityEngine.Object AsObject { get; }
        IMaterialAdapter ConvertToSkybox();
        bool IsValid { get; }
        string Shader { get; }
        static readonly IReadOnlyDictionary<string, string> emptyDictionary = new Dictionary<string, string>();
        IReadOnlyDictionary<string, string> MapShaderNameToDescription();
    }

    readonly struct InvalidMaterialAdapter : IMaterialAdapter
    {
        public string ProviderName => "Invalid Material";
        public UnityEngine.Object AsObject => null;
        public IMaterialAdapter ConvertToSkybox() => null;
        public bool IsValid => false;
        public bool HasTexture(string propertyName) => false;
        public Texture GetTexture(string propertyName) => null;
        public string[] GetTexturePropertyNames() => Array.Empty<string>();
        public void SetTexture(string propertyName, Texture texture) { }
        public string Shader => "Invalid";
        public IReadOnlyDictionary<string, string> MapShaderNameToDescription() => IMaterialAdapter.emptyDictionary;
    }

    readonly struct MaterialAdapter : IMaterialAdapter
    {
        readonly Material m_Material;

        public string ProviderName => m_Material != null ? m_Material.name : "Null Material";

        public MaterialAdapter(Material material) => m_Material = material;

        public bool HasTexture(string propertyName) => m_Material != null && m_Material.HasProperty(propertyName);

        public Texture GetTexture(string propertyName) => HasTexture(propertyName) ? m_Material.GetTexture(propertyName) : null;

        public string[] GetTexturePropertyNames() => m_Material == null ? Array.Empty<string>() : m_Material.GetTexturePropertyNames();

        public void SetTexture(string propertyName, Texture texture)
        {
            if (m_Material)
                m_Material.SetTexture(propertyName, texture);
        }

        public UnityEngine.Object AsObject => m_Material;

        public IMaterialAdapter ConvertToSkybox()
        {
            var cubemapShader = MaterialUtilities.GetCubemapShader();
            if (cubemapShader != null && m_Material.shader != cubemapShader)
                m_Material.shader = cubemapShader;
            return this;
        }

        public bool IsValid => m_Material != null && m_Material.shader != null;

        public string Shader => m_Material?.shader != null ? m_Material.shader.name : "Invalid";

        public IReadOnlyDictionary<string, string> MapShaderNameToDescription()
        {
            if (!m_Material || !m_Material.shader)
                return IMaterialAdapter.emptyDictionary;

            var shaderTextureProperties = new Dictionary<string, string>();
            var shader = m_Material.shader;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                if (shader.GetPropertyType(i) == ShaderPropertyType.Texture)
                {
                    shaderTextureProperties.TryAdd(shader.GetPropertyName(i), shader.GetPropertyDescription(i));
                }
            }

            return shaderTextureProperties;
        }

        public static implicit operator bool(MaterialAdapter provider) => provider.m_Material != null;
    }

    readonly struct TerrainLayerAdapter : IMaterialAdapter
    {
        readonly TerrainLayer m_TerrainLayer;

        static readonly Dictionary<string, int> k_PropertyMap = new()
        {
            { "_Diffuse", 0 },
            { "_NormalMap", 1 },
            { "_MaskMap", 2 }
        };

        static readonly Dictionary<string, string> k_PropertyShaderNameMap = new()
        {
            { "_Diffuse", "Diffuse" },
            { "_NormalMap", "Normal Map" },
            { "_MaskMap", "Mask Map" }
        };

        public string ProviderName => m_TerrainLayer != null ? m_TerrainLayer.name : "Null TerrainLayer";

        public UnityEngine.Object AsObject => m_TerrainLayer;
        public IMaterialAdapter ConvertToSkybox() => null;

        public TerrainLayerAdapter(TerrainLayer terrainLayer) => m_TerrainLayer = terrainLayer;

        public bool HasTexture(string propertyName) => m_TerrainLayer != null && k_PropertyMap.ContainsKey(propertyName);

        public Texture GetTexture(string propertyName)
        {
            if (m_TerrainLayer == null || !k_PropertyMap.TryGetValue(propertyName, out var value))
                return null;

            return value switch
            {
                0 => m_TerrainLayer.diffuseTexture,
                1 => m_TerrainLayer.normalMapTexture,
                2 => m_TerrainLayer.maskMapTexture,
                _ => (Texture)null
            };
        }

        public string[] GetTexturePropertyNames() => k_PropertyMap.Keys.ToArray();

        public void SetTexture(string propertyName, Texture texture)
        {
            if (m_TerrainLayer == null || !k_PropertyMap.TryGetValue(propertyName, out var value))
                return;

            switch (value)
            {
                case 0:
                    m_TerrainLayer.diffuseTexture = texture as Texture2D;
                    break;
                case 1:
                    m_TerrainLayer.normalMapTexture = texture as Texture2D;
                    break;
                case 2:
                    m_TerrainLayer.maskMapTexture = texture as Texture2D;
                    break;
            }
        }

        public bool IsValid => m_TerrainLayer != null;

        public string Shader => "Nature/Terrain/Standard";
        public IReadOnlyDictionary<string, string> MapShaderNameToDescription()
        {
            if (!m_TerrainLayer)
                return IMaterialAdapter.emptyDictionary;

            return k_PropertyShaderNameMap;
        }

        public static implicit operator bool(TerrainLayerAdapter provider) => provider.m_TerrainLayer != null;
    }
}
