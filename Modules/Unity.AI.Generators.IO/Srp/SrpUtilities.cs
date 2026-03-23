using UnityEngine;
#if HDRP_PRESENT
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
#endif

namespace Unity.AI.Generators.IO.Srp.Utilities
{
    static class SrpUtilities
    {
        public static void SetupPreviewLight(Light light, float intensity)
        {
#if HDRP_PRESENT
            var renderPipeline = GraphicsSettings.currentRenderPipeline ?? GraphicsSettings.defaultRenderPipeline;
            if (renderPipeline is HDRenderPipelineAsset)
            {
                if (!light.gameObject.GetComponent<HDAdditionalLightData>())
                    light.gameObject.AddComponent<HDAdditionalLightData>();
                light.intensity = intensity * 4;
            }
            else
                light.intensity = intensity;
#else
            light.intensity = intensity;
#endif
        }
    }
}
