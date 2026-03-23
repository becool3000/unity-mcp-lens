using System;
using UnityEngine;

namespace Unity.AI.Generators.UI.Utilities
{
    static class RenderTextureExtensions
    {
        public static bool IsValid(this RenderTexture rt)
        {
            if (rt == null)
                return false;
            if (!rt.IsCreated())
                return false;

            try
            {
                // This will throw if the texture is in an invalid state
                _ = rt.width;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
