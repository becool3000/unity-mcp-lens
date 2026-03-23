using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Generators.Tools;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class EditAnimationClipTool
    {
        internal const string k_FunctionId = "Unity.AssetGeneration.EditAnimationClipTool";

        [Serializable]
        public class EditAnimationClipOutput : AssetOutputBase { }

        [AgentTool(
            description: Constants.EditAnimationDescription,
            id: k_FunctionId,
            tags: Constants.AssetGenerationFunctionTag,
            Mcp = McpAvailability.Available
        )]
        public static async Task<EditAnimationClipOutput> EditAnimation(
            ToolExecutionContext context,
            [Parameter("The path to the Unity humanoid Animation Clip to work on.")]
            string inputAnimationClipPath,
            [Parameter(Constants.AnimationCommandDescription)]
            AnimationCommands command,
            [Parameter("For TrimToBestLoop, the start of the loop search window, in normalized [0-1] value. Defaults to the start of the clip.")]
            float loopSearchWindowStart = 0.01f,
            [Parameter("For TrimToBestLoop, the end of the loop search window, in normalized [0-1] value. Defaults to the end of the clip.")]
            float loopSearchWindowEnd = 0.99f,
            [Parameter("For TrimToBestLoop, the minimum loop duration as a fraction of the clip's total length (e.g., 0.25 for 25%).")]
            float minimumLoopDurationRatio = 0.25f,
            [Parameter("For TrimToBestLoop, the minimum motion coverage required for a valid loop, as a percentage (e.g., 0.5 for 50%).")]
            float minimumMotionCoverage = 0.5f,
            [Parameter("For TrimToBestLoop, the tolerance for matching poses, in degrees. A lower value is stricter.")]
            float muscleMatchingTolerance = 5.0f
        )
        {
            try
            {
                if (string.IsNullOrEmpty(inputAnimationClipPath) || !File.Exists(inputAnimationClipPath))
                {
                    throw new ArgumentNullException(nameof(inputAnimationClipPath));
                }

                var interruptedAssetPaths = AssetGenerators.GetAllDownloadAssets()
                    .Select(AssetDatabase.GetAssetPath)
                    .ToList();
                if (interruptedAssetPaths.Contains(inputAnimationClipPath))
                    throw new Exception($"Asset {inputAnimationClipPath} is still being generated, check back later.");

                var animationClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(inputAnimationClipPath);

                if (animationClip == null)
                {
                    throw new ArgumentException("Provide at least one animation clip to edit.");
                }

                var outputPath = AssetDatabase.GenerateUniqueAssetPath(inputAnimationClipPath);
                await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Create, outputPath);

                var parameters = new AnimationProcessingParameters
                {
                    InputAnimationClip = animationClip,
                    OutputPath = outputPath,
                    Command = command,
                    LoopSearchWindowStart = loopSearchWindowStart,
                    LoopSearchWindowEnd = loopSearchWindowEnd,
                    MinimumLoopDurationRatio = minimumLoopDurationRatio,
                    MinimumMotionCoverage = minimumMotionCoverage,
                    MuscleMatchingTolerance = muscleMatchingTolerance
                };

                await AssetGenerators.ProcessAnimation(parameters);

                var finalMessage = "Modified Animation Clip located at: " + outputPath;

                var output = new EditAnimationClipOutput
                {
                    Message = finalMessage,
                    AssetPath = outputPath,
                    AssetGuid = AssetDatabase.AssetPathToGUID(outputPath)
                };
                return output;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                throw new Exception($"Error with animation tool: {ex.Message}", ex);
            }
        }
    }
}
