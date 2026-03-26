using Unity.AI.Assistant.Utils;
using UnityEngine;

namespace Unity.AI.Search.Editor
{
    class PreviewAssetObservation : AssetObservation
    {
        public Texture2D[] previews;

        public override void Dispose()
        {
            if (m_Disposed)
                return;

            if (previews != null)
            {
                foreach (var preview in previews)
                {
                    if (preview != null)
                    {
                        MainThread.DispatchAndForget(() => { Object.DestroyImmediate(preview); });
                    }
                }

                previews = null;
            }

            base.Dispose();
        }
    }
}