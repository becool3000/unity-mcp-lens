#if SENTIS_AVAILABLE
using System.Threading.Tasks;
using Unity.AI.Search.Editor.Utilities;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Search.Editor.Services.Models
{
    interface IImageHandler
    {
        Model Load();
        Task<Tensor<float>> PreprocessInput(Texture2D[] images, int width, int height);
        bool CanLoad { get; }
    }

    record ImageHandlerOnnx : IImageHandler
    {
        public Model Load() => ModelLoader.Load(AssetDatabase.LoadAssetAtPath<ModelAsset>(SigLip2.ModelInfo.OnnxImageModelPath));

        public Task<Tensor<float>> PreprocessInput(Texture2D[] images, int width, int height) =>
            TensorUtils.TexturesToBatchTensorInternal(images, height, width, true);

        public bool CanLoad => SigLip2.ModelInfo.HasOnnxImageModel;
    }

    record ImageHandlerOptimized : IImageHandler
    {
        public Model Load() => ModelLoader.Load(SigLip2.ModelInfo.OptimizedImageModelPath);

        public Task<Tensor<float>> PreprocessInput(Texture2D[] images, int width, int height) =>
            TensorUtils.TexturesToBatchTensorInternal(images, height, width, false);

        public bool CanLoad => SigLip2.ModelInfo.HasOptimizedImageModel;
    }
}
#endif