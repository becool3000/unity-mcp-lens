using System;
using System.Linq;
using AiEditorToolsSdk.Components.Asset.Responses;
using AiEditorToolsSdk.Components.Common.Enums;
using AiEditorToolsSdk.Components.Common.Responses.OperationResponses;
using AiEditorToolsSdk.Components.Common.Responses.Wrappers;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Accounts.Services.Sdk;
using Unity.AI.Toolkit.Asset;
using Unity.AI.Toolkit.Connect;
using UnityEditor;
using UnityEngine;
using Logger = Unity.AI.Toolkit.Accounts.Services.Sdk.Logger;

namespace Unity.AI.Generators.UI.Actions
{
    static class GenerationActions
    {
        public static Creator<AssetReference> initializeAsset => Asset.VisualElementExtensions.initializeAsset;

        public static readonly string resultsSlice = "generationResults";
        public static Creator<GenerationAllowedData> setGenerationAllowed => new($"{resultsSlice}/setGenerationAllowed");
        public static Creator<GenerationsProgressData> setGenerationProgress => new($"{resultsSlice}/setGenerationProgress");
        public static Creator<GenerationsFeedbackData> addGenerationFeedback => new($"{resultsSlice}/addGenerationFeedback");
        public static Creator<AssetReference> removeGenerationFeedback => new($"{resultsSlice}/removeGenerationFeedback");
        public static Creator<GenerationsValidationResult> setGenerationValidationResult => new($"{resultsSlice}/setGenerationValidationResult");
        public static Creator<AsssetContext> pruneFulfilledSkeletons => new($"{resultsSlice}/pruneFulfilledSkeletons");

        public static Func<IStoreApi, string> selectedEnvironment = null;

        public static void DispatchProgress(this IStoreApi api, AssetReference asset, GenerationProgressData payload, string description, bool backgroundReport = false)
        {
            if (backgroundReport)
                EditorAsyncKeepAliveScope.ShowProgressOrCancelIfUnfocused("Editor background worker", description, payload.progress);

            if (payload.taskID > 0)
                Progress.Report(payload.taskID, payload.progress, description);
            api.Dispatch(setGenerationProgress, new GenerationsProgressData(asset, payload));
        }

        public static bool DispatchStoreAssetMessage(this IStoreApi api, AssetReference asset, OperationResult<BlobAssetResult> assetResults,
            out Guid assetGuid)
        {
            assetGuid = Guid.Empty;
            if (!assetResults.Result.IsSuccessful)
            {
                DispatchFailedMessage(api, asset, assetResults.Result.Error, assetResults.W3CTraceId);
                Debug.Log($"Trace Id {assetResults.SdkTraceId} => {assetResults.W3CTraceId}");

                // caller can simply return without throwing or additional logging because the error is already logged and we rely on 'finally' statements for cleanup
                return false;
            }
            assetGuid = assetResults.Result.Value.AssetId;
            if (LoggerUtilities.sdkLogLevel == 0)
                return true;
            if (assetResults.Result.Value.Ttl.HasValue)
                Debug.Log($"Asset {assetGuid} has ttl {assetResults.Result.Value.Ttl}");
            return true;
        }

        public static void DispatchValidatingUserMessage(this IStoreApi api, AssetReference asset)
        {
            var messages = new[] { "Validating user, project and organization..." };
            api.Dispatch(setGenerationValidationResult,
                new(asset,
                    new(false, BackendServiceConstants.ErrorTypes.Unknown, 0,
                        messages.Select(m => new GenerationFeedbackData(m)).ToList())));
        }

        public static void DispatchValidatingMessage(this IStoreApi api, AssetReference asset)
        {
            var messages = new[] { "Validating generation inputs..." };
            api.Dispatch(setGenerationValidationResult,
                new(asset,
                    new(false, BackendServiceConstants.ErrorTypes.Unknown, 0,
                        messages.Select(m => new GenerationFeedbackData(m)).ToList())));
        }

        static void AppendSupportInfo(ref string message, string w3CTraceId, string selectedEnv)
        {
            if (!string.IsNullOrEmpty(w3CTraceId) || !string.IsNullOrEmpty(selectedEnv))
            {
                message += " If this persists, please contact support with this information.";
                if (!string.IsNullOrEmpty(selectedEnv))
                    message += $" Server URL: {selectedEnv}";
                if (!string.IsNullOrEmpty(w3CTraceId))
                    message += $" W3CTraceId: {w3CTraceId}";
            }
        }

