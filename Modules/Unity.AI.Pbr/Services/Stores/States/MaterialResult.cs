using System;
using Unity.AI.Pbr.Services.Utilities;
using Unity.AI.Generators.Redux.Toolkit;
using Unity.AI.Toolkit.Utility;

namespace Unity.AI.Pbr.Services.Stores.States
{
    [Serializable]
    enum MapType
    {
        Preview = 0,
        Height = 1,
        Normal = 2,
        Emission = 3,
        Metallic = 4,
        Roughness = 5,
        Delighted = 6,
        Occlusion = 7,
        Smoothness = 8,
        MetallicSmoothness = 9,
        NonMetallicSmoothness = 10,
        MaskMap = 11,
        Edge = 12,
        Base = 13
    }

    [Serializable]
    record MaterialResult
    {
        public SerializableDictionary<MapType, TextureResult> textures = new();

        public static MaterialResult FromPreview(TextureResult preview) => new() { textures = new SerializableDictionary<MapType, TextureResult> { { MapType.Preview, preview } } };
        public static MaterialResult FromPath(string path) => FromPreview(TextureResult.FromPath(path));
        public static MaterialResult FromUrl(string url) => FromPreview(TextureResult.FromUrl(url));

        public Uri uri
        {
            get => this.GetUri();
            set => this.SetUri(value);
        }

        public virtual bool Equals(MaterialResult other)
        {
            if (other is null)
                return false;

            if (ReferenceEquals(this, other))
                return true;

            return uri?.Equals(other.uri) ?? other.uri is null;
        }

        public override int GetHashCode() => uri?.GetHashCode() ?? 0;

        public override string ToString() => uri?.ToString() ?? string.Empty;
    }
}
