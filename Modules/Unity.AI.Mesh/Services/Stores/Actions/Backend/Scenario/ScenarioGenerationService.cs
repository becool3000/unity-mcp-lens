using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.AI.Mesh.Services.Stores.Actions.Payloads;
using Unity.AI.ModelSelector.Services.Stores.States;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.Sdk;
using Unity.AI.Generators.UI.Actions;
using Unity.AI.Generators.UI.AIDropdownIntegrations;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Mesh.Services.Stores.Selectors;
using Unity.AI.Mesh.Services.Stores.States;
using Unity.AI.Mesh.Services.Utilities;
using Unity.AI.Mesh.Windows;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Mesh.Services.Stores.Actions.Backend.Scenario
{
    /// <summary>
    /// Generation service for Scenario AI models supporting multiple 3D generation models
    /// Provides access to Hunyuan3D, Rodin, PartCrafter, Trellis, Tripo, and Direct3D-S2
    /// </summary>
    class ScenarioGenerationService : IGenerationService
    {
        const string k_BaseUrl = "https://api.cloud.scenario.com/v1";

        static string GetAuthHeader()
        {
            var apiCredentials = EditorPrefs.GetString(MeshEditorPreferences.ScenarioBasicAuthKey);

            if (string.IsNullOrEmpty(apiCredentials))
            {
                throw new InvalidOperationException("Scenario.com Basic Auth Token is not set. Please set it in Preferences -> AI -> Asset Generators.");
            }

            return apiCredentials;
        }

        public Task<List<ModelSettings>> GetModelsAsync()
        {
            if (!MeshGeneratorInternals.MeshGeneratorOwnKeyEnabled)
                return Task.FromResult(new List<ModelSettings>());

            var models = new List<ModelSettings>();

            foreach (var scenarioModel in Scenario3DModels.Models)
            {
                var modelSettings = new ModelSettings
                {
                    id = scenarioModel.Id,
                    name = scenarioModel.ModelInfo.Name,
                    description = scenarioModel.ModelInfo.Description,
                    provider = "Scenario",
                    modality = ModelConstants.Modalities.Model3D,
                    operations = DetermineOperations(scenarioModel),
                    tags = GenerateTags(scenarioModel),
                    thumbnails = new List<string>() {Path.GetFullPath("Packages/com.unity.ai.assistant/Modules/Unity.AI.Mesh/Images/Hyper3D.png")},
                    icon = "",
                    isFavorite = false,
                    favoriteProcessing = false,
                    nativeResolution = new ImageDimensions { width = 1024, height = 1024 },
                    imageSizes = new List<ImageDimensions>
                    {
                        new() { width = 1024, height = 1024 }
                    }
                };

                models.Add(modelSettings);
            }

            return Task.FromResult(models);
        }

        public Task QuoteAsync(QuoteMeshesData data, AsyncThunkApi<bool> api)
        {
            try
            {
                var generationSetting = data.generationSetting;
                var modelID = generationSetting.SelectSelectedModelID();

                // Find the corresponding Scenario model
                var scenarioModel = Scenario3DModels.Models.FirstOrDefault(m => m.Id == modelID);
                if (scenarioModel == null)
                {
                    var messages = new[] { $"Scenario model '{modelID}' is not supported." };
                    api.Dispatch(GenerationActions.setGenerationValidationResult,
                        new(data.asset, new(false, BackendServiceConstants.ErrorTypes.Unknown, 0, messages.Select(m => new GenerationFeedbackData(m)).ToList())));
                    return Task.CompletedTask;
                }

                // Validate parameters based on model requirements
                var validationErrors = ValidateModelParameters(scenarioModel, data);
                if (validationErrors.Any())
                {
                    api.Dispatch(GenerationActions.setGenerationValidationResult,
                        new(data.asset, new(false, BackendServiceConstants.ErrorTypes.Unknown, 0, validationErrors.Select(e => new GenerationFeedbackData(e)).ToList())));
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error in Scenario QuoteAsync: {ex.Message}");
                var messages = new[] { $"Failed to get quote: {ex.Message}" };
                api.Dispatch(GenerationActions.setGenerationValidationResult,
                    new(data.asset, new(false, BackendServiceConstants.ErrorTypes.Unknown, 0, messages.Select(m => new GenerationFeedbackData(m)).ToList())));
            }
            return Task.CompletedTask;
        }

        public async Task GenerateAsync(GenerateMeshesData data, AsyncThunkApi<bool> api)
        {
            try
            {
                var asset = new AssetReference { guid = data.asset.guid };
                var modelID = api.State.SelectSelectedModelID(asset);

                var variations = data.generationSetting.SelectVariationCount();
                var progress = new GenerationProgressData(data.progressTaskId, variations, 0f);

                // Find the corresponding Scenario model
                var scenarioModel = Scenario3DModels.Models.FirstOrDefault(m => m.Id == modelID);
                if (scenarioModel == null)
                {
                    Debug.LogError($"Scenario model '{modelID}' is not supported.");
                    api.Dispatch(GenerationResultsActions.removeGeneratedSkeletons, new(data.asset, data.progressTaskId));
                    api.DispatchProgress(data.asset, progress with { progress = 1f }, "Model not supported.");
                    return;
                }

                Debug.Log($"Starting Scenario generation with model: {scenarioModel.ModelInfo.Name}");

                api.Dispatch(GenerationResultsActions.setGeneratedSkeletons,
                    new(data.asset, Enumerable.Range(0, variations).Select(i => new MeshSkeleton(data.progressTaskId, i)).ToList()));
                api.DispatchProgress(data.asset, progress with { progress = 0.1f }, $"Starting generation with {scenarioModel.ModelInfo.Name}...");

                var jobIds = new List<string>();
                try
                {
                    // Build request parameters
                    var requestParameters = BuildRequestParameters(scenarioModel, data);

                    var customSeeds = new List<int>();
                    for (var i = 0; i < variations; i++)
                    {
                        // Start generation job
                        var jobId = await StartGenerationJob(scenarioModel.Id, requestParameters);
                        jobIds.Add(jobId);
                        customSeeds.Add(0);
                    }

                    api.DispatchProgress(data.asset, progress with { progress = 0.25f }, $"Polling Scenario API for completion.");

                    // Dispatch download task to poll for completion
                    var downloadData = new DownloadMeshesData(asset: data.asset, jobIds: jobIds, progressTaskId: data.progressTaskId,
                        uniqueTaskId: Guid.NewGuid(), generationMetadata: data.generationSetting.MakeMetadata(data.asset), customSeeds: customSeeds.ToArray(),
                        autoApply: true, retryable: true);
                    await DownloadAsync(downloadData, api);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Generation failed: {ex.Message}");
                    api.Dispatch(GenerationResultsActions.removeGeneratedSkeletons, new(data.asset, data.progressTaskId));
                    api.DispatchProgress(data.asset, progress with { progress = 1f }, $"Generation failed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error in Scenario GenerateAsync: {ex.Message}");
                var variations = data.generationSetting.SelectVariationCount();
                var progress = new GenerationProgressData(data.progressTaskId, variations, 0f);
                api.Dispatch(GenerationResultsActions.removeGeneratedSkeletons, new(data.asset, data.progressTaskId));
                api.DispatchProgress(data.asset, progress with { progress = 1f }, $"Generation failed: {ex.Message}");
            }
        }

        public async Task DownloadAsync(DownloadMeshesData data, AsyncThunkApi<bool> api)
        {
            using var editorFocus = new EditorAsyncKeepAliveScope("Downloading meshes from Scenario.");
            var variations = data.jobIds.Count;
            var skeletons = Enumerable.Range(0, variations).Select(i => new MeshSkeleton(data.progressTaskId, i)).ToList();
            api.Dispatch(GenerationResultsActions.setGeneratedSkeletons, new(data.asset, skeletons));
            var progress = new GenerationProgressData(data.progressTaskId, variations, 0.25f);
            api.DispatchProgress(data.asset, progress, "Polling Scenario API for completion.");

            var generatedMeshResults = new List<MeshResult>();

            try
            {
                using var httpClientLease = HttpClientManager.instance.AcquireLease();

                // Convert GUIDs to Scenario job IDs
                var jobIds = data.jobIds.Select(guid => guid.ToString()).ToList();

                // Poll each job until completion and download assets
                for (var i = 0; i < jobIds.Count; i++)
                {
                    var jobId = jobIds[i];
                    api.DispatchProgress(data.asset, progress, $"Waiting for model {i + 1}/{jobIds.Count} to complete.");

                    // Poll until job is completed
                    ScenarioJobStatus completedJob = null;
                    const int maxAttempts = 120; // 10 minutes with 5-second intervals
                    const int pollIntervalMs = 5000;
                    var jobSucceeded = false;

                    for (var attempt = 0; attempt < maxAttempts; attempt++)
                    {
                        try
                        {
                            completedJob = await GetJobStatus(jobId);

                            switch (completedJob.status.ToLower())
                            {
                                case "success":
                                    jobSucceeded = true;
                                    break; // Break out of the switch
                                case "failed":
                                case "canceled":
                                    throw new InvalidOperationException($"Scenario job {completedJob.status}: {jobId}");
                                default:
                                    // Continue polling
                                    break;
                            }

                            if (jobSucceeded)
                                break; // Break out of the polling loop

                            await EditorTask.Delay(pollIntervalMs);
                        }
                        catch (Exception ex) when (attempt < maxAttempts - 1)
                        {
                            Debug.LogWarning($"Error polling job status (attempt {attempt + 1}): {ex.Message}");
                            await EditorTask.Delay(pollIntervalMs);
                        }
                    }

                    if (!jobSucceeded)
                        throw new TimeoutException($"Scenario job timed out after {maxAttempts * pollIntervalMs / 1000} seconds");

                    // Download the asset
                    if (completedJob.metadata?.assetIds == null || completedJob.metadata.assetIds.Length == 0)
                    {
                        throw new InvalidOperationException($"No assets generated for job {jobId}");
                    }

                    // Download the first asset (assuming single asset generation for now)
                    var assetId = completedJob.metadata.assetIds[0];
                    var assetUrl = await GetAssetUrl(assetId);

                    // Create MeshResult from URL and download
                    var meshResult = MeshResult.FromUrl(assetUrl);
                    var generativePath = data.asset.GetGeneratedAssetsPath();

                    // Use the Scenario job ID as the unique filename
                    var uniqueFileName = jobId;

                    api.DispatchProgress(data.asset, progress with { progress = 0.75f }, $"Downloading model {i + 1}/{jobIds.Count}...");

                    await meshResult.DownloadToProjectWithUniqueFilename(data.generationMetadata, generativePath, httpClientLease.client, uniqueFileName);

                    var fulfilled = new FulfilledSkeletons(data.asset, new List<FulfilledSkeleton> { new(data.progressTaskId, meshResult.uri.GetAbsolutePath()) });
                    api.Dispatch(GenerationResultsActions.setFulfilledSkeletons, fulfilled);
                    generatedMeshResults.Add(meshResult);
                }

                // Auto-apply first result if asset was blank
                var assetWasBlank = await data.asset.IsBlank();
                if (generatedMeshResults.Count > 0 && assetWasBlank)
                {
                    await api.Dispatch(GenerationResultsActions.selectGeneration, new SelectGenerationData(data.asset, generatedMeshResults[0], true, false));
                    api.Dispatch(GenerationResultsActions.setReplaceWithoutConfirmation, new ReplaceWithoutConfirmationData(data.asset, true));
                }

                api.DispatchProgress(data.asset, progress with { progress = 1f }, "Scenario mesh generation completed.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                api.Dispatch(GenerationResultsActions.removeGeneratedSkeletons, new(data.asset, data.progressTaskId));
                api.DispatchProgress(data.asset, progress with { progress = 1f }, "Download failed.");
                throw;
            }
            finally
            {
                GenerationRecovery.RemoveInterruptedDownload(data);
            }
        }

        /// <summary>
        /// Determines the supported operations based on model parameters
        /// </summary>
        static List<string> DetermineOperations(Scenario3DModel model)
        {
            var operations = new List<string>();

            // Check if model supports text prompts
            if (model.Parameters.ContainsKey("prompt"))
            {
                operations.Add(ModelConstants.Operations.TextPrompt);
            }

            // Check if model supports image inputs
            if (model.Parameters.ContainsKey("image") || model.Parameters.ContainsKey("images") || model.Parameters.ContainsKey("image_url"))
            {
                operations.Add(ModelConstants.Operations.ReferencePrompt);
            }

            // Default to text prompt if no specific operations found
            if (operations.Count == 0)
            {
                operations.Add(ModelConstants.Operations.TextPrompt);
            }

            return operations;
        }

        /// <summary>
        /// Generates tags based on model information and capabilities
        /// </summary>
        static List<string> GenerateTags(Scenario3DModel model)
        {
            var tags = new List<string> { "3D", "mesh", "scenario" };

            // Add developer as tag
            if (!string.IsNullOrEmpty(model.ModelInfo.Developer))
            {
                tags.Add(model.ModelInfo.Developer.ToLower());
            }

            // Add unique features as tags
            if (model.ModelInfo.UniqueFeatures != null)
            {
                tags.AddRange(model.ModelInfo.UniqueFeatures.Select(f => f.ToLower().Replace(" ", "-")));
            }

            // Add specialties as tags
            if (model.ModelInfo.Specialties != null)
            {
                tags.AddRange(model.ModelInfo.Specialties.Select(s => s.ToLower().Replace(" ", "-")));
            }

            // Add generation modes as tags
            if (model.ModelInfo.GenerationModes != null)
            {
                tags.AddRange(model.ModelInfo.GenerationModes.Select(m => m.ToLower().Replace(" ", "-")));
            }

            return tags.Distinct().ToList();
        }

        /// <summary>
        /// Validates model parameters against requirements
        /// </summary>
        static List<string> ValidateModelParameters(Scenario3DModel model, QuoteMeshesData data)
        {
            var errors = new List<string>();

            // Check for required parameters based on model
            foreach (var param in model.Parameters.Where(p => p.Value.Required))
            {
                switch (param.Key.ToLower())
                {
                    case "prompt":
                        // Check if text prompt is provided
                        var hasPrompt = !string.IsNullOrEmpty(data.generationSetting.SelectPrompt());
                        if (!hasPrompt)
                        {
                            errors.Add("Text prompt is required for this model.");
                        }
                        break;
                }
            }

            return errors;
        }

        /// <summary>
        /// Starts a generation job and returns the job ID
        /// </summary>
        static async Task<string> StartGenerationJob(string modelId, Dictionary<string, object> parameters)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{k_BaseUrl}/generate/custom/{modelId}");
            request.Headers.Add("Authorization", GetAuthHeader());

            var jsonContent = JsonConvert.SerializeObject(parameters);
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            using var httpClientLease = HttpClientManager.instance.AcquireLease();
            var response = await httpClientLease.client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var responseObj = JsonConvert.DeserializeObject<ScenarioGenerateResponse>(responseContent);

            if (responseObj?.job?.jobId == null)
            {
                throw new InvalidOperationException("Failed to start generation job - no job ID returned");
            }

            return responseObj.job.jobId;
        }

        /// <summary>
        /// Gets job status from Scenario API
        /// </summary>
        static async Task<ScenarioJobStatus> GetJobStatus(string jobId)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{k_BaseUrl}/jobs/{jobId}");
            request.Headers.Add("Authorization", GetAuthHeader());

            using var httpClientLease = HttpClientManager.instance.AcquireLease();
            var response = await httpClientLease.client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var responseObj = JsonConvert.DeserializeObject<ScenarioJobResponse>(responseContent);

            return responseObj?.job ?? throw new InvalidOperationException("Invalid job status response");
        }

        /// <summary>
        /// Gets asset download URL from Scenario API
        /// </summary>
        static async Task<string> GetAssetUrl(string assetId)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{k_BaseUrl}/assets/{assetId}");
            request.Headers.Add("Authorization", GetAuthHeader());

            using var httpClientLease = HttpClientManager.instance.AcquireLease();
            var response = await httpClientLease.client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var responseObj = JsonConvert.DeserializeObject<ScenarioAssetResponse>(responseContent);

            return responseObj?.asset?.url ?? throw new InvalidOperationException("Asset URL not available");
        }

        /// <summary>
        /// Builds request parameters for Scenario API based on model and generation data
        /// </summary>
        static Dictionary<string, object> BuildRequestParameters(Scenario3DModel model, QuoteMeshesData data)
        {
            var parameters = new Dictionary<string, object>();
            var generationSetting = data.generationSetting;

            // Add common parameters based on model requirements
            foreach (var param in model.Parameters)
            {
                var paramName = param.Key;
                var paramSpec = param.Value;
                object value = null;

                switch (paramName.ToLower())
                {
                    case "prompt":
                        var prompt = generationSetting.SelectPrompt();
                        if (!string.IsNullOrEmpty(prompt))
                        {
                            value = prompt;
                        }
                        else if (paramSpec.Required)
                        {
                            value = "3D model"; // Default fallback
                        }
                        break;

                    case "seed":
                        // Let the service handle random if not specified.
                        // The API will use a random seed if this parameter is omitted.
                        break;

                    default:
                        // Use default value if available and not null
                        if (paramSpec.DefaultValue != null)
                        {
                            value = paramSpec.DefaultValue;
                        }
                        break;
                }

                if (value != null)
                {
                    parameters[paramName] = value;
                }
            }

            return parameters;
        }

        /// <summary>
        /// Builds request parameters for generation (overload for GenerateMeshesData)
        /// </summary>
        static Dictionary<string, object> BuildRequestParameters(Scenario3DModel model, GenerateMeshesData data)
        {
            // Convert GenerateMeshesData to QuoteMeshesData for parameter building
            var quoteData = new QuoteMeshesData(data.asset, data.generationSetting);
            return BuildRequestParameters(model, quoteData);
        }
    }

    /// <summary>
    /// Response classes for Scenario API
    /// </summary>
    [Serializable]
    class ScenarioGenerateResponse
    {
        public int creativeUnitsCost;
        public ScenarioJob job;
    }

    [Serializable]
    class ScenarioJob
    {
        public string createdAt;
        public string jobId;
        public ScenarioJobMetadata metadata;
        public ScenarioStatusHistory[] statusHistory;
        public float progress;
        public string authorId;
        public string jobType;
        public string ownerId;
        public string status;
        public string updatedAt;
    }

    [Serializable]
    class ScenarioJobMetadata
    {
        public object output;
        public object input;
        public string[] assetIds;
    }

    [Serializable]
    class ScenarioStatusHistory
    {
        public string date;
        public string status;
    }

    [Serializable]
    class ScenarioJobResponse
    {
        public ScenarioJobStatus job;
        public int creativeUnitsCost;
    }

    [Serializable]
    class ScenarioJobStatus
    {
        public string jobId;
        public string status;
        public float progress;
        public ScenarioJobMetadata metadata;
    }

    [Serializable]
    class ScenarioAssetResponse
    {
        public ScenarioAsset asset;
    }

    [Serializable]
    class ScenarioAsset
    {
        public string id;
        public string url;
        public string createdAt;
        public object metadata;
    }
}
