using UnityEngine;

namespace Unity.AI.Generators.UI.Utilities
{
    static class FrameCacheUtils
    {
        public static void SafeBlit(Texture source, RenderTexture dest)
        {
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, dest);
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }
    }
}
