using System;
using System.Threading.Tasks;
using Object = UnityEngine.Object;
using UnityEngine;

namespace Unity.AI.Generators.Tools
{
    /// <summary>
    /// A set of parameters for an asset generation request, strongly typed to a specific set of settings.
    /// </summary>
    /// <typeparam name="TSettings">The type of the generator-specific settings (e.g., AnimationSettings).</typeparam>
    class GenerationParameters<TSettings> where TSettings : ISettings
    {
        /// <summary>
        /// The type of asset to generate.
        /// Supported types:
        /// <list type="bullet">
        /// <item><description><c>typeof(AnimationClip)</c></description></item>
        /// <item><description><c>typeof(Texture2D)</c> (Sprite)</description></item>
        /// <item><description><c>typeof(AudioClip)</c></description></item>
        /// <item><description><c>typeof(Material)</c></description></item>
        /// <item><description><c>typeof(TerrainLayer)</c></description></item>
        /// <item><description><c>typeof(GameObject)</c> (Mesh/Prefab)</description></item>
        /// </list>
        /// </summary>
        public Type AssetType;

        /// <summary>
        /// The text prompt describing the desired asset.
        /// </summary>
        public string Prompt;

        /// <summary>
        /// Optional. The path where the generated asset should be saved. If null, a temporary asset will be created.
        /// </summary>
        public string SavePath;

        /// <summary>
        /// Optional. The ID of the model to use for generation. If null, a default model for the modality will be used.
        /// </summary>
        public string ModelId;

        /// <summary>
        /// Optional. An existing asset to be modified. If provided, generation will operate on this asset.
        /// </summary>
        public Object TargetAsset;

        /// <summary>
        /// A strongly-typed struct containing settings specific to the asset type being generated.
        /// </summary>
        public TSettings Settings;

        /// <summary>
        /// Optional. An asynchronous callback to perform permission checks before generation begins.
        /// The callback receives the final, resolved asset path and the estimated points cost.
        /// </summary>
        public Func<string, long, Task> PermissionCheckAsync;
    }

    /// <summary>
    /// A marker interface for structs containing generator-specific settings.
    /// </summary>
    interface ISettings { }

    /// <summary>
    /// Settings specific to AnimationClip generation.
    /// </summary>
    class AnimationSettings : ISettings
    {
        /// <summary>
        /// Optional. The desired duration of a generated sound or animation in seconds.
        /// </summary>
        public float DurationInSeconds;
    }

    /// <summary>
    /// Settings specific to AudioClip generation.
    /// </summary>
    class SoundSettings : ISettings
    {
        /// <summary>
        /// Optional. The desired duration of a generated sound or animation in seconds.
        /// </summary>
        public float DurationInSeconds;

        /// <summary>
        /// Optional. If true, the generated audio will be seamlessly loopable. Requires a model that supports looping.
        /// </summary>
        public bool Loop;
    }

    /// <summary>
    /// Represents an image reference for generation, consisting of an image asset and an optional label.
    /// </summary>
    struct ObjectReference
    {
        /// <summary>
        /// The image asset (e.g., Texture2D).
        /// </summary>
        public Object Image;

        /// <summary>
        /// A label for future use, to specify the type of reference (e.g., "style", "pose").
        /// </summary>
        public string Label;
    }
    
    /// <summary>
    /// Settings specific to Texture2D (Image) generation.
    /// </summary>
    class ImageSettings : ISettings
    {
        /// <summary>
        /// Optional. The desired width of the generated image. If 0, a default value will be used. Requires ModelId to be specified.
        /// </summary>
        public int Width;

        /// <summary>
        /// Optional. The desired height of the generated image. If 0, a default value will be used. Requires ModelId to be specified.
        /// </summary>
        public int Height;

        /// <summary>
        /// Optional. If true, the background of the generated image will be removed.
        /// </summary>
        public bool RemoveBackground;

        /// <summary>
        /// Optional. An array of image references to use for generation.
        /// Only the first element (ImageReferences[0]) is currently used as a prompt image reference.
        /// </summary>
        public ObjectReference[] ImageReferences;

        /// <summary>
        /// Optional. If true, the generated sheet will be seamlessly loopable.
        /// </summary>
        public bool Loop;
    }    

    /// <summary>
    /// Settings specific to Texture2D (Sprite) generation.
    /// </summary>
    class SpriteSettings : ISettings
    {
        /// <summary>
        /// Optional. The desired width of the generated sprite. If 0, a default value will be used. Requires ModelId to be specified.
        /// </summary>
        public int Width;

        /// <summary>
        /// Optional. The desired height of the generated sprite. If 0, a default value will be used. Requires ModelId to be specified.
        /// </summary>
        public int Height;

        /// <summary>
        /// Optional. If true, the background of a generated sprite will be removed. Only applies to Texture2D generation.
        /// </summary>
        public bool RemoveBackground;

        /// <summary>
        /// Optional. An array of image references to use for generation.
        /// Only the first element (ImageReferences[0]) is currently used as a prompt image reference.
        /// This is for future extensibility; support for multiple references may be added later.
        /// </summary>
        public ObjectReference[] ImageReferences;

        /// <summary>
        /// Optional. If true, the generated spritesheet will be seamlessly loopable.
        /// </summary>
        public bool Loop;
    }

    /// <summary>
    /// Settings specific to Material generation.
    /// </summary>
    class MaterialSettings : ISettings
    {
        /// <summary>
        /// Optional. An array of image references to use for generation.
        /// Only the first element (ImageReferences[0]) is currently used as a prompt image reference.
        /// This is for future extensibility; support for multiple references may be added later.
        /// </summary>
        public ObjectReference[] ImageReferences;
    }

    /// <summary>
    /// Settings specific to TerrainLayer generation.
    /// </summary>
    class TerrainLayerSettings : ISettings
    {
        /// <summary>
        /// Optional. An array of image references to use for generation.
        /// Only the first element (ImageReferences[0]) is currently used as a prompt image reference.
        /// This is for future extensibility; support for multiple references may be added later.
        /// </summary>
        public ObjectReference[] ImageReferences;
    }

    /// <summary>
    /// Settings specific to Texture2D (Cubemap) generation.
    /// </summary>
    class CubemapSettings : ISettings
    {
        /// <summary>
        /// Optional. If true, the generated cubemap will be upscaled.
        /// </summary>
        public bool Upscale;
    }

    /// <summary>
    /// Settings specific to Mesh (GameObject) generation.
    /// </summary>
    class MeshSettings : ISettings
    {
        /// <summary>
        /// Optional. An array of image references to use for generation.
        /// Only the first element (ImageReferences[0]) is currently used as a prompt image reference.
        /// This is for future extensibility; support for multiple references may be added later.
        /// </summary>
        public ObjectReference[] ImageReferences;
    }
}
