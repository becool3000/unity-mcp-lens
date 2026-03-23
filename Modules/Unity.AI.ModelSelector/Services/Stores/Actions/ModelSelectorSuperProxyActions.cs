using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AiEditorToolsSdk;
using AiEditorToolsSdk.Components.Common.Enums;
using AiEditorToolsSdk.Components.GenerativeModels.Requests;
using AiEditorToolsSdk.Components.GenerativeModels.Responses;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.ModelSelector.Services.Stores.States;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.Sdk;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.ModelSelector.Services.Stores.Actions.Payloads;
using Unity.AI.ModelSelector.Services.Stores.Selectors;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Accounts.Services.Sdk;
using Unity.AI.Toolkit.Connect;
using UnityEditor;
using UnityEngine;
using Constants = Unity.AI.Generators.Sdk.Constants;
using Logger = Unity.AI.Toolkit.Accounts.Services.Sdk.Logger;

namespace Unity.AI.ModelSelector.Services.Stores.Actions
{
    static class ModelSelectorSuperProxyActions
    {
        public static ModelSettings FromSuperProxy(PlatformGenerativeModelsResult info, bool isFavorite, bool isAssistantFavorite)
        {
            var model = new ModelSettings
            {
                id = info.GenerativeModelId.ToString(),
                name = info.Name,
                tags = info.Tags,
                description = info.Description,
                provider = info.Provider.ToString(),
                thumbnails = info.ThumbnailsUrls.ToList(),
                icon = info.IconImageUrl,
                modality = info.Modality.ToString(),
                baseModelId = info.BaseModelId == Guid.Empty ? null : info.BaseModelId.ToString(),
                isFavorite = isFavorite,
                operations = info.OperationSubTypes.SelectMany(r => r).Select(ost => ost.ToString()).Distinct().ToList(),
                nativeResolution = new ImageDimensions { width = info.NativeResolutionWidth, height = info.NativeResolutionHeight },
                imageSizes = info.ImageSizes
                    .Select(r => new ImageDimensions { width = r.Width, height = r.Height })
                    .OrderBy(dim => dim.GetSquarenessFactor())
                    .ToList(),
                // capabilities will be set below
            };

            // Set capabilities
            model.capabilities = new List<string>();
            if (isAssistantFavorite)
            {
                model.capabilities.Add(ModelConstants.ModelCapabilities.AssistantFavorite);
            }
            var nameLower = (model.name ?? string.Empty).ToLowerInvariant();
            if (nameLower.StartsWith(k_SeedreamStartsWith))
            {
                model.capabilities.Add(ModelConstants.ModelCapabilities.CustomResolutions);
            }
            if ((nameLower.StartsWith(k_SeedreamStartsWith) || nameLower.StartsWith(k_GeminiStartsWith))
                && model.operations.Contains(ModelConstants.Operations.ReferencePrompt))
            {
                model.capabilities.Add(ModelConstants.ModelCapabilities.EditWithPrompt);
            }
            if (model.modality == ModelConstants.Modalities.Skybox)
                model.capabilities.Add(ModelConstants.ModelCapabilities.SingleInputImage);
            if (nameLower.StartsWith(k_ElevenLabsStartsWith))
                model.capabilities.Add(ModelConstants.ModelCapabilities.SupportsLooping);
            if (nameLower.StartsWith(k_GameUIStartsWith))
                model.capabilities.Add(ModelConstants.ModelCapabilities.Supports9SliceUI);
            if (nameLower.StartsWith(k_KlingStartsWith))
            {
                model.limitations.Add(ModelConstants.ModelLimitations.Requires300PxReference);
                model.limitations.Add(ModelConstants.ModelLimitations.Requires5Or10SecondDuration);
            }

            if (model.thumbnails.Count == 0 && !string.IsNullOrWhiteSpace(model.icon))
                model.thumbnails = new List<string> { model.icon };

            // cache thumbnails immediately
            if (model.thumbnails.Count > 0 && !string.IsNullOrWhiteSpace(model.thumbnails[0]))
            {
                try { _ = TextureCache.GetPreview(new Uri(model.thumbnails[0]), (int)TextureSizeHint.Carousel); }
                catch { /* ignored */ }
            }

            // First and Last frame references for video models with reference prompt support
            if (model.modality == ModelConstants.Modalities.Video && model.operations.Contains(ModelConstants.Operations.ReferencePrompt))
            {
                if (!model.operations.Contains(ModelConstants.Operations.FirstFrameReference))
                    model.operations.Add(ModelConstants.Operations.FirstFrameReference);
                if (!model.operations.Contains(ModelConstants.Operations.LastFrameReference))
                    model.operations.Add(ModelConstants.Operations.LastFrameReference);
            }

            return model;
        }

        const string k_SeedreamStartsWith = "seedream";
        const string k_GeminiStartsWith = "gemini";
        const string k_ElevenLabsStartsWith = "elevenlabs";
        const string k_GameUIStartsWith = "game ui elements (flux)";
        const string k_KlingStartsWith = "kling";

