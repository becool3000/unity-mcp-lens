using System;
using Unity.AI.ModelSelector.Services.SessionPersistence;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.Redux.Toolkit;

namespace Unity.AI.ModelSelector.Services.Stores.Selectors
{
    static partial class ModelSelectorSelectors
    {
        public static AppData SelectAppData(this IState state) => new()
            {
                modelSelectorSlice = state.SelectModels() with {},
                apiState = state.SelectApiState()
            };
    }
}
