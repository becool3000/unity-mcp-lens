using System;
using Unity.AI.Animate.Services.SessionPersistence;
using Unity.AI.Animate.Services.Stores.Actions;
using Unity.AI.Animate.Services.Stores.Actions.Payloads;
using Unity.AI.Animate.Services.Stores.States;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UI.Utilities;

namespace Unity.AI.Animate.Services.Undo
{
    [Serializable]
    class AssetUndoManager : AssetUndoManager<AnimationClipResult>
    {
        public AssetUndoManager() => m_OnRestoreAsset += (reference, clipResult) =>
            SharedStore.Store.Dispatch(GenerationResultsActions.setSelectedGeneration, new PromotedGenerationData(reference, clipResult));
    }
}