        public static void DispatchInvalidCloudProjectMessage(this IStoreApi api, AssetReference asset)
        {
            api.Dispatch(setGenerationAllowed, new(asset, true));
            var selectedEnv = string.Empty;
            if (selectedEnvironment != null)
                selectedEnv = selectedEnvironment(api);

            var message = $"Could not obtain generators access for user \"{UnityConnectProvider.userName}\".";
            AppendSupportInfo(ref message, null, selectedEnv);

            Debug.Log(message);
            api.Dispatch(addGenerationFeedback, new GenerationsFeedbackData(asset, new GenerationFeedbackData(message)));
        }

        public static void DispatchReferenceUploadFailedMessage(this IStoreApi api, AssetReference asset, string w3CTraceId)
        {
            api.Dispatch(setGenerationAllowed, new(asset, true));
            var selectedEnv = string.Empty;
            if (selectedEnvironment != null)
                selectedEnv = selectedEnvironment(api);

            var message = "Could not upload references. Please try again.";
            AppendSupportInfo(ref message, w3CTraceId, selectedEnv);

            Debug.Log(message);
            api.Dispatch(addGenerationFeedback, new GenerationsFeedbackData(asset, new GenerationFeedbackData(message)));
        }

        public static void DispatchClientGenerationAttemptFailedMessage(this IStoreApi api, AssetReference asset)
        {
            api.Dispatch(setGenerationAllowed, new(asset, true));

            const string message = "Client could not start generation request. Please try again.";

            Debug.Log(message);
            api.Dispatch(addGenerationFeedback, new GenerationsFeedbackData(asset, new GenerationFeedbackData(message)));
        }

        public static void DispatchClientGenerationRequestFailedMessage(this IStoreApi api, AssetReference asset)
        {
            api.Dispatch(setGenerationAllowed, new(asset, true));

            const string message = "Client could not complete generation request. Please try again.";

            Debug.Log(message);
            api.Dispatch(addGenerationFeedback, new GenerationsFeedbackData(asset, new GenerationFeedbackData(message)));
        }

        public static void DispatchGenerationRequestFailedMessage(this IStoreApi api, AssetReference asset, string w3CTraceId)
        {
            api.Dispatch(setGenerationAllowed, new(asset, true));
            var selectedEnv = string.Empty;
            if (selectedEnvironment != null)
                selectedEnv = selectedEnvironment(api);

            var message = "Could not make generation request. Please try again.";
            AppendSupportInfo(ref message, w3CTraceId, selectedEnv);

            Debug.LogError(message);
            api.Dispatch(addGenerationFeedback, new GenerationsFeedbackData(asset, new GenerationFeedbackData(message)));
        }

        public static void DispatchFailedBatchMessage<T>(this IStoreApi api, AssetReference asset, BatchOperationResult<T> results) where T : class
        {
            if (!results.Batch.IsSuccessful)
                DispatchFailedMessage(api, asset, results.Batch.Error, results.W3CTraceId);
            Debug.Log($"Trace Id '{results.SdkTraceId}' => W3CTraceId '{results.W3CTraceId}'");
        }

        public static void DispatchFailedMessage(this IStoreApi api, AssetReference asset, AiOperationFailedResult result, string w3CTraceId)
        {
            var selectedEnv = string.Empty;
            if (selectedEnvironment != null)
                selectedEnv = selectedEnvironment(api);

            api.Dispatch(setGenerationAllowed, new(asset, true));
            var messages = result.Errors.Count == 0
                ? new[] { $"Received '{result.AiResponseError.ToString()}' from server." }
                : result.Errors.Distinct().Select(m => $"{result.AiResponseError.ToString()}: {m}").ToArray();
            foreach (var message in messages)
            {
                var fullMessage = message;
                AppendSupportInfo(ref fullMessage, w3CTraceId, selectedEnv);
                Debug.Log(fullMessage);
                api.Dispatch(addGenerationFeedback, new GenerationsFeedbackData(asset, new GenerationFeedbackData(fullMessage)));
            }
        }

