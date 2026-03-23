using System;
using System.Threading.Tasks;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI;
using Unity.AI.Sound.Services.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Generators.Tools
{
    static partial class AssetGenerators
    {
        public enum AudioCommands
        {
            TrimSilence,
            TrimSound,
            ChangeVolume,
            LoopSound
        }

        public class AudioProcessingParameters
        {
            public AudioClip InputAudioClip { get; set; }
            public string OutputPath { get; set; }
            public AudioCommands Command { get; set; }
            public float StartTime { get; set; }
            public float EndTime { get; set; }
            public float Factor { get; set; } = 1.0f;
            public int CrossfadeDurationMs { get; set; } = 100;
        }

        public static async Task ProcessAudio(AudioProcessingParameters parameters)
        {
            switch (parameters.Command)
            {
                case AudioCommands.TrimSilence:
                {
                    var audioSamples = parameters.InputAudioClip.TrimSilences();
                    await parameters.InputAudioClip.SaveToDisk(audioSamples.samples, parameters.OutputPath,
                        audioSamples.startPosition, audioSamples.endPosition);
                    break;
                }
                case AudioCommands.TrimSound:
                {
                    var audioSamples = parameters.InputAudioClip.TrimAudioFromStartAndEndTime(parameters.StartTime, parameters.EndTime);
                    var playClip = AudioClip.Create(parameters.InputAudioClip.name + "_clone", audioSamples.Length / Math.Max(1, parameters.InputAudioClip.channels), parameters.InputAudioClip.channels, parameters.InputAudioClip.frequency, false);

                    try
                    {
                        playClip.SetData(audioSamples, 0);
                        // The clip we create does not have the generation label, we verify on the original one to see if we need to add it
                        await playClip.SaveToDisk(audioSamples, parameters.OutputPath, forceAddGenerationLabel : parameters.InputAudioClip.HasGenerationLabel());
                    }
                    finally
                    {
                        playClip.SafeDestroy();
                    }
                    break;
                }
                case AudioCommands.ChangeVolume:
                {
                    var samples = parameters.InputAudioClip.ChangeVolume(parameters.Factor);
                    await parameters.InputAudioClip.SaveToDisk(samples, parameters.OutputPath);
                    break;
                }
                case AudioCommands.LoopSound:
                {
                    var samples = parameters.InputAudioClip.CreateCrossFade(parameters.CrossfadeDurationMs);
                    await parameters.InputAudioClip.SaveToDisk(samples, parameters.OutputPath);
                    break;
                }
                default:
                    throw new ArgumentException($"Unsupported command: '{parameters.Command}'.");
            }
        }

        /// <summary>
        /// Save an audio clip to disk.
        /// </summary>
        /// <param name="audioClip">The audio clip to save.</param>
        /// <param name="audioSamples">The audio samples to save.</param>
        /// <param name="outputPath">Path to save the file.</param>
        /// <param name="startPosition">Start position normalized.</param>
        /// <param name="endPosition">End position normalized.</param>
        /// <param name="forceAddGenerationLabel"><c>true</c> to force adding the Generation Label to the new asset, <c>false</c> otherwise</param>
        static async Task SaveToDisk(this AudioClip audioClip, float[] audioSamples, string outputPath, float startPosition = 0, float endPosition = 1, bool forceAddGenerationLabel = false)
        {
            {
                await using var fileStream = FileIO.OpenWriteAsync(outputPath);
                await audioClip.EncodeToWavAsync(fileStream, audioSamples, audioClip.MakeDefaultEnvelope(startPosition, endPosition));
            }
            AssetDatabase.Refresh();
            {
                if (audioClip.HasGenerationLabel() || forceAddGenerationLabel)
                {
                    // Only add UnityAI label if the original clip had it
                    var newAsset = AssetDatabase.LoadAssetAtPath<AudioClip>(outputPath);
                    newAsset.EnableGenerationLabel();
                }

                var guid = AssetDatabase.AssetPathToGUID(outputPath);
                if (string.IsNullOrEmpty(guid))
                {
                    throw new NullReferenceException("guid is null or empty. Cannot copy to cache folder.");
                }

                var generatedAssetsPath = Asset.AssetReferenceExtensions.GetGeneratedAssetsPath(guid);
                try
                {
                    await IO.Utilities.FileUtilities.CopyFileToCacheDirectory(outputPath, generatedAssetsPath);
                }
                finally
                {
                    GenerationFileSystemWatcher.nudge?.Invoke();
                }
            }
        }
    }
}
