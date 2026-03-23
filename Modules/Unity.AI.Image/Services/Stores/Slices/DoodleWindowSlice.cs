using System.Linq;
using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Generators.Redux;

namespace Unity.AI.Image.Services.Stores.Slices
{
    static class DoodleWindowSlice
    {
        public static void Create(Store store) => store.CreateSlice(
            DoodleWindowActions.slice,
            new DoodleWindowState(),
            reducers => reducers
                .Add(DoodleWindowActions.setSize, (state, payload) => state.size = payload)
                .Add(DoodleWindowActions.setTool, (state, payload) => state.doodleTool = payload)
                .Add(DoodleWindowActions.setBrushSize, (state, payload) => state.brushSize = payload)
                .Add(DoodleWindowActions.setLayer, (state, payload) => state.EnsureLayer(payload.index).SetData(payload.data))
                .Add(DoodleWindowActions.setImageReferenceType, (state, payload) => state.imageReferenceType = payload)
                .Add(DoodleWindowActions.setAssetReference, (state, payload) => state.assetReference = payload)
                .Add(DoodleWindowActions.setLayers, (state, payload) => state.layers = payload.ToList())
                .Add(DoodleWindowActions.setShowBaseImage, (state, payload) => state.showBaseImage = payload)
                .Add(DoodleWindowActions.setBaseImageOpacity, (state, payload) => state.baseImageOpacity = payload)
                .Add(DoodleWindowActions.init).With((state, payload) => payload with {}),
            extraReducers => {},
            state =>
            {
                var newLayers = state.layers.Select(layer => layer with
                {
                    data = layer.data
                }).ToList();
                return state with
                {
                    doodleTool = state.doodleTool,
                    layers = newLayers
                };
            });
    }
}