        public static void DispatchFailedDownloadMessage<T>(this IStoreApi api, AssetReference asset, OperationResult<T> result, string lastW3CTraceId = null, bool willRetry = false) where T : class
        {
            if (result == null)
            {
                if (!willRetry)
                    DispatchFailedDownloadMessage(api, asset, new AiOperationFailedResult(AiResultErrorEnum.Unknown), lastW3CTraceId);
                return;
            }

            if (!result.Result.IsSuccessful && !willRetry)
                DispatchFailedDownloadMessage(api, asset, result.Result.Error, !string.IsNullOrWhiteSpace(result.W3CTraceId) ? result.W3CTraceId : lastW3CTraceId);
            Debug.Log($"Trace Id '{result.SdkTraceId}' => W3CTraceId '{(!string.IsNullOrWhiteSpace(result.W3CTraceId) ? result.W3CTraceId : lastW3CTraceId)}'");
        }

        public static bool DispatchSingleFailedDownloadMessage<T>(this IStoreApi api, AssetReference asset, OperationResult<T> result, string lastW3CTraceId = null, bool willRetry = false) where T : class
        {
            if (result == null)
            {
                if (!willRetry)
                {
                    DispatchFailedDownloadMessage(api, asset, new AiOperationFailedResult(AiResultErrorEnum.Unknown), lastW3CTraceId);
                    return false;
                }

                return true;
            }

            if (!result.Result.IsSuccessful && !willRetry)
            {
                if (result.Result.Error.AiResponseError != AiResultErrorEnum.SdkTimeout &&
                    result.Result.Error.AiResponseError != AiResultErrorEnum.ServerTimeout)
                {
                    DispatchFilteredDownloadMessage(api, asset, result.Result.Error, string.IsNullOrEmpty(result.W3CTraceId) ? lastW3CTraceId : result.W3CTraceId);
                    return true;
                }

                DispatchFailedDownloadMessage(api, asset, result.Result.Error, string.IsNullOrEmpty(result.W3CTraceId) ? lastW3CTraceId : result.W3CTraceId);
            }

            Debug.Log($"Trace Id '{result.SdkTraceId}' => W3CTraceId '{(!string.IsNullOrWhiteSpace(result.W3CTraceId) ? result.W3CTraceId : lastW3CTraceId)}'");
            return false;
        }

        public static void DispatchFailedDownloadMessage(this IStoreApi api, AssetReference asset, AiOperationFailedResult result, string w3cTraceId = null)
        {
            var selectedEnv = string.Empty;
            if (selectedEnvironment != null)
                selectedEnv = selectedEnvironment(api);

            var messages = result.Errors.Count == 0
                ? new[] { $"Received '{result.AiResponseError.ToString()}' from server." }
                : result.Errors.Distinct().Select(m => $"{result.AiResponseError.ToString()}: {m}").ToArray();
            foreach (var message in messages)
            {
                var fullMessage = message;
                AppendSupportInfo(ref fullMessage, w3cTraceId, selectedEnv);
                Debug.Log(fullMessage);
                api.Dispatch(addGenerationFeedback, new GenerationsFeedbackData(asset, new GenerationFeedbackData(fullMessage)));
            }
        }

        public static void DispatchFilteredDownloadMessage(this IStoreApi api, AssetReference asset, AiOperationFailedResult result, string lastW3CTraceId = null)
        {
            var selectedEnv = string.Empty;
            if (selectedEnvironment != null)
                selectedEnv = selectedEnvironment(api);

            var messages = result.Errors.Count == 0
                ? new[] { $"Download failed. Error was '{result.AiResponseError.ToString()}' from server." }
                : result.Errors.Distinct().Select(m => $"{result.AiResponseError.ToString()}: {m}").ToArray();
            foreach (var message in messages)
            {
                var fullMessage = message;
                AppendSupportInfo(ref fullMessage, lastW3CTraceId, selectedEnv);

                Debug.Log(fullMessage);
                api.Dispatch(addGenerationFeedback, new GenerationsFeedbackData(asset, new GenerationFeedbackData(fullMessage)));
            }
        }

        static JobStatusSdkEnum s_LastJobStatus = JobStatusSdkEnum.None;

        public static void DispatchJobUpdates(this IStoreApi _, string jobId, JobStatusSdkEnum jobStatus)
        {
            if (s_LastJobStatus == jobStatus)
                return;
            s_LastJobStatus = jobStatus;
            EditorTask.RunOnMainThread(() =>
            {
                if (LoggerUtilities.sdkLogLevel == 0)
                    return;
                Debug.Log($"Job {jobId} status: {jobStatus}");
            });
        }
    }
}
