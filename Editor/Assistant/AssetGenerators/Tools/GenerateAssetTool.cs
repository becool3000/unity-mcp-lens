using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Generators.Tools;
using Unity.AI.Toolkit;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class GenerateAssetTool
    {
        public const string ToolName = "Unity.AssetGeneration.GenerateAsset";

        [AgentTool(
            Constants.GenerateAssetFunctionDescription,
            ToolName,
            tags: Constants.AssetGenerationFunctionTag,
            Mcp = McpAvailability.Default)]
        public static async Task<GenerateAssetOutput> GenerateAsset(
            ToolExecutionContext context,
            [Parameter(Constants.CommandDescription)]
            GenerationCommands command,
            [Parameter(Constants.ModelIdDescription)]
            string modelId = null,
            [Parameter(Constants.PromptDescription)]
            string prompt = null,
            [Parameter(Constants.SavePathDescription)]
            string savePath = null,
            [Parameter(Constants.WaitForCompletionDescription)]
            bool waitForCompletion = true,
            [Parameter(Constants.TargetAssetPathDescription)]
            string targetAssetPath = null,
            [Parameter(Constants.ReferenceImageInstanceIdDescription)]
            int referenceImageInstanceId = -1,
            [Parameter(Constants.DurationInSecondsDescription)]
            float durationInSeconds = 0,
            [Parameter(Constants.LoopDescription)]
            bool loop = false,
            [Parameter(Constants.SpriteWidthDescription)]
            int width = 0,
            [Parameter(Constants.SpriteHeightDescription)]
            int height = 0,
            [Parameter(Constants.ForceGenerationDescription)]
            bool forceGeneration = false)
        {
            if (!forceGeneration && AssetGenerators.HasInterruptedDownloads())
            {
                throw new Exception("There are interrupted asset generations. Please 'resume' them before generating new assets. To bypass this check, set 'forceGeneration' to 'true'.");
            }

            var interruptedAssetPaths = AssetGenerators.GetAllDownloadAssets()
                .Select(AssetDatabase.GetAssetPath)
                .ToList();

            if (!string.IsNullOrEmpty(targetAssetPath) && interruptedAssetPaths.Contains(targetAssetPath))
                throw new Exception($"Asset {targetAssetPath} is still being generated, check back later.");

#if UNITY_6000_3_OR_NEWER
            if (referenceImageInstanceId != -1 && EditorUtility.EntityIdToObject(referenceImageInstanceId) == null)
#else
            if (referenceImageInstanceId != -1 && EditorUtility.InstanceIDToObject(referenceImageInstanceId) == null)
#endif
                throw new Exception($"Reference image with InstanceID {referenceImageInstanceId} does not exist. Please provide a valid InstanceID.");

            {
#if UNITY_6000_3_OR_NEWER
                var referenceImagePath = AssetDatabase.GetAssetPath((EntityId) referenceImageInstanceId);
#else
                var referenceImagePath = AssetDatabase.GetAssetPath(EditorUtility.InstanceIDToObject(referenceImageInstanceId));
#endif
                if (!string.IsNullOrEmpty(referenceImagePath) && interruptedAssetPaths.Contains(referenceImagePath))
                    throw new Exception($"Asset {referenceImagePath} is still being generated, check back later.");
            }

#if UNITY_6000_3_OR_NEWER
            var referenceImage = EditorUtility.EntityIdToObject(referenceImageInstanceId);
#else
            var referenceImage = EditorUtility.InstanceIDToObject(referenceImageInstanceId);
#endif

            try
            {
                AssetTypes assetType;
                GenerationHandle<Object> generationHandle;
                Object targetAsset = null;

                switch (command)
                {
                    case GenerationCommands.GenerateHumanoidAnimation:
                    {
                        if (string.IsNullOrEmpty(prompt)) throw new ArgumentException(Constants.PromptRequired);
                        if (referenceImage) throw new ArgumentException("A 'referenceImage' cannot be used when generating a Humanoid Animation.");
                        assetType = AssetTypes.HumanoidAnimation;
                        var parameters = new GenerationParameters<AnimationSettings>
                        {
                            AssetType = typeof(AnimationClip),
                            Prompt = prompt,
                            SavePath = savePath,
                            ModelId = modelId,
                            Settings = new AnimationSettings { DurationInSeconds = durationInSeconds },
                            PermissionCheckAsync = (path, cost) => context.Permissions.CheckAssetGeneration(path, typeof(AnimationClip), cost)
                        };
                        if (!string.IsNullOrEmpty(targetAssetPath))
                        {
                            targetAsset = AssetDatabase.LoadAssetAtPath<AnimationClip>(targetAssetPath);
                            if (targetAsset == null)
                                throw new ArgumentException($"Failed to find a valid AnimationClip asset at the specified path: '{targetAssetPath}'.");
                            parameters.TargetAsset = targetAsset;
                        }
                        generationHandle = AssetGenerators.GenerateAsync(parameters);
                        break;
                    }
                    case GenerationCommands.GenerateCubemap:
                    {
                        if (string.IsNullOrEmpty(prompt)) throw new ArgumentException(Constants.PromptRequired);
                        if (referenceImage) throw new ArgumentException("A 'referenceImage' cannot be used when generating a Cubemap.");
                        assetType = AssetTypes.Cubemap;
                        var parameters = new GenerationParameters<CubemapSettings>
                        {
                            AssetType = typeof(Cubemap),
                            Prompt = prompt,
                            SavePath = savePath,
                            ModelId = modelId,
                            Settings = new CubemapSettings { Upscale = false },
                            PermissionCheckAsync = (path, cost) => context.Permissions.CheckAssetGeneration(path, typeof(Cubemap), cost)
                        };
                        if (!string.IsNullOrEmpty(targetAssetPath))
                        {
                            targetAsset = AssetDatabase.LoadAssetAtPath<Cubemap>(targetAssetPath);
                            if (targetAsset == null)
                                throw new ArgumentException($"Failed to find a valid Cubemap asset at the specified path: '{targetAssetPath}'.");
                            parameters.TargetAsset = targetAsset;
                        }
                        generationHandle = AssetGenerators.GenerateAsync(parameters);
                        break;
                    }
                    case GenerationCommands.UpscaleCubemap:
                    {
                        if (string.IsNullOrEmpty(targetAssetPath)) throw new ArgumentException($"'{nameof(targetAssetPath)}' is required for upscaling a cubemap.");
                        if (!string.IsNullOrEmpty(prompt)) throw new ArgumentException($"A '{nameof(prompt)}' cannot be used when upscaling a cubemap.");
                        if (referenceImage) throw new ArgumentException($"A '{nameof(referenceImageInstanceId)}' cannot be used when upscaling a cubemap.");

                        assetType = AssetTypes.Cubemap;
                        var targetCubemap = AssetDatabase.LoadAssetAtPath<Cubemap>(targetAssetPath);
                        if (targetCubemap == null)
                            throw new ArgumentException($"Failed to find a valid {nameof(Cubemap)} asset at the specified path: '{targetAssetPath}'.");

                        generationHandle = AssetGenerators.UpscaleCubemapAsync(targetCubemap, (path, cost) => context.Permissions.CheckAssetGeneration(path, typeof(Cubemap), cost));
                        targetAsset = targetCubemap;
                        break;
                    }
                    case GenerationCommands.GenerateMaterial:
                    case GenerationCommands.GenerateTerrainLayer:
                    {
                        var isTerrainLayer = command == GenerationCommands.GenerateTerrainLayer;

                        if (referenceImage == null && string.IsNullOrEmpty(prompt))
                            throw new ArgumentException($"Either '{nameof(referenceImageInstanceId)}' or '{nameof(prompt)}' must be provided.");

                        if (isTerrainLayer)
                        {
                            assetType = AssetTypes.TerrainLayer;
                            var settings = new TerrainLayerSettings();
                            if (referenceImage != null)
                            {
                                if (referenceImage is not Texture2D)
                                    throw new ArgumentException($"{nameof(referenceImageInstanceId)} is not a valid {nameof(Texture2D)}.");
                                settings.ImageReferences = new[] { new ObjectReference { Image = referenceImage } };
                            }
                            var parameters = new GenerationParameters<TerrainLayerSettings>
                            {
                                AssetType = typeof(TerrainLayer),
                                Prompt = prompt,
                                SavePath = savePath,
                                ModelId = modelId,
                                Settings = settings,
                                PermissionCheckAsync = (path, cost) => context.Permissions.CheckAssetGeneration(path, typeof(TerrainLayer), cost)
                            };
                            if (!string.IsNullOrEmpty(targetAssetPath))
                            {
                                targetAsset = AssetDatabase.LoadAssetAtPath<TerrainLayer>(targetAssetPath);
                                if (targetAsset == null) throw new ArgumentException($"Failed to find a valid {nameof(TerrainLayer)} asset at the specified path: '{targetAssetPath}'.");
                                parameters.TargetAsset = targetAsset;
                            }
                            generationHandle = AssetGenerators.GenerateAsync(parameters);
                        }
                        else
                        {
                            assetType = AssetTypes.Material;
                            var settings = new MaterialSettings();
                            if (referenceImage != null)
                            {
                                if (referenceImage is not (Texture2D or Cubemap))
                                    throw new ArgumentException($"{nameof(referenceImageInstanceId)} is not a valid {nameof(Texture2D)} or {nameof(Cubemap)}.");
                                settings.ImageReferences = new[] { new ObjectReference { Image = referenceImage } };
                            }
                            var parameters = new GenerationParameters<MaterialSettings>
                            {
                                AssetType = typeof(Material),
                                Prompt = prompt,
                                SavePath = savePath,
                                ModelId = modelId,
                                Settings = settings,
                                PermissionCheckAsync = (path, cost) => context.Permissions.CheckAssetGeneration(path, typeof(Material), cost)
                            };
                            if (!string.IsNullOrEmpty(targetAssetPath))
                            {
                                targetAsset = AssetDatabase.LoadAssetAtPath<Material>(targetAssetPath);
                                if (targetAsset == null) throw new ArgumentException($"Failed to find a valid {nameof(Material)} asset at the specified path: '{targetAssetPath}'.");
                                parameters.TargetAsset = targetAsset;
                            }
                            generationHandle = AssetGenerators.GenerateAsync(parameters);
                        }
                        break;
                    }
                    case GenerationCommands.AddPbrToMaterial:
                    {
                        if (string.IsNullOrEmpty(targetAssetPath)) throw new ArgumentException($"'{nameof(targetAssetPath)}' is required to add PBR to a material.");
                        assetType = AssetTypes.Material;

                        var targetMaterial = AssetDatabase.LoadAssetAtPath<Material>(targetAssetPath);
                        if (targetMaterial == null)
                            throw new ArgumentException($"Failed to find a valid {nameof(Material)} asset at the specified path: '{targetAssetPath}'.");

                        var settings = new MaterialSettings();
                        if (referenceImage != null)
                        {
                            if (referenceImage is not (Texture2D or Cubemap))
                                throw new ArgumentException($"{nameof(referenceImageInstanceId)} is not a valid {nameof(Texture2D)} or {nameof(Cubemap)}.");
                            settings.ImageReferences = new[] { new ObjectReference { Image = referenceImage } };
                        }
                        generationHandle = AssetGenerators.AddPbrToMaterialAsync(targetMaterial, settings, modelId, (path, cost) => context.Permissions.CheckAssetGeneration(path, typeof(Material), cost));
                        targetAsset = targetMaterial;
                        break;
                    }
                    case GenerationCommands.AddPbrToTerrainLayer:
                    {
                        if (string.IsNullOrEmpty(targetAssetPath)) throw new ArgumentException($"'{nameof(targetAssetPath)}' is required to add PBR to a terrain layer.");
                        assetType = AssetTypes.TerrainLayer;

                        var targetTerrainLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(targetAssetPath);
                        if (targetTerrainLayer == null)
                            throw new ArgumentException($"Failed to find a valid {nameof(TerrainLayer)} asset at the specified path: '{targetAssetPath}'.");

                        var settings = new TerrainLayerSettings();
                        if (referenceImage != null)
                        {
                            if (referenceImage is not Texture2D)
                                throw new ArgumentException($"{nameof(referenceImageInstanceId)} is not a valid {nameof(Texture2D)}.");
                            settings.ImageReferences = new[] { new ObjectReference { Image = referenceImage } };
                        }
                        generationHandle = AssetGenerators.AddPbrToTerrainLayerAsync(targetTerrainLayer, settings, modelId, (path, cost) => context.Permissions.CheckAssetGeneration(path, typeof(TerrainLayer), cost));
                        targetAsset = targetTerrainLayer;
                        break;
                    }
                    case GenerationCommands.GenerateMesh:
                    {
                        if (string.IsNullOrEmpty(prompt) && referenceImage == null)
                            throw new ArgumentException($"Either '{nameof(prompt)}' or '{nameof(referenceImageInstanceId)}' must be provided for Mesh generation.");
                        assetType = AssetTypes.Mesh;
                        var settings = new MeshSettings();
                        if (referenceImage != null)
                        {
                            if (referenceImage is not Texture2D)
                                throw new ArgumentException($"{nameof(referenceImageInstanceId)} is not a valid {nameof(Texture2D)}.");
                            settings.ImageReferences = new[] { new ObjectReference { Image = referenceImage } };
                        }
                        var parameters = new GenerationParameters<MeshSettings>
                        {
                            AssetType = typeof(GameObject),
                            Prompt = prompt,
                            SavePath = savePath,
                            ModelId = modelId,
                            Settings = settings,
                            PermissionCheckAsync = (path, cost) => context.Permissions.CheckAssetGeneration(path, typeof(GameObject), cost)
                        };
                        if (!string.IsNullOrEmpty(targetAssetPath))
                        {
                            targetAsset = AssetDatabase.LoadAssetAtPath<GameObject>(targetAssetPath);
                            if (targetAsset == null) throw new ArgumentException($"Failed to find a valid {nameof(GameObject)} asset at the specified path: '{targetAssetPath}'.");
                            parameters.TargetAsset = targetAsset;
                        }
                        generationHandle = AssetGenerators.GenerateAsync(parameters);
                        break;
                    }
                    case GenerationCommands.GenerateSound:
                    {
                        if (string.IsNullOrEmpty(prompt)) throw new ArgumentException(Constants.PromptRequired);
                        if (referenceImage) throw new ArgumentException("A 'referenceImage' cannot be used when generating a Sound.");
                        assetType = AssetTypes.Sound;
                        var parameters = new GenerationParameters<SoundSettings>
                        {
                            AssetType = typeof(AudioClip),
                            Prompt = prompt,
                            SavePath = savePath,
                            ModelId = modelId,
                            Settings = new SoundSettings { DurationInSeconds = durationInSeconds, Loop = loop },
                            PermissionCheckAsync = (path, cost) => context.Permissions.CheckAssetGeneration(path, typeof(AudioClip), cost)
                        };
                        if (!string.IsNullOrEmpty(targetAssetPath))
                        {
                            targetAsset = AssetDatabase.LoadAssetAtPath<AudioClip>(targetAssetPath);
                            if (targetAsset == null) throw new ArgumentException($"Failed to find a valid {nameof(AudioClip)} asset at the specified path: '{targetAssetPath}'.");
                            parameters.TargetAsset = targetAsset;
                        }
                        generationHandle = AssetGenerators.GenerateAsync(parameters);
                        break;
                    }
                    case GenerationCommands.GenerateSprite:
                    {
                        if (string.IsNullOrEmpty(prompt)) throw new ArgumentException(Constants.PromptRequired);
                        assetType = AssetTypes.Sprite;
                        var settings = new SpriteSettings { RemoveBackground = false, Width = width, Height = height };
                        if (referenceImage)
                        {
                            if (referenceImage is not Texture2D) throw new ArgumentException("ReferenceImage is not a valid Texture2D.");
                            settings.ImageReferences = new[] { new ObjectReference { Image = referenceImage } };
                        }
                        var parameters = new GenerationParameters<SpriteSettings>
                        {
                            AssetType = typeof(Texture2D),
                            Prompt = prompt,
                            SavePath = savePath,
                            ModelId = modelId,
                            Settings = settings,
                            PermissionCheckAsync = (path, cost) => context.Permissions.CheckAssetGeneration(path, typeof(Texture2D), cost)
                        };
                        if (!string.IsNullOrEmpty(targetAssetPath))
                        {
                            targetAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(targetAssetPath);
                            if (targetAsset == null) throw new ArgumentException($"Failed to find a valid {nameof(Texture2D)} asset at the specified path: '{targetAssetPath}'.");
                            parameters.TargetAsset = targetAsset;
                        }
                        generationHandle = AssetGenerators.GenerateAsync(parameters);
                        break;
                    }
                    case GenerationCommands.GenerateImage:
                    {
                        if (string.IsNullOrEmpty(prompt)) throw new ArgumentException(Constants.PromptRequired);
                        assetType = AssetTypes.Image;
                        var settings = new ImageSettings { RemoveBackground = false, Width = width, Height = height };
                        if (referenceImage)
                        {
                            if (referenceImage is not Texture2D) throw new ArgumentException("ReferenceImage is not a valid Texture2D.");
                            settings.ImageReferences = new[] { new ObjectReference { Image = referenceImage } };
                        }
                        var parameters = new GenerationParameters<ImageSettings>
                        {
                            AssetType = typeof(Texture2D),
                            Prompt = prompt,
                            SavePath = savePath,
                            ModelId = modelId,
                            Settings = settings,
                            PermissionCheckAsync = (path, cost) => context.Permissions.CheckAssetGeneration(path, typeof(Texture2D), cost)
                        };
                        if (!string.IsNullOrEmpty(targetAssetPath))
                        {
                            targetAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(targetAssetPath);
                            if (targetAsset == null) throw new ArgumentException($"Failed to find a valid {nameof(Texture2D)} asset at the specified path: '{targetAssetPath}'.");
                            parameters.TargetAsset = targetAsset;
                        }
                        generationHandle = AssetGenerators.GenerateAsync(parameters);
                        break;
                    }
                    case GenerationCommands.RemoveSpriteBackground:
                    case GenerationCommands.RemoveImageBackground:
                    {
                        if (string.IsNullOrEmpty(targetAssetPath)) throw new ArgumentException($"'{nameof(targetAssetPath)}' is required for removing a sprite's background.");
                        if (!string.IsNullOrEmpty(prompt)) throw new ArgumentException($"A '{nameof(prompt)}' cannot be used when removing a sprite's background.");
                        if (referenceImage) throw new ArgumentException($"A '{nameof(referenceImageInstanceId)}' cannot be used when removing a background.");

                        assetType = command == GenerationCommands.RemoveSpriteBackground ? AssetTypes.Sprite : AssetTypes.Image;
                        var targetSprite = AssetDatabase.LoadAssetAtPath<Texture2D>(targetAssetPath);
                        if (targetSprite == null)
                            throw new ArgumentException($"Failed to find a valid {nameof(Texture2D)} asset at the specified path: '{targetAssetPath}'.");

                        generationHandle = AssetGenerators.RemoveSpriteBackgroundAsync(targetSprite, (path, cost) => context.Permissions.CheckAssetGeneration(path, typeof(Texture2D), cost));
                        targetAsset = targetSprite;
                        break;
                    }
                    case GenerationCommands.EditSpriteWithPrompt:
                    {
                        if (string.IsNullOrEmpty(targetAssetPath) && !referenceImage) throw new ArgumentException($"'{nameof(targetAssetPath)}' or '{nameof(referenceImageInstanceId)}' is required when editing an asset with a prompt.");
                        if (string.IsNullOrEmpty(prompt)) throw new ArgumentException($"'{nameof(prompt)}' is required when editing an asset.");

                        assetType = AssetTypes.Sprite;
                        if (!string.IsNullOrEmpty(targetAssetPath))
                        {
                            targetAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(targetAssetPath);
                            if (targetAsset == null)
                                throw new ArgumentException($"Failed to find a valid {nameof(Texture2D)} asset at the specified path: '{targetAssetPath}'.");
                        }

                        Texture2D imageReference;
                        if (referenceImage != null)
                        {
                            imageReference = referenceImage as Texture2D;
                            if (imageReference == null)
                                throw new ArgumentException($"{nameof(referenceImageInstanceId)} is not a valid {nameof(Texture2D)}.");
                        }
                        else
                        {
                            imageReference = (Texture2D)targetAsset;
                        }

                        var settings = new SpriteSettings
                        {
                            RemoveBackground = false,
                            Width = width,
                            Height = height,
                            ImageReferences = new[] { new ObjectReference { Image = imageReference } }
                        };
                        var parameters = new GenerationParameters<SpriteSettings>
                        {
                            AssetType = typeof(Texture2D),
                            Prompt = prompt,
                            SavePath = savePath,
                            ModelId = modelId,
                            Settings = settings,
                            TargetAsset = targetAsset,
                            PermissionCheckAsync = (path, cost) => context.Permissions.CheckAssetGeneration(path, typeof(Texture2D), cost)
                        };
                        generationHandle = AssetGenerators.GenerateAsync(parameters);
                        break;
                    }
                    case GenerationCommands.EditImageWithPrompt:
                    {
                        if (string.IsNullOrEmpty(targetAssetPath) && !referenceImage) throw new ArgumentException($"'{nameof(targetAssetPath)}' or '{nameof(referenceImageInstanceId)}' is required when editing an asset with a prompt.");
                        if (string.IsNullOrEmpty(prompt)) throw new ArgumentException($"'{nameof(prompt)}' is required when editing an asset.");

                        assetType = AssetTypes.Image;
                        if (!string.IsNullOrEmpty(targetAssetPath))
                        {
                            targetAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(targetAssetPath);
                            if (targetAsset == null)
                                throw new ArgumentException($"Failed to find a valid {nameof(Texture2D)} asset at the specified path: '{targetAssetPath}'.");
                        }

                        Texture2D imageReference;
                        if (referenceImage != null)
                        {
                            imageReference = referenceImage as Texture2D;
                            if (imageReference == null)
                                throw new ArgumentException($"{nameof(referenceImageInstanceId)} is not a valid {nameof(Texture2D)}.");
                        }
                        else
                        {
                            imageReference = (Texture2D)targetAsset;
                        }

                        var settings = new ImageSettings
                        {
                            RemoveBackground = false,
                            Width = width,
                            Height = height,
                            ImageReferences = new[] { new ObjectReference { Image = imageReference } }
                        };
                        var parameters = new GenerationParameters<ImageSettings>
                        {
                            AssetType = typeof(Texture2D),
                            Prompt = prompt,
                            SavePath = savePath,
                            ModelId = modelId,
                            Settings = settings,
                            TargetAsset = targetAsset,
                            PermissionCheckAsync = (path, cost) => context.Permissions.CheckAssetGeneration(path, typeof(Texture2D), cost)
                        };
                        generationHandle = AssetGenerators.GenerateAsync(parameters);
                        break;
                    }
                    default:
                        throw new ArgumentException($"Unsupported command: '{nameof(command)}'.");
                    case GenerationCommands.GenerateSpritesheet:
                    {
                        if (string.IsNullOrEmpty(prompt) || referenceImage == null)
                            throw new ArgumentException($"Both '{nameof(prompt)}' and '{nameof(referenceImageInstanceId)}' must be provided for Spritesheet generation.");

                        assetType = AssetTypes.Spritesheet;
                        var settings = new SpriteSettings { RemoveBackground = false, Width = width, Height = height, Loop = loop };

                        if (referenceImage != null)
                        {
                            if (referenceImage is not Texture2D)
                                throw new ArgumentException($"{nameof(referenceImageInstanceId)} is not a valid {nameof(Texture2D)}.");
                            settings.ImageReferences = new[] { new ObjectReference { Image = referenceImage } };
                        }

                        var parameters = new GenerationParameters<SpriteSettings>
                        {
                            AssetType = typeof(Texture2D),
                            Prompt = prompt,
                            SavePath = savePath,
                            ModelId = modelId,
                            Settings = settings,
                            PermissionCheckAsync = (path, cost) => context.Permissions.CheckAssetGeneration(path, typeof(Texture2D), cost)
                        };

                        if (!string.IsNullOrEmpty(targetAssetPath))
                        {
                            targetAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(targetAssetPath);
                            if (targetAsset == null)
                                throw new ArgumentException($"Failed to find a valid {nameof(Texture2D)} asset at the specified path: '{targetAssetPath}'.");
                            parameters.TargetAsset = targetAsset;
                        }

                        generationHandle = AssetGenerators.GenerateSpritesheetAsync(parameters, settings);
                        break;
                    }
                }

                await generationHandle.ValidationTask;
                if (generationHandle.Messages.Any())
                {
                    var errorMessages = string.Join("\n", generationHandle.Messages);
                    throw new Exception($"{assetType} operation failed. Details: {errorMessages}");
                }

                var placeholder = generationHandle.Placeholder;
                if (placeholder == null)
                    throw new Exception(Constants.FailedToCreatePlaceholder);

                InterruptedDownloadResumer.TrackDownload(generationHandle);

                if (!waitForCompletion)
                {
                    await generationHandle.GenerationTask;
                    if (generationHandle.Messages.Any())
                    {
                        var errorMessages = string.Join("\n", generationHandle.Messages);
                        throw new Exception($"{assetType} operation failed. Details: {errorMessages}");
                    }

                    var message = $"Placeholder for {assetType} '{placeholder.name}' created. Generation is in progress.";
                    if (targetAsset != null)
                        message = $"Modification for {assetType} '{placeholder.name}' is in progress.";

                    var placeholderPath = AssetDatabase.GetAssetPath(placeholder);
                    return new GenerateAssetOutput
                    {
                        Message = message,
                        AssetName = placeholder.name,
                        AssetPath = placeholderPath,
                        AssetGuid = AssetDatabase.AssetPathToGUID(placeholderPath),
                        AssetType = assetType,
                        FileInstanceID = placeholder.GetInstanceID(),
                        SubObjectInstanceID = GetOutputInstanceId(placeholder, assetType)
                    };
                }

                var finalAsset = await generationHandle;
                if (finalAsset == null || generationHandle.Messages.Any())
                {
                    var errorMessages = string.Join("\n", generationHandle.Messages);
                    throw new Exception($"{assetType} operation failed. Details: {errorMessages}");
                }

                // Defer ping after import to avoid conflicts with rapid asset switching
                EditorTask.delayCall += () =>
                {
                    if (finalAsset != null)
                        EditorGUIUtility.PingObject(finalAsset);
                };

                var finalMessage = targetAsset != null
                    ? $"Modification for {assetType} '{finalAsset.name}' completed successfully."
                    : $"{assetType} asset '{finalAsset.name}' generated successfully.";

                var finalAssetPath = AssetDatabase.GetAssetPath(finalAsset);
                return new GenerateAssetOutput
                {
                    Message = finalMessage,
                    AssetName = finalAsset.name,
                    AssetPath = finalAssetPath,
                    AssetGuid = AssetDatabase.AssetPathToGUID(finalAssetPath),
                    AssetType = assetType,
                    FileInstanceID = finalAsset.GetInstanceID(),
                    SubObjectInstanceID = GetOutputInstanceId(finalAsset, assetType)
                };
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                throw new Exception($"Error during asset operation: {ex.Message}", ex);
            }
        }

        internal static int GetOutputInstanceId(Object mainAsset, AssetTypes assetType)
        {
            if (mainAsset == null)
                return 0;

            var instanceId = mainAsset.GetInstanceID();
            if (assetType is AssetTypes.Sprite or AssetTypes.Image or AssetTypes.Spritesheet)
            {
                var assetPath = AssetDatabase.GetAssetPath(mainAsset);
                var sprites = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath).OfType<Sprite>().ToArray();
                if (sprites.Length > 0)
                {
                    instanceId = sprites[0].GetInstanceID();
                }
            }

            return instanceId;
        }

        [InitializeOnLoad]
        internal static class InterruptedDownloadResumer
        {
            static readonly string k_TokenFilePath = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "Temp", "ai_assistant_active_downloads_token");

            static int s_ActiveDownloads;

            static InterruptedDownloadResumer()
            {
                if (Application.isBatchMode || !File.Exists(k_TokenFilePath))
                    return;

                try
                {
                    var result = ManageInterruptedAssetGenerationsTool.ManageInterruptedAssetGenerations(ManageInterruptedAssetGenerationsCommands.Resume, true);

                    // If we tried to resume but found nothing, the token file was stale. Clean it up.
                    if (result.Generations == null || result.Generations.Length == 0)
                    {
                        RemoveToken();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    // If resuming fails, remove the token to prevent a resume loop on next reload.
                    RemoveToken();
                }
            }

            public static void TrackDownload(GenerationHandle<Object> generationHandle)
            {
                IncrementCounter();
                generationHandle.DownloadTask.ContinueWith(_ => DecrementCounter(), TaskScheduler.FromCurrentSynchronizationContext());
            }

            static void IncrementCounter()
            {
                s_ActiveDownloads++;
                if (s_ActiveDownloads > 0)
                {
                    CreateToken();
                }
            }

            static void DecrementCounter()
            {
                s_ActiveDownloads--;
                if (s_ActiveDownloads <= 0)
                {
                    s_ActiveDownloads = 0;
                    RemoveToken();
                }
            }

            [ToolPermissionIgnore]  // To ignore file creation permission check
            static void CreateToken()
            {
                try
                {
                    if (!File.Exists(k_TokenFilePath))
                        using (var _ = File.Create(k_TokenFilePath)) { }
                }
                catch
                {
                    // Error logging is optional and can be noisy.
                }
            }

            [ToolPermissionIgnore]  // To ignore file creation permission check
            static void RemoveToken()
            {
                try
                {
                    if (File.Exists(k_TokenFilePath))
                        File.Delete(k_TokenFilePath);
                }
                catch
                {
                    // Error logging is optional and can be noisy.
                }
            }
        }
    }
}
