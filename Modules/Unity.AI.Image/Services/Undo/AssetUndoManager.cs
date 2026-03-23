using System;
using Unity.AI.Image.Services.SessionPersistence;
using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.Actions.Payloads;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UI.Utilities;

namespace Unity.AI.Image.Services.Undo
{
    [Serializable]
    class AssetUndoManager : AssetUndoManager<TextureResult>
    {
        public AssetUndoManager() => m_OnRestoreAsset += (reference, clipResult) =>
            SharedStore.Store.Dispatch(GenerationResultsActions.setSelectedGeneration, new SelectedGenerationData(reference, clipResult));
    }
}
