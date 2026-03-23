using System;
using Unity.AI.Generators.UI.Actions;
using UnityEditor;

namespace Unity.AI.ModelSelector.Services.Utilities
{
    static class WebUtils
    {
        [InitializeOnLoadMethod]
        static void RegisterEnvironmentKeys() => GenerationActions.selectedEnvironment = api => Stores.Selectors.ModelSelectorSelectors.SelectEnvironment(api.State);
    }
}