        static readonly List<string> k_PreferredAssistantFavorites = new() { "gpt image", "nano banana", k_SeedreamStartsWith, "standard", "cinematic", "hunyuan", k_ElevenLabsStartsWith, k_GameUIStartsWith, k_GeminiStartsWith };
        static readonly List<string> k_PreferredFavorites = new(k_PreferredAssistantFavorites) { "flux.1 dev", "realistic textures" };

        // While fetching models is safe to do concurrently, we use this mutex as a best-effort
        // mechanism to minimize redundant API calls. A short timeout allows requests to proceed
        // in extreme cases, and frequent cache checks within the method will often prevent
        // redundant work even if the semaphore wait times out.
        static readonly SemaphoreSlim k_Mutex = new(1, 1);

        public static readonly AsyncThunkCreator<DiscoverModelsData, List<ModelSettings>> fetchModels = new($"{ModelSelectorActions.slice}/fetchModelsSuperProxy", async (data, api) =>
        {
            using var editorFocus = new EditorAsyncKeepAliveScope("Fetching models from backend.");

            var taskID = ProgressUtility.Start($"Requesting models.");
            using var progressTokenSource = new CancellationTokenSource();
            List<ModelSettings> models = new();
            var semaphoreAcquired = false;

            try
            {
                // Check cache first
                if (ModelsCache.IsValid(data.environment))
                    return ModelsCache.models;

                const int millisecondsTimeout = 2000;
                semaphoreAcquired = await k_Mutex.WaitAsync(millisecondsTimeout).ConfigureAwaitMainThread();

                // Check cache again after waiting for the mutex
                if (ModelsCache.IsValid(data.environment))
                    return ModelsCache.models;

                SetProgress(0.0f, "Authenticating with UnityConnect.");
                if (!WebUtilities.AreCloudProjectSettingsValid())
                {
                    LogInvalidCloudProjectSettings();
                    api.Cancel();
                    return models;
                }

                // Check cache again after potentially long authentication
                if (ModelsCache.IsValid(data.environment))
                    return ModelsCache.models;

                SetProgress(0.1f, "Preparing request.");

                {
                    var logger = new Logger();

                    using var httpClientLease = HttpClientManager.instance.AcquireLease();
                    var timeout = Constants.modelsFetchTimeout;

                    var builder = Builder.Build(orgId: UnityConnectProvider.organizationKey, userId: UnityConnectProvider.userId,
                        projectId: UnityConnectProvider.projectId, httpClient: httpClientLease.client, baseUrl: data.environment, logger: logger,
                        unityAuthenticationTokenProvider: new AuthenticationTokenProvider(), enableDebugLogging: true, defaultOperationTimeout: timeout, packageInfoProvider: new PackageInfoProvider());
                    var generativeModelsComponent = builder.GenerativeModelsComponent();

                    using var timeoutTokenSource = new CancellationTokenSource(timeout);

                    SetProgress(0.2f, "Requesting model list.");

                    var modelResults = await generativeModelsComponent.GetGenerativeModelsList(timeoutOverride: timeout, cancellationToken: timeoutTokenSource.Token);

                    // Check cache again after potentially long request
                    if (ModelsCache.IsValid(data.environment))
                        return ModelsCache.models;

                    SetProgress(0.4f, "Finishing model list request.");
                    if (timeoutTokenSource.IsCancellationRequested)
                        throw new OperationCanceledException();

                    SetProgress(0.5f, "Validating model list.");

                    if (!modelResults.Result.IsSuccessful)
                    {
                        if (modelResults.Result.Error.Errors.Count == 0)
                            Debug.Log($"Error reason is '{modelResults.Result.Error.AiResponseError.ToString()}' and no additional error information was provided ({data.environment}).");
                        else
                            modelResults.Result.Error.Errors.ForEach(e => Debug.Log($"{modelResults.Result.Error.AiResponseError.ToString()}: {e}"));

                        // we can simply return without throwing or additional logging because the error is already logged
                        return models;
                    }

                    SetProgress(0.6f, "Requesting model favorites.");

                    var favoritesResults = await generativeModelsComponent.GetGenerativeModelsFavoritesList(timeoutOverride: timeout, cancellationToken: timeoutTokenSource.Token);
                    SetProgress(0.7f, "Finishing model favorites request.");
                    if (timeoutTokenSource.IsCancellationRequested)
                        throw new OperationCanceledException();

                    SetProgress(0.8f, "Validating model favorites.");

                    var favoritesSuccessful = favoritesResults.Result.IsSuccessful;
                    if (!favoritesSuccessful)
                    {
                        if (favoritesResults.Result.Error.Errors.Count == 0)
                            Debug.Log($"Error reason is '{favoritesResults.Result.Error.AiResponseError.ToString()}' and no additional error information was provided ({data.environment}).");
                        else
                        {
                            if (modelResults.Result.Error.AiResponseError is AiResultErrorEnum.None or AiResultErrorEnum.Unknown && logger.LastException != null)
                                Debug.LogException(logger.LastException);
                            favoritesResults.Result.Error.Errors.ForEach(e => Debug.Log($"{favoritesResults.Result.Error.AiResponseError.ToString()}: {e}"));
                        }

                        // do not return here, we can still use the models
                    }

                    SetProgress(0.9f, "Parsing model list.");

                    foreach (var modelResult in modelResults.Result.Value)
                    {
                        if (modelResult.ModelType != ModelTypeEnum.Base && (modelResult.Modality != ModalityEnum.None || modelResult.Provider == ProviderEnum.None))
                        {
                            var isUserFavorite = favoritesSuccessful && favoritesResults.Result.Value
                                .Any(f => f.GenerativeModelId == modelResult.GenerativeModelId);

                            var isFavorite = isUserFavorite ||
                                k_PreferredFavorites.Any(preferred => modelResult.Name.ToLower().StartsWith(preferred));
                            var isAssistantFavorite = isUserFavorite ||
                                k_PreferredAssistantFavorites.Any(preferred => modelResult.Name.ToLower().StartsWith(preferred)) ||
                                modelResult.Provider != ProviderEnum.Unity && modelResult.Modality != ModalityEnum.Image && modelResult.Modality != ModalityEnum.Skybox;

                            models.Add(FromSuperProxy(modelResult, isFavorite, isAssistantFavorite));
                        }
                    }

                    // Update cache only if both operations were successful
                    if (favoritesSuccessful)
                        ModelsCache.UpdateCache(models, data.environment);
                }
            }
            finally
            {
                if (semaphoreAcquired)
                    k_Mutex.Release();

                progressTokenSource.Cancel();
                SetProgress(1, models.Count > 0 ? $"Retrieved {models.Count} models." : "Failed to retrieve models.");
                ProgressUtility.Finish(taskID);
            }

            return models;

            void SetProgress(float progress, string description)
            {
                if (taskID > 0)
                    Progress.Report(taskID, progress, description);
            }

            void LogInvalidCloudProjectSettings() =>
                Debug.Log($"Error reason is 'Invalid Unity Cloud configuration': Could not obtain organizations for user \"{UnityConnectProvider.userName}\".");
        });

