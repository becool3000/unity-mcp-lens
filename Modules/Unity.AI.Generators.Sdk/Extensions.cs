using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AiEditorToolsSdk.Components.Asset;
using AiEditorToolsSdk.Components.Asset.Responses;
using AiEditorToolsSdk.Components.Common.Responses.Wrappers;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Toolkit;
using UnityEngine;

namespace Unity.AI.Generators.Sdk
{
    static class Extensions
    {
        public static Task<OperationResult<BlobAssetResult>> StoreAssetWithResult(this IAssetComponent assetComponent, Stream stream, HttpClient client) =>
            StoreAssetWithResult(assetComponent, stream, client, CancellationToken.None, CancellationToken.None);

        /// <summary>
        /// Note that the input stream is disposed after the method call.
        /// </summary>
        public static async Task<OperationResult<BlobAssetResult>> StoreAssetWithResult(this IAssetComponent assetComponent, Stream stream, HttpClient client, CancellationToken sdkToken, CancellationToken putToken)
        {
#if AI_TK_DEBUG_DUMP_STREAM
            stream = await DumpStreamToDisk(stream, "stream_dump");
#endif
            var assetResult = await assetComponent.CreateAssetUploadUrl(cancellationToken: sdkToken);
            if (sdkToken.IsCancellationRequested)
                throw new OperationCanceledException("Operation was cancelled while creating asset upload URL.", sdkToken);
            if (assetResult.Result.IsSuccessful)
            {
                using var content = new StreamContent(stream);
                content.Headers.Add("x-ms-blob-type", "BlockBlob");
                // mime-type is not strictly required but some generative ai providers may expect it, we do a best effort and skip if not detectable
                if (FileTypeSupport.TryGetMimeTypeFromStream(stream, out var contentType))
                    content.Headers.Add("Content-Type", contentType);
                using var response = await client.PutAsync(assetResult.Result.Value.AssetUrl.Url, content, putToken).ConfigureAwaitMainThread();
                response.EnsureSuccessStatusCode();
            }
            return assetResult;
        }

        public static Task<OperationResult<BlobAssetResult>> StoreAssetWithResultPreservingStream(this IAssetComponent assetComponent, Stream stream, HttpClient client) =>
            StoreAssetWithResultPreservingStream(assetComponent, stream, client, CancellationToken.None, CancellationToken.None);

        /// <summary>
        /// Note that the input stream is not disposed after the method call.
        /// </summary>
        public static async Task<OperationResult<BlobAssetResult>> StoreAssetWithResultPreservingStream(this IAssetComponent assetComponent, Stream stream, HttpClient client, CancellationToken sdkToken, CancellationToken putToken)
        {
            long originalPosition = 0;
            var canSeek = stream.CanSeek;
            if (canSeek)
                originalPosition = stream.Position;

            try
            {
#if AI_TK_DEBUG_DUMP_STREAM
                stream = await DumpStreamToDisk(stream, "stream_dump");
#endif
                // Use a wrapper that prevents the StreamContent from disposing the underlying stream
                await using var wrapper = new NonDisposingStreamWrapper(stream);
                return await StoreAssetWithResult(assetComponent, wrapper, client, sdkToken, putToken);
            }
            finally
            {
                if (canSeek)
                {
                    try { stream.Position = originalPosition; }
                    catch { /* ignored */ }
                }
            }
        }

#if AI_TK_DEBUG_DUMP_STREAM
        /// <summary>
        /// Helper method to dump a stream to disk for debugging purposes.
        /// </summary>
        /// <param name="stream">The stream to dump</param>
        /// <param name="prefix">Filename prefix</param>
        /// <returns>The stream to use for further processing</returns>
        static async Task<Stream> DumpStreamToDisk(Stream stream, string prefix)
        {
            var debugDir = Path.Combine(Application.persistentDataPath, "StreamDebugDumps");
            Directory.CreateDirectory(debugDir);
            var filename = Path.Combine(debugDir, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.bin");

            if (stream.CanSeek)
            {
                var originalPosition = stream.Position;

                await using var fileStream = File.Create(filename);
                await stream.CopyToAsync(fileStream);

                // Reset position to where it was
                stream.Position = originalPosition;
                Debug.Log($"[AI Debug] Stream dumped to: {filename}");

                return stream;
            }

            // For non-seekable streams, we need a memory stream as a buffer
            var memStream = new MemoryStream();
            await stream.CopyToAsync(memStream);
            memStream.Position = 0;

            {
                // Save to file
                await using var fileStream = File.Create(filename);
                await memStream.CopyToAsync(fileStream);
            }

            // Reset for actual upload
            memStream.Position = 0;
            Debug.Log($"[AI Debug] Non-seekable stream dumped to: {filename}");

            return memStream;
        }
#endif
    }
}
