using System;
using System.Collections.Generic;
using Unity.AI.Pbr.Services.Undo;
using Unity.AI.Pbr.Services.Utilities;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Utility;

namespace Unity.AI.Pbr.Services.Stores.States
{
    [Serializable]
    record GenerationResult
    {
        public const string noneMapping = "None";

        public bool generationAllowed = true;
        public List<GenerationProgressData> generationProgress = new();
        public List<GenerationFeedbackData> generationFeedback = new();
        public List<MaterialResult> generatedMaterials = new();
        public List<MaterialSkeleton> generatedSkeletons = new();

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

        public MaterialResult selectedGeneration = new();
        public SerializableDictionary<MapType, string> generatedMaterialMapping = new()
        {
            { MapType.Preview, noneMapping },
            { MapType.Height, noneMapping },
            { MapType.Normal, noneMapping },
            { MapType.Emission, noneMapping },
            { MapType.Metallic, noneMapping },
            { MapType.Roughness, noneMapping },
            { MapType.Delighted, noneMapping },
            { MapType.Occlusion, noneMapping },
            { MapType.Smoothness, noneMapping },
            { MapType.MetallicSmoothness, noneMapping },
            { MapType.NonMetallicSmoothness, noneMapping },
            { MapType.MaskMap, noneMapping },
            { MapType.Base, noneMapping },
            { MapType.Edge, noneMapping }
        };
        public AssetUndoManager assetUndoManager;
        public bool replaceWithoutConfirmation = true; // AI.Pbr and AI.Animate are unable to detect differences between asset and generation so we default to true for easier workflows
        public SerializableDictionary<string, GeneratedResultSelectorSettings> generatedResultSelectorSettings = new();
        public GenerationValidationResult generationValidation = new(false, BackendServiceConstants.ErrorTypes.Unknown, 1, new List<GenerationFeedbackData>());
    }

    [Serializable]
    record GeneratedResultSelectorSettings
    {
        public int itemCountHint = 0;
    }
}
