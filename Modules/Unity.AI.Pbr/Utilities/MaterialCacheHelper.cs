using System;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Pbr.Services.Stores.States;
using Unity.AI.Generators.UI.Utilities;

namespace Unity.AI.Pbr.Services.Utilities
{
    static class MaterialCacheHelper
    {
        /// <summary>
        /// Checks if all textures associated with a given material are already cached.
        /// For materials that represent .mat files (i.e. IsMat() returns true)
        /// this method returns true.
        /// </summary>
        /// <param name="material">The material to check.</param>
        /// <returns>True if there’s nothing to load; otherwise, false.</returns>
        public static bool Peek(MaterialResult material)
        {
            // If the material represents a .mat file, then it has no textures to load.
            if (material.IsMat())
                return true;

            // Check each texture: if any texture is not cached, return false.
            return material.textures.All(kvp => TextureCache.Peek(kvp.Value.uri));
        }

        /// <summary>
        /// Precache (load) all textures associated with a given material.
        /// If the texture is already present in the cache (peeked) it will be skipped.
        /// Uses a maximum concurrency of 4 requests at a time.
        /// </summary>
        /// <param name="material">The material whose textures will be loaded.</param>
        public static async Task Precache(MaterialResult material)
        {
            // No textures to preload if material represents .mat.
            if (material.IsMat())
                return;

            foreach (var kvp in material.textures)
            {
                // Skip if the texture is already cached
                if (TextureCache.Peek(kvp.Value.uri))
                    continue;

                // Launch the appropriate load request based on map type.
                _ = kvp.Key is MapType.Normal
                    ? await TextureCache.GetNormalMap(kvp.Value.uri)
                    : await TextureCache.GetTexture(kvp.Value.uri);
            }
        }
    }
}
