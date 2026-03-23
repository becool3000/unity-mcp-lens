using System;
using System.Collections.Generic;
using AiEditorToolsSdk.Components.Common.Enums;

namespace Unity.AI.ModelSelector.Services.Stores.States
{
    static class ModelConstants
    {
        public static class Providers
        {
            public const string None = "None";
            public const string Unity = "Unity";
            public const string Scenario = "Scenario";
            public const string Layer = "Layer";

            public static IEnumerable<string> EnumerateAll()
            {
                yield return None;
                yield return Unity;
                yield return Scenario;
                yield return Layer;
            }
        }

        public static class Modalities
        {
            public const string None = "None";
            public const string Image = "Image";
            public const string Texture2d = "Texture2d";
            public const string Sound = "Sound";
            public const string Animate = "Animate";
            public const string Skybox = "Skybox";
            public const string Model3D = "Model3d";
            public const string Video = "Video";

            public static IEnumerable<string> EnumerateAll()
            {
                yield return None;
                yield return Image;
                yield return Texture2d;
                yield return Sound;
                yield return Animate;
                yield return Skybox;
                yield return Model3D;
                yield return Video;
            }
        }

        public static class Operations
        {
            public const string None = "None";

            ////////////////////////////////////// Generative

            /// <summary>
            /// Generic text prompt
            /// </summary>
            public const string TextPrompt = "TextPrompt";

            /// <summary>
            /// Generic single reference
            /// </summary>
            public const string ReferencePrompt = "ReferencePrompt";

            // Specific Image references
            public const string StyleReference = "StyleReference";
            public const string CompositionReference = "CompositionReference";
            public const string PoseReference = "PoseReference";
            public const string DepthReference = "DepthReference";
            public const string LineArtReference = "LineArtReference";
            public const string FeatureReference = "FeatureReference";
            public const string MaskReference = "MaskReference";
            public const string RecolorReference = "RecolorReference";
            public const string GenerativeUpscale = "GenerativeUpscale";
            public const string TextureUpscale = "TextureUpscale";

            // Specific Stylesheet references
            public const string FirstFrameReference = "FirstFrameReference";
            public const string LastFrameReference = "LastFrameReference";

            // Specific Animate references
            public const string MotionFrameReference = "MotionFrameReference";

            // Specific Texture2D references
            public const string Pbr = "Pbr";

            ////////////////////////////////////// Transformative
            public const string Pixelate = "Pixelate";
            public const string RemoveBackground = "RemoveBackground";
            public const string Upscale = "Upscale";
            public const string SkyboxUpscale = "SkyboxUpscale";


            public static IEnumerable<string> EnumerateAll()
            {
                yield return None;
                yield return TextPrompt;
                yield return ReferencePrompt;
                yield return StyleReference;
                yield return CompositionReference;
                yield return PoseReference;
                yield return DepthReference;
                yield return LineArtReference;
                yield return FeatureReference;
                yield return MaskReference;
                yield return RecolorReference;
                yield return GenerativeUpscale;
                yield return MotionFrameReference;
                yield return Pbr;
                yield return Pixelate;
                yield return RemoveBackground;
                yield return Upscale;
                yield return SkyboxUpscale;
                yield return TextureUpscale;
            }
        }

        public static ProviderEnum ConvertToProvider(string provider)
        {
            return Enum.TryParse<ProviderEnum>(provider, out var result) ? result : ProviderEnum.None;
        }

        public static ModalityEnum ConvertToModality(string modality)
        {
            return Enum.TryParse<ModalityEnum>(modality, out var result) ? result : ModalityEnum.None;
        }

        public static OperationSubTypeEnum ConvertToOperation(string operation)
        {
            return Enum.TryParse<OperationSubTypeEnum>(operation, out var result) ? result : OperationSubTypeEnum.None;
        }

        // replace Texture2d with Material when needing to resolve ambiguities caused by Texture2d with agents
        public const string Material = "Material";
        public const string Cubemap = "Cubemap";

        public static class ModelCapabilities
        {
            public const string EditWithPrompt = "EditWithPrompt";
            public const string SingleInputImage = "SingleInputImage";
            public const string CustomResolutions = "CustomResolutions";
            public const string AssistantFavorite = "AssistantFavorite";
            public const string SupportsLooping = "SupportsLooping";
            public const string Supports9SliceUI = "Supports9SliceUI";

            public const int CustomResolutionsMin = 1024;
            public const int CustomResolutionsMax = 4096;
        }

        public static class ModelLimitations
        {
            public const string Requires300PxReference = "Requires300pxReference";
            public const string Requires5Or10SecondDuration = "Requires5Or10SecondDuration";
        }
    }
}
