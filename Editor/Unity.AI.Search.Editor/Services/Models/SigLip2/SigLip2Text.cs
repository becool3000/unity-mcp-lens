#if SENTIS_AVAILABLE
using System;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using Unity.InferenceEngine;
using Microsoft.ML.Tokenizers;
using System.Threading.Tasks;
using Unity.AI.Assistant.Utils;
using Unity.AI.Search.Editor.Utilities;

namespace Unity.AI.Search.Editor.Services.Models
{
    class SigLip2Text : IDisposable
    {
        // Protect against too many concurrent tasks as it can lead to invalid output tensors
        const int k_MaxConcurrentTasks = 10;

        static readonly SemaphoreSlim k_ConcurrencyLimiter =
            new SemaphoreSlim(k_MaxConcurrentTasks, k_MaxConcurrentTasks);

        static string TextModelPath => SigLip2.ModelInfo.OnnxTextModelPath;
        static string TokenizerPath => SigLip2.ModelInfo.TokenizerPath;

        SentencePieceTokenizer m_Tokenizer;
        ITextHandler m_Handler;
        Model m_TextModel;
        Worker m_TextWorker;

        public bool CanLoad()
        {
            if (!File.Exists(TokenizerPath))
            {
                return false;
            }

            TryInit();
            return m_Handler?.CanLoad ?? false;
        }

        void TryInit()
        {
            if (m_Handler != null) return;

            if (SigLip2.ModelInfo.HasOptimizedTextModel)
                m_Handler = new TextHandlerOptimized();
            else if (SigLip2.ModelInfo.HasOnnxTextModel)
                m_Handler = new TextHandlerOnnx();
        }

        void EnsureModelLoaded()
        {
            TryInit();

            if (m_Tokenizer == null)
            {
                using var spStream = File.OpenRead(TokenizerPath);
                m_Tokenizer = SentencePieceTokenizer.Create(spStream, addBeginOfSentence: false, addEndOfSentence: false);
            }

            if (m_TextModel == null)
            {
                m_TextModel = m_Handler.Load();
                m_TextWorker = new Worker(m_TextModel, SigLipModelInfo.ModelBackendType);
            }
        }

        public async Task<float[]> GetTextEmbeddings(string text) => (await GetTextEmbeddings(new[] { text }))[0];

        public async Task<float[][]> GetTextEmbeddings(string[] texts)
        {
            if (texts == null || texts.Length == 0)
                return Array.Empty<float[]>();

            EnsureModelLoaded();

            // Preprocess text with same format as PyTorch version
            var preprocessed = texts.Select(t => $"{t.ToLower()}.").ToArray();

            // Tokenize batch
            var (inputIds, attentionMask, tokenStrings) = TokenizeBatch(preprocessed, maxLen: 64);

#if ASSISTANT_SIGLIP_DEBUG
            // Log tokens like in PyTorch version
            for (var i = 0; i < preprocessed.Length; i++)
            {
                var ids = inputIds.Skip(i * 64).Take(64).ToArray();
                var mask = attentionMask.Skip(i * 64).Take(64).ToArray();

                InternalLog.Log($"Text {i}: \"{preprocessed[i]}\"", LogFilter.SearchVerbose);
                InternalLog.Log($"Tokens: [{string.Join(", ", tokenStrings[i].Take(15))}...]", LogFilter.SearchVerbose);
                InternalLog.Log($"IDs: [{string.Join(", ", ids.Take(15))}...]", LogFilter.SearchVerbose);
                InternalLog.Log($"Mask: [{string.Join(", ", mask.Take(15))}...]", LogFilter.SearchVerbose);
            }
#endif

            await k_ConcurrencyLimiter.WaitAsync();

            try
            {
                // Run inference
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var embeddings = await RunTextInference(inputIds, attentionMask, texts.Length);
                stopwatch.Stop();

                InternalLog.Log(
                    $"Text inference (batch size: {texts.Length}) in {stopwatch.ElapsedMilliseconds}ms ({stopwatch.ElapsedMilliseconds / (float)texts.Length:F1}ms per embedding)",
                    LogFilter.SearchVerbose);

                return embeddings;
            }
            finally
            {
                k_ConcurrencyLimiter.Release();
            }
        }

        (long[] inputIds, long[] attentionMask, string[][] tokenStrings) TokenizeBatch(string[] texts, int maxLen = 64)
        {
            var batchSize = texts.Length;
            var inputIds = new long[batchSize * maxLen];
            var attentionMask = new long[batchSize * maxLen];
            var tokenStrings = new string[batchSize][];

            for (var i = 0; i < batchSize; i++)
            {
                var tokens = m_Tokenizer.EncodeToTokens(texts[i], out _, addBeginningOfSentence: false, addEndOfSentence: true);
                var ids = tokens.Select(t => (long)t.Id).ToArray();
                tokenStrings[i] = tokens.Select(t => t.Value).ToArray();

                // Truncate or pad to maxLen
                var actualLen = Mathf.Min(ids.Length, maxLen);

                // Fill input_ids and attention_mask
                for (var j = 0; j < maxLen; j++)
                {
                    if (j < actualLen)
                    {
                        inputIds[i * maxLen + j] = ids[j];
                        attentionMask[i * maxLen + j] = 1;
                    }
                    else
                    {
                        inputIds[i * maxLen + j] = 0; // pad token
                        attentionMask[i * maxLen + j] = 1; // SigLIP uses all ones for mask
                    }
                }
            }

            return (inputIds, attentionMask, tokenStrings);
        }

        async Task<float[][]> RunTextInference(long[] inputIds, long[] attentionMask, int batchSize)
        {
            const int maxLen = 64;

            // Create input tensors with data
            var inputIdsArray = new int[batchSize * maxLen];
            var attentionMaskArray = new int[batchSize * maxLen];

            // Fill arrays with converted data
            for (var i = 0; i < inputIds.Length; i++)
            {
                inputIdsArray[i] = (int)inputIds[i];
                attentionMaskArray[i] = (int)attentionMask[i];
            }

            // Create tensors with the data
            using var inputIdsTensor = new Tensor<int>(new TensorShape(batchSize, maxLen), inputIdsArray);
            using var attentionMaskTensor = new Tensor<int>(new TensorShape(batchSize, maxLen), attentionMaskArray);

            // Use persistent worker
            m_TextWorker.Schedule(inputIdsTensor, attentionMaskTensor);

            // Get output
            Tensor<float> outputTensor;
            while ((outputTensor = m_TextWorker.PeekOutput() as Tensor<float>) == null)
            {
                await Task.Yield();
            }

            // Convert output tensor to embeddings
            return await TensorUtils.OutputTensorToEmbeddings(outputTensor, batchSize);
        }

        public void Dispose()
        {
            m_TextWorker?.Dispose();
            m_TextWorker = null;
            m_TextModel = null;
            m_Tokenizer = null;
        }
    }
}
#endif