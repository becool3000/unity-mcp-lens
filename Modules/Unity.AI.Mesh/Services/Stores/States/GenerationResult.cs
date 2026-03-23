using System;
using System.Collections.Generic;
using Unity.AI.Mesh.Services.Undo;
using Unity.AI.Mesh.Services.Utilities;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Utility;

namespace Unity.AI.Mesh.Services.Stores.States
{
    [Serializable]
    record GenerationResult
    {
        public bool generationAllowed = true;
        public List<GenerationProgressData> generationProgress = new();
        public List<GenerationFeedbackData> generationFeedback = new();
        public List<MeshResult> generatedMeshes = new();
        public List<MeshSkeleton> generatedSkeletons = new();

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

        public MeshResult selectedGeneration = new();
        public AssetUndoManager assetUndoManager;
        public int generationCount;
        public bool replaceWithoutConfirmation;
        public SerializableDictionary<string, GeneratedResultSelectorSettings> generatedResultSelectorSettings = new();
        public GenerationValidationResult generationValidation = new(true, BackendServiceConstants.ErrorTypes.Unknown, 0, new List<GenerationFeedbackData>());
    }

    [Serializable]
    record GeneratedResultSelectorSettings
    {
        public int itemCountHint = 0;
    }
}
