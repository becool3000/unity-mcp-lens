using System;
using Unity.AI.Mesh.Services.SessionPersistence;
using Unity.AI.Mesh.Services.Stores.Actions;
using Unity.AI.Mesh.Services.Stores.Actions.Payloads;
using Unity.AI.Mesh.Services.Stores.States;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UI.Utilities;
using UnityEngine;

namespace Unity.AI.Mesh.Services.Undo
{
    [Serializable]
    class AssetUndoManager : AssetUndoManager<MeshResult>
    {
        public AssetUndoManager() => m_OnRestoreAsset += (reference, clipResult) =>
            SharedStore.Store.Dispatch(GenerationResultsActions.setSelectedGeneration, new SelectedGenerationData(reference, clipResult));
    }
}
