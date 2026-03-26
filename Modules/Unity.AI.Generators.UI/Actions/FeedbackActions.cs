using System;
using AiEditorToolsSdk.Components.Common.Enums;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Accounts.Services.Core;
using Unity.AI.Toolkit.Asset;
using UnityEngine;

namespace Unity.AI.Generators.UI.Actions
{
    /// <summary>
    /// Feedback sentiment for generation feedback.
    /// </summary>
    internal enum GenerationFeedbackSentiment
    {
        Positive,
        Negative
    }

    /// <summary>
    /// Payload for submitting generation feedback.
    /// </summary>
    /// <param name="asset">The asset reference for the generation.</param>
    /// <param name="generationUri">The URI of the generated asset to provide feedback on.</param>
    /// <param name="sentiment">The sentiment of the feedback (positive/negative).</param>
    /// <param name="dialogType">The dialog type for the generation modality.</param>
    /// <param name="feedbackText">Optional text feedback from the user.</param>
    /// <param name="feedbackCategories">Categories for the feedback.</param>
    internal record SubmitGenerationFeedbackPayload(
        AssetReference asset,
        string generationUri,
        GenerationFeedbackSentiment sentiment,
        string dialogType = FeedbackActions.ImageGenerationDialogType,
        string feedbackText = null,
        string[] feedbackCategories = null,
        string downloadedAssetId = null);

    /// <summary>
    /// Payload for tracking that feedback was submitted.
    /// </summary>
    /// <param name="asset">The asset reference for the generation.</param>
    /// <param name="generationUri">The URI of the generated asset that received feedback.</param>
    /// <param name="sentiment">The sentiment that was submitted.</param>
    internal record GenerationFeedbackSubmittedPayload(
        AssetReference asset,
        string generationUri,
        GenerationFeedbackSentiment sentiment);

    /// <summary>
    /// Actions for submitting feedback on generated assets.
    /// </summary>
    static class FeedbackActions
    {
        public static readonly string slice = "generationResults";

        /// <summary>
        /// Default feedback categories for image generation.
        /// </summary>
        public static readonly string[] DefaultCategories = { "quality", "prompt_adherence", "style", "other" };

        /// <summary>
        /// Dialog type for image generation feedback.
        /// </summary>
        public const string ImageGenerationDialogType = "editor-generators-image";

        /// <summary>
        /// Dialog type for animation generation feedback.
        /// </summary>
        public const string AnimateGenerationDialogType = "editor-generators-animate";

        /// <summary>
        /// Dialog type for mesh generation feedback.
        /// </summary>
        public const string MeshGenerationDialogType = "editor-generators-mesh";

        /// <summary>
        /// Dialog type for PBR/material generation feedback.
        /// </summary>
        public const string PbrGenerationDialogType = "editor-generators-pbr";

        /// <summary>
        /// Dialog type for sound generation feedback.
        /// </summary>
        public const string SoundGenerationDialogType = "editor-generators-sound";

        /// <summary>
        /// Action to mark a generation as having received feedback.
        /// </summary>
        public static Creator<GenerationFeedbackSubmittedPayload> setGenerationFeedbackSubmitted => new($"{slice}/setGenerationFeedbackSubmitted");

        /// <summary>
        /// Async thunk to submit feedback for a generation.
        /// </summary>
        public static readonly AsyncThunkCreatorWithArg<SubmitGenerationFeedbackPayload> submitGenerationFeedback = new(
            $"{slice}/submitGenerationFeedback",
            async (payload, api) =>
            {
                try
                {
                    var sdkSentiment = payload.sentiment == GenerationFeedbackSentiment.Positive
                        ? FeedbackSentimentEnum.Positive
                        : FeedbackSentimentEnum.Negative;

                    var categories = payload.feedbackCategories ?? DefaultCategories;
                    var feedbackText = payload.feedbackText ?? string.Empty;

                    var dialogType = payload.dialogType ?? ImageGenerationDialogType;

                    var success = await AccountApi.SubmitFeedback(
                        payload.asset,
                        dialogType,
                        feedbackText,
                        categories,
                        sdkSentiment,
                        payload.downloadedAssetId);

                    if (success)
                    {
                        // Mark the generation as having received feedback
                        api.Dispatch(setGenerationFeedbackSubmitted, new GenerationFeedbackSubmittedPayload(
                            payload.asset,
                            payload.generationUri,
                            payload.sentiment));

                        // Persist feedback to the metadata JSON file so it survives editor restarts
                        var feedbackValue = payload.sentiment == GenerationFeedbackSentiment.Positive ? 1 : -1;
                        if (Uri.TryCreate(payload.generationUri, UriKind.Absolute, out var uri))
                            _ = GeneratedAssetMetadata.WriteFeedbackToMetadata(uri, feedbackValue);
                    }
                    else
                    {
                        const string message = "Failed to submit generation feedback.";
                        Debug.LogWarning(message);
                        api.Dispatch(GenerationActions.addGenerationFeedback,
                            new Payloads.GenerationsFeedbackData(payload.asset, new Payloads.GenerationFeedbackData(message)));
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            });
    }
}
