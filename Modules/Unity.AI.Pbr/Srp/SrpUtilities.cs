using UnityEngine;

namespace Unity.AI.Pbr.Srp.Utilities
{
    static class SrpUtilities
    {
        public static void SetupPreviewLight(Light light, float intensity) =>
            Generators.IO.Srp.Utilities.SrpUtilities.SetupPreviewLight(light, intensity);
    }
}
