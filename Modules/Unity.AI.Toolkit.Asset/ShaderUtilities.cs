using System.ComponentModel;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Unity.AI.Toolkit.Asset
{
    /// <summary>
    /// Utility class for handling shader operations in the Unity Editor.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    static class ShaderUtilities
    {
        /// <summary>
        /// Finds and returns the built-in Skybox/Cubemap shader.
        /// </summary>
        /// <returns>The Skybox/Cubemap shader, or null if not found.</returns>
        public static Shader GetCubemapShader()
        {
            const string builtinSkybox = "Skybox/Cubemap";

            var builtinShader = Shader.Find(builtinSkybox);
            if (builtinShader != null)
                return builtinShader;

            Debug.LogWarning($"'{builtinSkybox}' was not found.");
            return null;
        }
    }
}
