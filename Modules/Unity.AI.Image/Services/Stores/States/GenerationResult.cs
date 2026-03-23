using System;
using System.Collections.Generic;
using Unity.AI.Generators.Asset;
using Unity.AI.Image.Services.Undo;
using Unity.AI.Image.Services.Utilities;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;
using Unity.AI.Toolkit.Utility;

namespace Unity.AI.Image.Services.Stores.States
{
    [Serializable]
    record GenerationResult
    {
        public bool generationAllowed = true;
        public List<GenerationProgressData> generationProgress = new();
        public List<GenerationFeedbackData> generationFeedback = new();
        public List<TextureResult> generatedTextures = new();
        public List<TextureSkeleton> generatedSkeletons = new();

        /// <summary>
        /// Maps in-progress skeletons to their completed texture results.
        ///
        /// When a generation starts, a TextureSkeleton is created to represent the in-progress task.
        /// When the generation completes, a TextureResult is created with the result URI.
        /// FulfilledSkeletons links these two by storing:
        /// - progressTaskID: Matches with TextureSkeleton.taskID
        /// - resultUri: Matches with TextureResult.uri.AbsoluteUri
        ///
        /// This mapping allows UI to properly transition from showing in-progress skeletons to completed results.
        /// </summary>
        public List<FulfilledSkeleton> fulfilledSkeletons = new();

        public TextureResult selectedGeneration = new();
        public AssetUndoManager assetUndoManager;
        public int generationCount;
        public bool replaceWithoutConfirmation;
        public bool useUnsavedAssetBytes = true;
        public Action<AssetReference> promoteNewAssetPostAction = null;
        public SerializableDictionary<string, GeneratedResultSelectorSettings> generatedResultSelectorSettings = new();
        public GenerationValidationResult generationValidation = new(true, BackendServiceConstants.ErrorTypes.Unknown, 0, new List<GenerationFeedbackData>());
    }

    [Serializable]
    record GeneratedResultSelectorSettings
    {
        public int itemCountHint = 0;
    }
}
