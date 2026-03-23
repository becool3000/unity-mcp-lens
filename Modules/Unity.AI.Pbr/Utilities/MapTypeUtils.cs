using System;
using Unity.AI.Pbr.Services.Stores.States;

namespace Unity.AI.Pbr.Services.Utilities
{
    static class MapTypeUtils
    {
        public static MapType Parse(string mapType)
        {
            if (mapType == null)
                throw new ArgumentNullException(nameof(mapType));

            return mapType.ToLowerInvariant() switch
            {
                "preview" => MapType.Preview,
                "height" => MapType.Height,
                "normal" => MapType.Normal,
                "emission" => MapType.Emission,
                "metallic" => MapType.Metallic,
                "roughness" => MapType.Roughness, // 1P model uses roughness instead of smoothness but with smoothness (1 - v) values
                "delighted" => MapType.Delighted,
                "occlusion" => MapType.Occlusion,
                "ambient-occlusion" => MapType.Occlusion,
                "smoothness" => MapType.Smoothness,
                "metallicsmoothness" => MapType.MetallicSmoothness,
                "nonmetallicsmoothness" => MapType.NonMetallicSmoothness,
                "maskmap" => MapType.MaskMap,
                "edge" => MapType.Edge,
                "base" => MapType.Base,
                _ => throw new ArgumentOutOfRangeException(nameof(mapType), mapType, "Invalid map type")
            };
        }
    }
}
