using System;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Generators.Tools;
using UnityEditor;
using UnityEngine;
using File = System.IO.File;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class EditAudioClipTool
    {
        internal const string k_FunctionId = "Unity.AudioClip.Edit";

        [Serializable]
        public class EditAudioClipOutput : AssetOutputBase { }

        [AgentTool(
            description: Constants.EditAudioDescription,
            id: k_FunctionId,
            assistantMode: AssistantMode.Agent,
            toolCallEnvironment:ToolCallEnvironment.EditMode | ToolCallEnvironment.PlayMode,
            tags: Constants.AssetGenerationFunctionTag,
            Mcp = McpAvailability.Available
            )]
        public static async Task<EditAudioClipOutput> EditAudio(
            ToolExecutionContext context,
            [Parameter("The path to the Audio Clip to work on.")]
            string inputAudioClipPath,
            [Parameter(Constants.AudioCommandDescription)]
            AssetGenerators.AudioCommands command,
            [Parameter("The start time in seconds of the audio to keep.")]
            float startTime = 0,
            [Parameter("The end time in seconds of the audio to keep. If the audio clip is 10 seconds, a value of 10 would mean to keep until the end of the clip.")]
            float endTime = 0,
            [Parameter("The factor by which to increase or decrease the volume. For example, '1.2f' increases volume by 20%, '0.8f' decreases volume by 20%.")]
            float factor = 1.0f,
            [Parameter("The duration in milliseconds of overlap samples to use as overlap when making a loop. 1000 ms = 1 second. Usually 100 ms is a good default value.")]
            int crossfadeDurationMs = 100
            )
        {
            try
            {
                if (string.IsNullOrEmpty(inputAudioClipPath) || !File.Exists(inputAudioClipPath))
                {
                    throw new ArgumentNullException(nameof(inputAudioClipPath));
                }

                var interruptedAssetPaths = AssetGenerators.GetAllDownloadAssets()
                    .Select(AssetDatabase.GetAssetPath)
                    .ToList();
                if (interruptedAssetPaths.Contains(inputAudioClipPath))
                    throw new Exception($"Asset {inputAudioClipPath} is still being generated, check back later.");

                var audioClip = AssetDatabase.LoadAssetAtPath<AudioClip>(inputAudioClipPath);

                if (audioClip == null)
                {
                    throw new ArgumentException("Provide at least one audio clip to edit.");
                }

                var outputPath = AssetDatabase.GenerateUniqueAssetPath(inputAudioClipPath);
                await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Create, outputPath);

                var parameters = new AssetGenerators.AudioProcessingParameters
                {
                    InputAudioClip = audioClip,
                    OutputPath = outputPath,
                    Command = command,
                    StartTime = startTime,
                    EndTime = endTime,
                    Factor = factor,
                    CrossfadeDurationMs = crossfadeDurationMs
                };

                await AssetGenerators.ProcessAudio(parameters);

                var output = new EditAudioClipOutput
                {
                    AssetPath = outputPath,
                    AssetGuid = AssetDatabase.AssetPathToGUID(outputPath)
                };
                return output;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error with audio tool: {ex.Message}", ex);
            }
        }
    }
}
