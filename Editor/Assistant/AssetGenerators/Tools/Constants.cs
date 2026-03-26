using System;
using Unity.AI.Generators.Tools;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class Constants
    {
        public static class AssetTypeNames
        {
            public const string HumanoidAnimation = "HumanoidAnimation";
            public const string Cubemap = "Cubemap";
            public const string Material = "Material";
            public const string Mesh = "Mesh";
            public const string Sound = "Sound";
            public const string Sprite = "Sprite";
            public const string Image = "Image";
            public const string Spritesheet = "Spritesheet";
            public const string TerrainLayer = "TerrainLayer";
        }

        public static class CommandNames
        {
            public const string GenerateHumanoidAnimation = "Generate" + AssetTypeNames.HumanoidAnimation;
            public const string GenerateCubemap = "Generate" + AssetTypeNames.Cubemap;
            public const string UpscaleCubemap = "Upscale" + AssetTypeNames.Cubemap;
            public const string GenerateMaterial = "Generate" + AssetTypeNames.Material;
            public const string AddPbrToMaterial = "AddPbrTo" + AssetTypeNames.Material;
            public const string GenerateMesh = "Generate" + AssetTypeNames.Mesh;
            public const string GenerateSound = "Generate" + AssetTypeNames.Sound;
            public const string GenerateSprite = "Generate" + AssetTypeNames.Sprite;
            public const string GenerateImage = "Generate" + AssetTypeNames.Image;
            public const string GenerateSpritesheet = "Generate" + AssetTypeNames.Spritesheet;
            public const string RemoveSpriteBackground = "Remove" + AssetTypeNames.Sprite + "Background";
            public const string RemoveImageBackground = "Remove" + AssetTypeNames.Image + "Background";
            public const string EditSpriteWithPrompt = "Edit" + AssetTypeNames.Sprite + "WithPrompt";
            public const string EditImageWithPrompt = "Edit" + AssetTypeNames.Image + "WithPrompt";
            public const string GenerateTerrainLayer = "Generate" + AssetTypeNames.TerrainLayer;
            public const string AddPbrToTerrainLayer = "AddPbrTo" + AssetTypeNames.TerrainLayer;
        }

        // command
        public const string CommandDescription = "The specific generation or editing command to execute. Supported values are: " +
            "'" + CommandNames.GenerateHumanoidAnimation + "', " +
            "'" + CommandNames.GenerateCubemap + "', '" + CommandNames.UpscaleCubemap + "', " +
            "'" + CommandNames.GenerateMaterial + "', '" + CommandNames.AddPbrToMaterial + "', " +
            "'" + CommandNames.GenerateMesh + "', " +
            "'" + CommandNames.GenerateSound + "', " +
            "'" + CommandNames.GenerateSprite + "', '" + CommandNames.GenerateImage + "', '" + CommandNames.GenerateSpritesheet + "', " +
            "'" + CommandNames.RemoveSpriteBackground + "', '" + CommandNames.RemoveImageBackground + "', " +
            "'" + CommandNames.EditSpriteWithPrompt + "', '" + CommandNames.EditImageWithPrompt + "', " +
            "'" + CommandNames.GenerateTerrainLayer + "', '" + CommandNames.AddPbrToTerrainLayer + "'.";

        // general
        const string k_GenerateAssetGeneralDescription = "Generates or modifies a Unity asset. " +
            "The specific action is determined by the 'command' parameter. " +
            "The model id parameter requires a valid model ID from the list of available models for asset generation. " +
            "For optimal performance, you are strongly encouraged to generate multiple independent assets in parallel by issuing multiple tool calls simultaneously in a single response.";

        const string k_GenerateSpriteDescription =
            "To generate a sprite, use '" + CommandNames.GenerateSprite + "' you should then call '" + CommandNames.RemoveImageBackground + "'. " +
            "To generate an image (e.g., a portrait or environment), use '" + CommandNames.GenerateImage + "'. " +
            "To remove the background from an existing sprite or image, use '" + CommandNames.RemoveImageBackground + "'. " +
            "To edit a sprite using a prompt, use '" + CommandNames.EditSpriteWithPrompt + "'. " +
            "To edit an image using a prompt, use '" + CommandNames.EditImageWithPrompt + "'. " +
            "To edit a sprite using a prompt into a sprite sheet (" + AssetTypeNames.Spritesheet + "), use '" + CommandNames.GenerateSpritesheet + "'. " +
            "All editing commands require a target asset path.";

        const string k_GenerateCubemapDescription =
            "To generate a cubemap, use '" + CommandNames.GenerateCubemap + "'. To upscale an existing cubemap, use '" + CommandNames.UpscaleCubemap + "' and provide the target asset path.";

        const string k_GenerateMaterialDescription =
            "To generate a base material (texture only), use '" + CommandNames.GenerateMaterial + "'. To add PBR maps to an existing material, use '" + CommandNames.AddPbrToMaterial + "' and provide the target asset path. " +
            "The same applies to '" + CommandNames.GenerateTerrainLayer + "' and '" + CommandNames.AddPbrToTerrainLayer + "'. " +
            "For base " + AssetTypeNames.Material + " and " + AssetTypeNames.TerrainLayer + " generation, it's highly recommended to ask for the list of available composition patterns to use as a starting point.";

        const string k_GenerateMeshDescription =
            "To generate a 3D model, use '" + CommandNames.GenerateMesh + "'. This command generates a self-contained Prefab asset that includes the generated mesh and a corresponding material, ready to be used in a scene.";

        public const string GenerateAssetFunctionDescription = k_GenerateAssetGeneralDescription + " " + k_GenerateSpriteDescription + " " + k_GenerateCubemapDescription + " " + k_GenerateMaterialDescription + " " + k_GenerateMeshDescription;

        // common
        public const string ModelIdDescription = "A mandatory model unique ID for the generation. Must be retrieved from the list of available models for asset generation. Do not guess or invent model IDs.";
        public const string PromptDescription = "A description of the asset to generate. For example: 'a sci-fi crate' for a " + AssetTypeNames.Mesh + ", or 'a cute cat' for a " + AssetTypeNames.Sprite + ". " +
            "For " + AssetTypeNames.Material + " and " + AssetTypeNames.TerrainLayer + ", this can be used to describe the desired look, and can be combined with a 'referenceImagePath' for more detailed control.";
        public const string SavePathDescription = "The full project path, including file name and extension, where a NEW asset should be created and saved. " +
            "This path is primarily used with 'Generate*' commands. " +
            "Examples: " +
            "HumanoidAnimation: \"Assets/Animations/MyNewAnimation.anim\", " +
            "Cubemap: \"Assets/Cubemaps/MyNewCubemap.png\", " +
            "Material: \"Assets/Materials/MyNewMaterial.mat\", " +
            "Mesh: \"Assets/Models/MyNew3DObject.prefab\", " +
            "Sound: \"Assets/Audio/MyNewSound.wav\", " +
            "Sprite: \"Assets/Sprites/MyNewSprite.png\", " +
            "TerrainLayer: \"Assets/TerrainLayers/MyNewTerrainLayer.terrainlayer\". " +
            "If 'savePath' is provided during an 'Edit*' or 'Upscale*' command, a new asset will be created at this path instead of modifying the original asset specified in target asset path.";
        public const string TargetAssetPathDescription = "The project path to an EXISTING asset that will be directly modified, edited, or used as the subject of an operation. " +
            "This is required for commands like '" + CommandNames.EditSpriteWithPrompt + "', '" + CommandNames.UpscaleCubemap + "', '" + CommandNames.RemoveImageBackground + "', '" + CommandNames.AddPbrToMaterial + "', and '" + CommandNames.AddPbrToTerrainLayer + "'.";
        public const string ReferenceImagePathDescription = "The project path to an EXISTING image (Texture2D) that will be used as inspiration or a visual guide for generating a NEW asset. " +
            "The reference image itself is NOT modified. This is used with asset conversion operations.";
        public const string ReferenceImageInstanceIdDescription = "The instance ID of an EXISTING image (Texture2D) that will be used as inspiration or a visual guide for generating a NEW asset. " +
            "The reference image itself is NOT modified. This is used with commands like '" + CommandNames.GenerateSpritesheet + "', '" + CommandNames.GenerateMesh + "' or '" + CommandNames.GenerateMaterial + "'. " +
            "For " + AssetTypeNames.Material + " and " + AssetTypeNames.TerrainLayer + ", you can ask for a list of available composition patterns and use their instance ID here.";
        public const string WaitForCompletionDescription = "When issuing multiple tool calls simultaneously to generate independent assets, this wait applies to the entire group of parallel requests. " +
            "To ensure subsequent operations are performed on valid, completed generations, this should be set to '" + TrueString + "'. " +
            "Setting this to '" + FalseString + "' returns a placeholder asset immediately while the generation continues in the background; this is recommended only for expert workflows where handling incomplete assets is required.";

        // optional
        public const string ForceGenerationDescription = "If set to '" + TrueString + "', generation will proceed even if there are interrupted downloads. This should only be used if the user has explicitly confirmed they want to proceed without resuming or discarding. Defaults to '" + FalseString + "'. Valid values are '" + TrueString + "' or '" + FalseString + "'.";
        public const string DurationInSecondsDescription = "The desired duration of the " + AssetTypeNames.Sound + " in seconds. Between 1 and 10 seconds.";
        public const string LoopDescription = "For " + AssetTypeNames.Sound + " and " + AssetTypeNames.Spritesheet + " generation. If '" + TrueString + "', the generated asset will be seamlessly loopable. For " + AssetTypeNames.Sound + ", this requires a model that supports audio looping. Valid values are '" + TrueString + "' or '" + FalseString + "'.";
        public const string SpriteWidthDescription = "(Optional) The desired width of the generated " + AssetTypeNames.Sprite + " in pixels. Values from 1024 to 4096 are preferred.";
        public const string SpriteHeightDescription = "(Optional) The desired height of the generated " + AssetTypeNames.Sprite + " in pixels. Values from 1024 to 4096 are preferred.";

        // errors
        public const string PromptRequired = "'prompt' parameter is required.";
        public const string FailedToCreatePlaceholder = "Failed to create placeholder asset.";

        // models
        public const string GetAssetGenerationModelsFunctionDescription = "Gets a list of available models for asset generation. When asked for a model list always include the ModelId guid in the response.";
        public const string IncludeAllModelsParameterDescription = "If " + FalseString + " (default), returns a curated list of recommended models. If " + TrueString + ", returns all available models. Setting this to " + TrueString + " is very costly. Valid values are '" + TrueString + "' or '" + FalseString + "'.";

        // interrupted generations
        public const string ManageInterruptedAssetGenerationsFunctionDescription = "Manages interrupted asset generations. Can be used to check for or resume pending generations.";
        public const string ManageInterruptedAssetGenerationsCommandDescription = "The command to perform. Supported values are: '" +
            ListCommand + "', '" + ResumeCommand + "', '" + DiscardCommand + "'.";

        public const string AssetGenerationFunctionTag = "smart-context";

        public const string TrueString = "true";
        public const string FalseString = "false";

        // interrupted generations commands
        public const string ListCommand = "List";
        public const string ResumeCommand = "Resume";
        public const string DiscardCommand = "Discard";

        public static AssetTypes GetAssetType(Type type)
        {
            if (type == typeof(AnimationClip)) return AssetTypes.HumanoidAnimation;
            if (type == typeof(Cubemap)) return AssetTypes.Cubemap;
            if (type == typeof(Material)) return AssetTypes.Material;
            if (type == typeof(GameObject)) return AssetTypes.Mesh;
            if (type == typeof(AudioClip)) return AssetTypes.Sound;
            if (type == typeof(Texture2D)) return AssetTypes.Sprite; // Or Image, or Spritesheet
            if (type == typeof(TerrainLayer)) return AssetTypes.TerrainLayer;
            throw new ArgumentException($"Unsupported asset type: '{type}'.");
        }

		// audio
        public const string EditAudioDescription =
            "Modifies an Audio Clip asset. The specific action is determined by a command parameter. " +
            "To remove the silences from the start and end of an audio clip, use '" + nameof(AssetGenerators.AudioCommands.TrimSilence) + "'. " +
            "To trim an Audio Clip from a start time and an end time (in seconds), use '" + nameof(AssetGenerators.AudioCommands.TrimSound) + "'. " +
            "To increase or decrease the volume of an audio clip, use '" + nameof(AssetGenerators.AudioCommands.ChangeVolume) + "'. " +
            "To create a seamless loop from an audio clip, use '" + nameof(AssetGenerators.AudioCommands.LoopSound) + "' It is better to trim the silences from the beginning and the end of an AudioClip before using this command. ";

        public const string AudioCommandDescription = "The specific audio command to execute. Supported values are: " +
            "'" + nameof(AssetGenerators.AudioCommands.TrimSilence) + "', " +
            "'" + nameof(AssetGenerators.AudioCommands.TrimSound) + "', " +
            "'" + nameof(AssetGenerators.AudioCommands.ChangeVolume) + "', " +
            "'" + nameof(AssetGenerators.AudioCommands.LoopSound) + "'.";

        // animation
        public const string EditAnimationDescription =
            "Modifies a Unity humanoid Animation Clip asset. The specific action is determined by the 'command' parameter. " +
            "Only Unity humanoid animation clips are supported. " +
            "To make an animation stationary (remove root motion), use '" + nameof(AnimationCommands.MakeStationary) + "'. " +
            "To find the best loop in an animation and trim the clip to that section, use '" + nameof(AnimationCommands.TrimToBestLoop) + "'.";

        public const string AnimationCommandDescription = "The specific animation command to execute. Supported values are: " +
            "'" + nameof(AnimationCommands.MakeStationary) + "', " +
            "'" + nameof(AnimationCommands.TrimToBestLoop) + "'.";
    }

    enum AssetTypes
    {
        None = 0,
        HumanoidAnimation,
        Cubemap,
        Material,
        Mesh,
        Sound,
        Sprite,
        Image,
        TerrainLayer,
        Spritesheet,
        SpriteAnimation,
        AnimatorController
    }

    [Serializable]
    class AssetOutputBase
    {
        public string Message;
        public string AssetPath;
        public string AssetGuid;
    }

    [Serializable]
    class GenerateAssetOutput : AssetOutputBase
    {
        public string AssetName;
        public AssetTypes AssetType;
        public int FileInstanceID;
        public int SubObjectInstanceID;
    }

    [Serializable]
    struct ManageInterruptedAssetGenerationsOutput
    {
        public string Message;
        public GenerateAssetOutput[] Generations;
    }

    enum ManageInterruptedAssetGenerationsCommands
    {
        List,
        Resume,
        Discard
    }
}
