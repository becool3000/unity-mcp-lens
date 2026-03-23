using System;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Generators.Tools;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class ManageInterruptedAssetGenerationsTool
    {
        internal const string k_FunctionId = "Unity.AssetGeneration.ManageInterrupted";

        [AgentTool(
            Constants.ManageInterruptedAssetGenerationsFunctionDescription,
            k_FunctionId,
            tags: Constants.AssetGenerationFunctionTag,
            Mcp = McpAvailability.Available)]
        public static ManageInterruptedAssetGenerationsOutput ManageInterruptedAssetGenerations(
            [Parameter(Constants.ManageInterruptedAssetGenerationsCommandDescription)]
            ManageInterruptedAssetGenerationsCommands command) =>
            ManageInterruptedAssetGenerations(command: command, trackDownloads: false);

        [ToolPermissionIgnore]  // To ignore file creation permission check
        public static ManageInterruptedAssetGenerationsOutput ManageInterruptedAssetGenerations(ManageInterruptedAssetGenerationsCommands command, bool trackDownloads)
        {
            try
            {
                switch (command)
                {
                    case ManageInterruptedAssetGenerationsCommands.List:
                    {
                        var count = AssetGenerators.GetInterruptedDownloadsCount();
                        if (count == 0)
                            return new ManageInterruptedAssetGenerationsOutput { Message = "There are no interrupted generations to recover." };

                        var assets = AssetGenerators.GetInterruptedDownloadAssets();
                        var generationOutputs = assets.Select(asset =>
                        {
                            var assetType = Constants.GetAssetType(asset.GetType());
                            var assetPath = AssetDatabase.GetAssetPath(asset);
                            return new GenerateAssetOutput
                            {
                                AssetName = asset.name,
                                AssetPath = assetPath,
                                AssetGuid = AssetDatabase.AssetPathToGUID(assetPath),
                                AssetType = assetType,
                                FileInstanceID = asset.GetInstanceID(),
                                SubObjectInstanceID = GenerateAssetTool.GetOutputInstanceId(asset, assetType)
                            };
                        }).ToArray();

                        var message = $"Found {count} interrupted generation(s). You can '{Constants.ResumeCommand}' or '{Constants.DiscardCommand}' them.";
                        return new ManageInterruptedAssetGenerationsOutput { Message = message, Generations = generationOutputs };
                    }

                    case ManageInterruptedAssetGenerationsCommands.Resume:
                    {
                        var count = AssetGenerators.GetInterruptedDownloadsCount();
                        if (count == 0)
                            return new ManageInterruptedAssetGenerationsOutput { Message = "There are no interrupted generations to resume." };

                        var assets = AssetGenerators.GetInterruptedDownloadAssets();
                        var generationOutputs = assets.Select(asset =>
                        {
                            var assetType = Constants.GetAssetType(asset.GetType());
                            var assetPath = AssetDatabase.GetAssetPath(asset);
                            return new GenerateAssetOutput
                            {
                                AssetName = asset.name,
                                AssetPath = assetPath,
                                AssetGuid = AssetDatabase.AssetPathToGUID(assetPath),
                                AssetType = assetType,
                                FileInstanceID = asset.GetInstanceID(),
                                SubObjectInstanceID = GenerateAssetTool.GetOutputInstanceId(asset, assetType)
                            };
                        }).ToArray();

                        var handles = AssetGenerators.ResumeInterruptedDownloads();
                        foreach (var handle in handles)
                        {
                            if (trackDownloads)
                            {
                                GenerateAssetTool.InterruptedDownloadResumer.TrackDownload(handle);
                            }

                            // We don't await the download task, just kick it off.
                            _ = handle.DownloadTask.ContinueWith(t =>
                            {
                                if (t.IsFaulted)
                                    Debug.LogException(t.Exception);
                            }, TaskScheduler.FromCurrentSynchronizationContext());
                        }

                        return new ManageInterruptedAssetGenerationsOutput { Message = $"Resuming {count} interrupted generation(s).", Generations = generationOutputs };
                    }

                    case ManageInterruptedAssetGenerationsCommands.Discard:
                    {
                        var count = AssetGenerators.GetInterruptedDownloadsCount();
                        if (count == 0)
                            return new ManageInterruptedAssetGenerationsOutput { Message = "There are no interrupted generations to discard." };

                        var assets = AssetGenerators.GetInterruptedDownloadAssets();
                        var generationOutputs = assets.Select(asset =>
                        {
                            var assetType = Constants.GetAssetType(asset.GetType());
                            var assetPath = AssetDatabase.GetAssetPath(asset);
                            return new GenerateAssetOutput
                            {
                                AssetName = asset.name,
                                AssetPath = assetPath,
                                AssetGuid = AssetDatabase.AssetPathToGUID(assetPath),
                                AssetType = assetType,
                                FileInstanceID = asset.GetInstanceID(),
                                SubObjectInstanceID = GenerateAssetTool.GetOutputInstanceId(asset, assetType)
                            };
                        }).ToArray();

                        AssetGenerators.DiscardAllInterruptedDownloads();
                        return new ManageInterruptedAssetGenerationsOutput { Message = $"Discarded {count} interrupted generation(s).", Generations = generationOutputs };
                    }

                    default:
                        throw new ArgumentException($"Unsupported command: '{command}'. Supported values are: '{Constants.ListCommand}', '{Constants.ResumeCommand}', '{Constants.DiscardCommand}'.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                throw new Exception($"Error managing interrupted generations: {ex.Message}", ex);
            }
        }
    }
}
