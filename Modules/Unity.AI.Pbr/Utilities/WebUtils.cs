using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Pbr.Services.SessionPersistence;
using Unity.AI.ModelSelector.Services.Stores.Actions.Payloads;
using Unity.AI.Toolkit.Accounts.Services;
using Unity.AI.Toolkit.Accounts.Services.Core;
using UnityEditor;

namespace Unity.AI.Pbr.Services.Utilities
{
    static class WebUtils
    {
        public const string materialEnvironmentKey = "AI_Toolkit_Material_Environment";

        public static string selectedEnvironment => Environment.GetSelectedEnvironment(materialEnvironmentKey);

        [InitializeOnLoadMethod]
        static void RegisterEnvironmentKeys() { Environment.RegisterEnvironmentKey(materialEnvironmentKey, "Material Environment",
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
