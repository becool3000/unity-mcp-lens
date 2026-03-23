using System;
using System.IO;
using System.Threading.Tasks;
using Unity.AI.Animate.Services.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.UI;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Generators.Tools
{
    /// <summary>
    /// Commands for processing Unity humanoid AnimationClips.
    /// </summary>
    enum AnimationCommands
    {
        /// <summary>
        /// Makes the humanoid animation stationary by removing root motion on the XZ plane and starting it at the origin.
        /// </summary>
        MakeStationary,

        /// <summary>
        /// Trims the humanoid animation to the best loop based on motion analysis.
        /// </summary>
        TrimToBestLoop
    }

    /// <summary>
    /// Parameters for processing a Unity humanoid AnimationClip.
    /// </summary>
    class AnimationProcessingParameters
    {
        /// <summary>
        /// The input humanoid animation clip to process. Must be a Unity humanoid AnimationClip.
        /// </summary>
        public AnimationClip InputAnimationClip { get; set; }

        /// <summary>
        /// The path to save the processed animation clip.
        /// </summary>
        public string OutputPath { get; set; }

        /// <summary>
        /// The command to execute on the animation clip.
        /// </summary>
        public AnimationCommands Command { get; set; }

        /// <summary>
        /// The start of the loop search window, in normalized value [0-1].
        /// </summary>
        public float LoopSearchWindowStart { get; set; } = 0.01f;

        /// <summary>
        /// The end of the loop search window, in normalized value [0-1].
        /// </summary>
        public float LoopSearchWindowEnd { get; set; } = 0.99f;

        /// <summary>
        /// The minimum loop duration ratio, as a fraction of the clip's total length.
        /// </summary>
        public float MinimumLoopDurationRatio { get; set; } = 0.25f;

        /// <summary>
        /// The minimum motion coverage required for a valid loop, as a percentage.
        /// </summary>
        public float MinimumMotionCoverage { get; set; } = 0.5f;

        /// <summary>
        /// The tolerance for muscle matching, in degrees.
        /// </summary>
        public float MuscleMatchingTolerance { get; set; } = 5.0f;
    }

    static partial class AssetGenerators
    {
        public static async Task ProcessAnimation(AnimationProcessingParameters parameters)
        {
            switch (parameters.Command)
            {
                case AnimationCommands.MakeStationary:
                {
                    if (parameters.InputAnimationClip == null)
                        throw new ArgumentNullException(nameof(parameters.InputAnimationClip));

                    // Only Unity humanoid animation clips are supported
                    if (!parameters.InputAnimationClip.CanBeEdited())
                        throw new InvalidOperationException("The input AnimationClip cannot be edited. Only Unity humanoid animation clips are supported. Ensure it is a humanoid animation.");

                    // Create a working copy to avoid modifying the original asset directly
                    var workingClip = new AnimationClip();
                    EditorUtility.CopySerialized(parameters.InputAnimationClip, workingClip);
                    workingClip.SetDefaultClipSettings(false);
                    workingClip.name = $"{parameters.InputAnimationClip.name}_stationary";

                    // Normalize transform to start at origin, then flatten root motion to keep it there.
                    workingClip.NormalizeRootTransform();
                    workingClip.FlattenRootMotion();

                    await workingClip.SaveToDisk(parameters.OutputPath, forceAddGenerationLabel: parameters.InputAnimationClip.HasGenerationLabel());
                    break;
                }
                case AnimationCommands.TrimToBestLoop:
                {
                    if (parameters.InputAnimationClip == null)
                        throw new ArgumentNullException(nameof(parameters.InputAnimationClip));

                    // Only Unity humanoid animation clips are supported
                    if (!parameters.InputAnimationClip.CanBeEdited())
                        throw new InvalidOperationException("The input AnimationClip cannot be edited. Only Unity humanoid animation clips are supported. Ensure it is a humanoid animation.");

                    // Create a working copy to avoid modifying the original asset directly
                    var workingClip = new AnimationClip();
                    EditorUtility.CopySerialized(parameters.InputAnimationClip, workingClip);
                    workingClip.SetDefaultClipSettings(false);
                    workingClip.name = $"{parameters.InputAnimationClip.name}_looped";

                    workingClip.NormalizeRootTransform();
                    workingClip.FlattenRootMotion();

                    var (success, startTime, endTime, _, _, _) = await workingClip.TryFindOptimalLoopPointsAsync(
                        parameters.LoopSearchWindowStart,
                        parameters.LoopSearchWindowEnd,
                        parameters.MinimumLoopDurationRatio,
                        parameters.MinimumMotionCoverage,
                        parameters.MuscleMatchingTolerance);

                    if (!success)
                    {
                        throw new Exception("Could not find a suitable loop in the animation clip with the given parameters.");
                    }

                    workingClip.Crop(startTime, endTime);
                    workingClip.SetDefaultClipSettings();

                    await workingClip.SaveToDisk(parameters.OutputPath, forceAddGenerationLabel: parameters.InputAnimationClip.HasGenerationLabel());
                    break;
                }
                default:
                    throw new ArgumentException($"Unsupported command: '{parameters.Command}'.");
            }
        }

        static async Task SaveToDisk(this AnimationClip clip, string outputPath, bool forceAddGenerationLabel = false)
        {
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var uniquePath = AssetDatabase.GenerateUniqueAssetPath(outputPath);
            AssetDatabase.CreateAsset(clip, uniquePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (clip.HasGenerationLabel() || forceAddGenerationLabel)
            {
                // Only add UnityAI label if the original clip had it
                var newAsset = AssetDatabase.LoadAssetAtPath<AnimationClip>(uniquePath);
                newAsset.EnableGenerationLabel();
            }

            var guid = AssetDatabase.AssetPathToGUID(uniquePath);
            if (string.IsNullOrEmpty(guid))
            {
                throw new NullReferenceException("guid is null or empty. Cannot copy to cache folder.");
            }

            var generatedAssetsPath = Asset.AssetReferenceExtensions.GetGeneratedAssetsPath(guid);
            try
            {
                await IO.Utilities.FileUtilities.CopyFileToCacheDirectory(uniquePath, generatedAssetsPath);
            }
            finally
            {
                GenerationFileSystemWatcher.nudge?.Invoke();
            }
        }
    }
}
