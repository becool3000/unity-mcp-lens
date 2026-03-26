#if SENTIS_AVAILABLE
using Unity.InferenceEngine;
using UnityEditor;

namespace Unity.AI.Search.Editor.Services.Models
{
    interface ITextHandler
    {
        Model Load();
        bool CanLoad { get; }
    }

    record TextHandlerOnnx : ITextHandler
    {
        public Model Load() => ModelLoader.Load(AssetDatabase.LoadAssetAtPath<ModelAsset>(SigLip2.ModelInfo.OnnxTextModelPath));

        public bool CanLoad => SigLip2.ModelInfo.HasOnnxTextModel;
    }

    record TextHandlerOptimized : ITextHandler
    {
        public Model Load() => ModelLoader.Load(SigLip2.ModelInfo.OptimizedTextModelPath);

        public bool CanLoad => SigLip2.ModelInfo.HasOptimizedTextModel;
    }
}
#endif