using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Search.Editor
{
    /// <summary>
    /// Inputs gathered from inspecting an asset.
    /// </summary>
    record AssetObservation : IDisposable
    {
        public string assetGuid;
        public Texture2D[] previews;

        public void CleanUpTextures()
        {
            if (previews != null)
            {
                foreach (var preview in previews)
                {
                    if (preview != null)
                        Object.DestroyImmediate(preview);
                }

                previews = null;
            }
        }

        public void Dispose()
        {
            CleanUpTextures();
        }

        ~AssetObservation()
        {
            Dispose();
        }
    }
}