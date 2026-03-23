using System;
using Unity.AI.Animate.Services.SessionPersistence;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.Redux.Toolkit;

namespace Unity.AI.Animate.Services.Stores.Selectors
{
    static partial class Selectors
    {
        public static AppData SelectAppData(this IState state) => new()
            {
                sessionSlice = SelectSession(state) with {},
                generationSettingsSlice = SelectGenerationSettings(state) with {},
                generationResultsSlice = SelectGenerationResults(state) with {},
                apiState = state.SelectApiState()
            };
    }
}
