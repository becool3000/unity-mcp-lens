using System;
using Unity.AI.Sound.Services.SessionPersistence;
using Unity.AI.Sound.Services.Stores.Actions;
using Unity.AI.Sound.Services.Stores.Actions.Payloads;
using Unity.AI.Sound.Services.Stores.States;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UI.Utilities;
using UnityEngine;

namespace Unity.AI.Sound.Services.Undo
{
    [Serializable]
    class AssetUndoManager : AssetUndoManager<AudioClipResult>
    {
        public AssetUndoManager() => m_OnRestoreAsset += (reference, clipResult) =>
            SharedStore.Store.Dispatch(GenerationResultsActions.setSelectedGeneration, new SelectedGenerationData(reference, clipResult));
    }
}
