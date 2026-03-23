using System.IO;
using HFHubClient;

#if SENTIS_AVAILABLE
using Unity.InferenceEngine;
#endif

namespace Unity.AI.Search.Editor.Services.Models
{
    abstract record ModelInfo(int size, string id, string rootFolder, int suggestedBatchSize = 1)
    {
        protected string GetModelFile(string filename) => Path.Combine(rootFolder, filename);

        public abstract string[] GetRequiredFiles();
    }

    record SigLipModelInfo(int size, string id, string rootFolder) : ModelInfo(size, id, rootFolder, SigLipModelInfo.BatchSize)
    {
#if SENTIS_AVAILABLE
#if ASSETKNOWLEDGE_COMPUTE_SHADERS_ENABLED  // Enable when this is ready: ASST-2775
        const int BatchSize = 32;
        public const BackendType ModelBackendType = BackendType.GPUCompute;
#else
        const int BatchSize = 1;
        public const BackendType ModelBackendType = BackendType.GPUPixel;
#endif
#else
        const int BatchSize = 1;
#endif

        const int k_Quantization = 32;

        public string OnnxImageModelPath => GetModelFile($"siglip2_image_fp{k_Quantization}.onnx");
        public string OnnxTextModelPath => GetModelFile($"siglip2_text_fp{k_Quantization}.onnx");
        public string OptimizedImageModelPath => GetModelFile($"siglip2_image_fp{k_Quantization}.inferenceengine");
        public string OptimizedTextModelPath => GetModelFile($"siglip2_text_fp{k_Quantization}.inferenceengine");
        public string TokenizerPath => GetModelFile("tokenizer.model");

        public bool HasOptimizedImageModel => File.Exists(OptimizedImageModelPath);
        public bool HasOptimizedTextModel => File.Exists(OptimizedTextModelPath);
        public bool HasOnnxImageModel => File.Exists(OnnxImageModelPath);
        public bool HasOnnxTextModel => File.Exists(OnnxTextModelPath);

        public override string[] GetRequiredFiles()
        {
            return new[]
            {
                OptimizedImageModelPath,
                OptimizedTextModelPath,
                TokenizerPath
            };
        }

        public static SigLipModelInfo Create(int size) =>
            new SigLipModelInfo(size, $"bopbopbop123/siglip2-base-patch16-{size}-onnx",
                Paths.ModelPath($"bopbopbop123/siglip2-base-patch16-{size}-onnx"));
    }
}
