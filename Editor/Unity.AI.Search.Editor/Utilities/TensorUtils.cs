#if SENTIS_AVAILABLE
using System.Threading.Tasks;
using Unity.AI.Search.Editor.Knowledge;
using Unity.Collections;
using UnityEngine;
using Unity.InferenceEngine;

namespace Unity.AI.Search.Editor.Utilities
{
    static class TensorUtils
    {
        /// <summary>
        /// Converts pixel values from [0,1] range to [-1,1] range by applying: (pixel * 2.0) - 1.0
        /// This normalization is commonly used in computer vision models.
        /// </summary>
        /// <param name="pixelValue">Pixel value in [0,1] range</param>
        /// <returns>Normalized pixel value in [-1,1] range</returns>
        static float PixelToMean(float pixelValue) => (pixelValue * 2.0f) - 1.0f;

        public static FunctionalTensor PixelToMean(FunctionalTensor f) => f * 2.0f - 1.0f;

        public static async Task<Tensor<float>> TexturesToBatchTensorInternal(Texture2D[] images, int height, int width,
            bool applyPixelToMean)
        {
            var batchSize = images.Length;
            var imageSize = 3 * height * width;

            // Create batch tensor data array
            var batchData = new float[batchSize * imageSize];

            var transform = new TextureTransform()
                .SetCoordOrigin(CoordOrigin.TopLeft)
                .SetTensorLayout(TensorLayout.NCHW)
                .SetChannelSwizzle(ChannelSwizzle.RGBA);

            // Convert each texture to its portion of the batch data
            for (var i = 0; i < batchSize; i++)
            {
                using var imageConverter = new Tensor<float>(new TensorShape(1, 3, height, width));
                // Create single image tensor for this image
                TextureConverter.ToTensor(images[i], imageConverter, transform);

                Tensor<float> imageData;
                if (!AssetKnowledgeSettings.RunAsync)
                {
                    imageData = imageConverter.ReadbackAndClone();
                }
                else
                {
                    imageData = await imageConverter.ReadbackAndCloneAsync();

                    // ReadbackAndCloneAsync can survive domain reloads!
                    // Yield so execution stops if a domain reload happened:
                    if (AssetKnowledgeSettings.RunAsync)
                        await Task.Yield();
                }

                if (applyPixelToMean)
                {
                    // Copy data directly to the correct position in batch array
                    var batchOffset = i * imageSize;
                    for (var j = 0; j < imageSize; j++)
                        batchData[batchOffset + j] = PixelToMean(imageData[j]);
                }
                else
                {
                    NativeArray<float>.Copy(
                        imageData.AsReadOnlyNativeArray(),
                        0, batchData,
                        i * imageSize,
                        imageSize);
                }

                imageData.Dispose();
            }

            // Create batch tensor from the complete data
            return new Tensor<float>(new TensorShape(batchSize, 3, height, width), batchData);
        }

        /// <summary>
        /// Converts a batched output tensor to an array of float arrays representing embeddings.
        /// </summary>
        /// <param name="outputTensor">Output tensor from model inference</param>
        /// <param name="batchSize">Number of items in the batch</param>
        /// <returns>Array of embedding arrays, one per batch item</returns>
        public static async Task<float[][]> OutputTensorToEmbeddings(Tensor<float> outputTensor, int batchSize)
        {
            // Read output and convert to batch of embeddings
            Tensor<float> output;
            if (!AssetKnowledgeSettings.RunAsync)
                output = outputTensor.ReadbackAndClone();
            else
                output = await outputTensor.ReadbackAndCloneAsync();

            var embeddingDim = output.count / batchSize;
            var result = new float[batchSize][];

            for (var i = 0; i < batchSize; i++)
            {
                result[i] = new float[embeddingDim];
                for (var j = 0; j < embeddingDim; j++)
                {
                    result[i][j] = output[i * embeddingDim + j];
                }
            }

            output.Dispose();
            return result;
        }
    }
}
#endif