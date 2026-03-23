using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Generators.IO.Utilities
{
    static class UnityObjectExtensions
    {
        public static void SafeDestroy(this Object unityObject)
        {
            if (!unityObject)
                return;

            if (Application.isPlaying)
                Object.Destroy(unityObject);
            else
                Object.DestroyImmediate(unityObject);
        }
    }
}
