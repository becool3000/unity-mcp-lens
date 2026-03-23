using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Mesh.Services.SessionPersistence;
using Unity.AI.ModelSelector.Services.Stores.Actions.Payloads;
using Unity.AI.Toolkit.Accounts.Services;
using Unity.AI.Toolkit.Accounts.Services.Core;
using UnityEditor;

namespace Unity.AI.Mesh.Services.Utilities
{
    static class WebUtils
    {
        public const string meshEnvironmentKey = "AI_Toolkit_Mesh_Environment";

        public static string selectedEnvironment => Environment.GetSelectedEnvironment(meshEnvironmentKey);

        [InitializeOnLoadMethod]
        static void RegisterEnvironmentKeys()
        {
            Environment.RegisterEnvironmentKey(meshEnvironmentKey, "Mesh Environment",
                _ => SharedStore.Store.Dispatch(ModelSelector.Services.Stores.Actions.ModelSelectorActions.discoverModels,
                    new DiscoverModelsData(selectedEnvironment)));

            // both the subscriber and the publisher are pure C# code, the subscription will not accumulate across domain reloads
            Account.settings.OnChange += () => {
                if (!Account.settings.AiGeneratorsEnabled)
                    return;

                SharedStore.Store.Dispatch(ModelSelector.Services.Stores.Actions.ModelSelectorActions.discoverModels,
                    new DiscoverModelsData(selectedEnvironment));
            };
        }
    }
}