        public static readonly AsyncThunkCreator<(FavoriteModelPayload,string), bool> setModelFavorite = new($"{ModelSelectorActions.slice}/setModelFavoriteSuperProxy", async (arg, api) =>
        {
            var (payload, environment) = arg;

            if (string.IsNullOrEmpty(payload.modelId))
                return false;

            using var httpClientLease = HttpClientManager.instance.AcquireLease();
            var builder = Builder.Build(orgId: UnityConnectProvider.organizationKey, userId: UnityConnectProvider.userId,
                projectId: UnityConnectProvider.projectId, httpClient: httpClientLease.client, baseUrl: environment, logger: new Logger(),
                unityAuthenticationTokenProvider: new AuthenticationTokenProvider(), enableDebugLogging: true, packageInfoProvider: new PackageInfoProvider());
            var generativeModelsComponent = builder.GenerativeModelsComponent();

            var res = await generativeModelsComponent.UpdateGenerativeModelsFavorite(new GenerativeModelsFavoritesRequest
            {
                GenerativeModelId = Guid.Parse(payload.modelId),
                ModelOperation = payload.isFavorite ? ModelOperationEnum.Favorite : ModelOperationEnum.Unfavorite
            });
            if (!res.Result.IsSuccessful)
            {
                if (res.Result.Error.Errors.Count == 0)
                {
                    if (Unsupported.IsDeveloperMode())
                        Debug.Log($"Error reason is '{res.Result.Error.AiResponseError.ToString()}' and no additional error information was provided ({environment}).");
                }
                else
                    res.Result.Error.Errors.ForEach(e => Debug.Log($"{res.Result.Error.AiResponseError.ToString()}: {e}"));
            }

            return res.Result.IsSuccessful;
        });
    }

    static class ImageDimensionsExtensions
    {
        public static double GetSquarenessFactor(this ImageDimensions dimensions)
        {
            if (dimensions.width == 0 || dimensions.height == 0)
                return double.MaxValue;

            double w = dimensions.width;
            double h = dimensions.height;

            // Calculate the ratio such that it's always >= 1
            // For example, 100x200 (ratio 0.5) and 200x100 (ratio 2.0)
            // both become 2.0 using this method. A 100x100 square becomes 1.0.
            return Math.Max(w / h, h / w);
        }
    }

    static class ModelsCache
    {
        public static List<ModelSettings> models { get; private set; }
        public static DateTime cacheTimestamp { get; private set; }
        public static string environment { get; private set; }

        public static bool IsValid(string env)
        {
            if (models == null || environment != env)
                return false;

            var cacheAge = DateTime.Now - cacheTimestamp;
            return cacheAge.TotalSeconds <= ModelSelectorSelectors.timeToLiveGlobally;
        }

        public static void UpdateCache(IEnumerable<ModelSettings> currentModels, string env)
        {
            models = new List<ModelSettings>(currentModels);
            environment = env;
            cacheTimestamp = DateTime.Now;
        }
    }
}
