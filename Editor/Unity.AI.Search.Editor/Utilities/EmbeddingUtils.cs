using System.Numerics;
using Unity.AI.Assistant.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.AI.Search.Editor.Utils
{
    static class EmbeddingsUtils
    {
        const float k_NormalizationThreshold = 0.01f; // Tolerance for "close to 1.0"

        /// <summary>
        /// Normalize a vector to unit length (magnitude = 1.0).
        /// Should be called once at storage time.
        /// </summary>
        public static float[] Normalize(this float[] vector)
        {
            if (vector == null || vector.Length == 0)
                return vector;

            var sqSum = 0f;
            for (var i = 0; i < vector.Length; i++)
                sqSum += vector[i] * vector[i];

            // Check if already normalized:
            if (math.abs(sqSum - 1f) < k_NormalizationThreshold)
            {
                return vector;
            }
            
            var magnitude = math.sqrt(sqSum);

            // Avoid division by zero
            if (magnitude < 1e-10f)
            {
                InternalLog.LogWarning("[EmbeddingUtils] Attempted to normalize zero vector", LogFilter.Search);
                return vector;
            }

            var invMag = 1f / magnitude;
            var normalized = new float[vector.Length];
            for (var i = 0; i < vector.Length; i++)
                normalized[i] = vector[i] * invMag;

            return normalized;
        }

        /// <summary>
        /// Check if a vector is already normalized.
        /// </summary>
        static bool IsNormalized(this float[] vector)
        {
            if (vector == null || vector.Length == 0)
                return false;
        
            var sqSum = 0f;
            for (var i = 0; i < vector.Length; i++)
                sqSum += vector[i] * vector[i];
        
            // Check if magnitude is close to 1.0
            return math.abs(sqSum - 1f) < k_NormalizationThreshold;
        }

        /// <summary>
        /// Compute cosine similarity between two vectors.
        /// Assumes vectors are normalized (magnitude = 1.0).
        /// For unit vectors: cos(θ) = A·B / (||A|| × ||B||) = A·B / (1 × 1) = A·B
        /// </summary>
        public static float CosineSimilarity(float[] vectorA, float[] vectorB)
        {
            if (vectorA == null || vectorB == null || vectorA.Length != vectorB.Length)
                return 0f;

#if ASSISTANT_INTERNAL
            // Validation in debug builds: check if vectors are normalized
            // Only check vectorA since we're comparing query (A) vs many assets (B)
            // and we want to catch if the query embedding isn't normalized
            if (!vectorA.IsNormalized())
            {
                InternalLog.LogWarning("[EmbeddingUtils] Asset vector is not normalized. " +
                                       "Results may be incorrect. Consider normalizing embeddings.", LogFilter.Search);
            }
#endif

            var length = vectorA.Length;
            var dotProduct = 0f;
            var i = 0;

            // SIMD optimization using System.Numerics.Vector<float>
            // Automatically uses the best SIMD width available (SSE: 4, AVX: 8, AVX-512: 16 floats)
            var simdLength = Vector<float>.Count;
            var simdEnd = length - simdLength;

            for (; i <= simdEnd; i += simdLength)
            {
                var vecA = new Vector<float>(vectorA, i);
                var vecB = new Vector<float>(vectorB, i);
                dotProduct += Vector.Dot(vecA, vecB);
            }

            // Handle remainder (scalar tail)
            for (; i < length; i++)
                dotProduct += vectorA[i] * vectorB[i];

            // Clamp for numerical stability (floating-point errors can push slightly outside [-1, 1])
            return math.clamp(dotProduct, -1f, 1f);
        }

        /// <summary>
        /// Fast dot product for already-normalized vectors. No checks, no clamp.
        /// Use in hot paths where both inputs are guaranteed unit-length and same dimension.
        /// </summary>
        public static float CosineSimilarityFast(float[] vectorA, float[] vectorB)
        {
            var length = vectorA.Length;
            var dotProduct = 0f;
            var i = 0;

            var simdLength = Vector<float>.Count;
            var simdEnd = length - simdLength;

            for (; i <= simdEnd; i += simdLength)
            {
                var vecA = new Vector<float>(vectorA, i);
                var vecB = new Vector<float>(vectorB, i);
                dotProduct += Vector.Dot(vecA, vecB);
            }

            for (; i < length; i++)
                dotProduct += vectorA[i] * vectorB[i];

            return dotProduct;
        }

        /// <summary>
        /// Full cosine similarity calculation with magnitude computation using SIMD.
        /// Used as fallback for non-normalized vectors.
        /// </summary>
        static float CosineSimilarityWithNormalize(float[] vectorA, float[] vectorB)
        {
            var dotProduct = 0f;
            var magnitudeA = 0f;
            var magnitudeB = 0f;
            var i = 0;
            var length = vectorA.Length;

            // SIMD optimization using System.Numerics.Vector<float>
            var simdLength = Vector<float>.Count;
            var simdEnd = length - simdLength;

            for (; i <= simdEnd; i += simdLength)
            {
                var vecA = new Vector<float>(vectorA, i);
                var vecB = new Vector<float>(vectorB, i);
                dotProduct += Vector.Dot(vecA, vecB);
                magnitudeA += Vector.Dot(vecA, vecA);
                magnitudeB += Vector.Dot(vecB, vecB);
            }

            // Handle remainder (scalar tail)
            for (; i < length; i++)
            {
                var a = vectorA[i];
                var b = vectorB[i];
                dotProduct += a * b;
                magnitudeA += a * a;
                magnitudeB += b * b;
            }

            if (magnitudeA == 0 || magnitudeB == 0)
                return 0f; // Avoid division by zero

            return dotProduct / (math.sqrt(magnitudeA) * math.sqrt(magnitudeB));
        }

        /// <summary>
        /// Original scalar implementation of cosine similarity.
        /// Kept for reference and testing purposes.
        /// </summary>
        public static float CosineSimilarityScalar(float[] vectorA, float[] vectorB)
        {
            if (vectorA == null || vectorB == null || vectorA.Length != vectorB.Length)
                return 0f;

            var dotProduct = 0f;
            var magnitudeA = 0f;
            var magnitudeB = 0f;

            for (var i = 0; i < vectorA.Length; i++)
            {
                var a = vectorA[i];
                var b = vectorB[i];
                dotProduct += a * b;
                magnitudeA += a * a;
                magnitudeB += b * b;
            }

            if (magnitudeA == 0 || magnitudeB == 0)
                return 0f;

            return dotProduct / (Mathf.Sqrt(magnitudeA) * Mathf.Sqrt(magnitudeB));
        }
    }
}
