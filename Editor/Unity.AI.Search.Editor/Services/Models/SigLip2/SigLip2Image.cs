#if SENTIS_AVAILABLE
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Unity.InferenceEngine;
using Unity.AI.Search.Editor.Utilities;

namespace Unity.AI.Search.Editor.Services.Models
{
    class SigLip2Image : IDisposable
    {
        // Protect against too many concurrent tasks as it can lead to invalid output tensors
        const int k_MaxConcurrentTasks = 1;

        static readonly SemaphoreSlim k_ConcurrencyLimiter =
            new SemaphoreSlim(k_MaxConcurrentTasks, k_MaxConcurrentTasks);

        IImageHandler m_Handler;
        Model m_ImageModel;
        Worker m_ImageWorker;

        public bool CanLoad()
        {
            TryInit();
            return m_Handler?.CanLoad ?? false;
        }

        public void TryInit()
        {
            if (m_Handler != null) return;

            if (SigLip2.ModelInfo.HasOptimizedImageModel)
                m_Handler = new ImageHandlerOptimized();
            else if (SigLip2.ModelInfo.HasOnnxImageModel)
                m_Handler = new ImageHandlerOnnx();
        }

        void EnsureModelLoaded()
        {
            if (m_ImageModel == null)
            {
                m_ImageModel = m_Handler.Load();
                m_ImageWorker = new Worker(m_ImageModel, SigLipModelInfo.ModelBackendType);
            }
        }

        public async Task<float[]> GetImageEmbeddings(Texture2D image) =>
            (await GetImageEmbeddings(new[] { image }))[0];

        public async Task<float[][]> GetImageEmbeddings(Texture2D[] images)
        {
            if (images == null || images.Length == 0)
                return Array.Empty<float[]>();

            EnsureModelLoaded();

            var width = images[0].width;
            var height = images[0].height;
            // Validate all images have the same dimensions
            if (!images.All(img => img.height == height && img.width == width))
                throw new ArgumentException("All images in batch must have the same dimensions");

            await k_ConcurrencyLimiter.WaitAsync();

            try
            {
                using var input = await m_Handler.PreprocessInput(images,
                    SigLip2.ModelInfo.size,
                    SigLip2.ModelInfo.size);

                var embeddings = await RunImageInference(input);

                return embeddings;
            }
            finally
            {
                k_ConcurrencyLimiter.Release();
            }
        }

        async Task<float[][]> RunImageInference(Tensor<float> input)
        {
            var batchSize = input.shape[0];
            m_ImageWorker.Schedule(input);

            Tensor<float> outputTensor;
            while ((outputTensor = m_ImageWorker.PeekOutput() as Tensor<float>) == null)
            {
                await Task.Yield();
            }

            // Convert output tensor to embeddings
            return await TensorUtils.OutputTensorToEmbeddings(outputTensor, batchSize);
        }

        public void Dispose()
        {
            m_ImageWorker?.Dispose();
            m_ImageWorker = null;
            m_ImageModel = null;
            // Note: Don't dispose static semaphore as it's shared across all instances
        }
    }
}
#endif