using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Search.Editor.Services;
using UnityEngine;

namespace Unity.AI.Search.Editor.Knowledge
{
    abstract class AssetDescriptor
    {
        public abstract string Version { get; }
        
        public abstract IModelService Model { get; }
        
        public abstract Task<AssetObservation> ProcessAsync(Object assetObject, CancellationToken cancellationToken);

        internal static bool RetainPreviews = false;
    }
}
