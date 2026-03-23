using System;
using System.Collections.Generic;
using System.IO;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Generators.IO.Utilities
{
    /// <summary>
    /// Central registry for image file type support and detection
    /// </summary>
    static class ImageFileTypeSupport
    {
        /// <summary>
        /// Delegate for methods that attempt to get image dimensions from bytes
        /// </summary>
        /// <param name="bytes">The image bytes to analyze</param>
        /// <param name="width">Output width of the image if successful</param>
        /// <param name="height">Output height of the image if successful</param>
        /// <returns>True if dimensions were obtained successfully, false otherwise</returns>
        public delegate bool GetDimensionsDelegate(IReadOnlyList<byte> bytes, out int width, out int height);

        /// <summary>
        /// Delegate for methods that encode a Texture2D to image bytes
        /// </summary>
        /// <param name="texture">The texture to encode</param>
        /// <returns>Byte array containing the encoded image data</returns>
        public delegate byte[] EncodeTextureDelegate(Texture2D texture);

        /// <summary>
        /// Delegate for methods that determine if a stream requires special import handling
        /// </summary>
        /// <param name="stream">The image stream to analyze</param>
        /// <returns>True if the image requires special import handling, false otherwise</returns>
        public delegate bool RequiresTemporaryAssetDelegate(Stream stream);

        /// <summary>
        /// Contains all functions related to a specific image format
        /// </summary>
        public class FormatSupport
        {
            // The primary extension for this format (e.g. ".png")
            public string primaryExtension { get; }

            // All extensions that map to this format (e.g. [".jpg", ".jpeg"])
            public string[] extensions { get; }

            // The MIME type for this format (e.g. "image/png")
            public string mimeType { get; }

            // Function to detect if bytes match this format
            public Func<IReadOnlyList<byte>, bool> isFormatFunc { get; }

            // Function to detect if stream contains this format (stream position handling is done by ImageFileTypeSupport)
            public Func<Stream, bool> isFormatStreamFunc { get; }

            // Function to get dimensions for this format (returns true if successful)
            public GetDimensionsDelegate getDimensionsFunc { get; }

            // Function to encode a Texture2D to this format
            public EncodeTextureDelegate encodeFunc { get; }

            // Function to determine if the image requires special import handling
            public RequiresTemporaryAssetDelegate requiresTemporaryAssetFunc { get; }

            /// <summary>
            /// Indicates whether this format requires linear color space conversion when encoding
            /// </summary>
            public bool requiresLinearColorSpace { get; init; }

            public FormatSupport(string primaryExtension, string[] extensions, string mimeType,
                Func<IReadOnlyList<byte>, bool> isFormatFunc,
                Func<Stream, bool> isFormatStreamFunc,
                GetDimensionsDelegate getDimensionsFunc = null,
                EncodeTextureDelegate encodeFunc = null,
                RequiresTemporaryAssetDelegate requiresTemporaryAssetFunc = null)
            {
                this.primaryExtension = primaryExtension;
                this.extensions = extensions;
                this.mimeType = mimeType;
                this.isFormatFunc = isFormatFunc;
                this.isFormatStreamFunc = isFormatStreamFunc;
                this.getDimensionsFunc = getDimensionsFunc;
                this.encodeFunc = encodeFunc;
                this.requiresTemporaryAssetFunc = requiresTemporaryAssetFunc;
            }
        }

        static readonly Dictionary<string, FormatSupport> k_FormatRegistry = new(StringComparer.OrdinalIgnoreCase);
        static readonly List<FormatSupport> k_Formats = new();
        static bool s_Initialized;

        /// <summary>
        /// Gets all registered format extensions
        /// </summary>
        public static IReadOnlyList<string> supportedExtensions { get; private set; } = Array.Empty<string>();

        /// <summary>
        /// Initialize the registry with built-in formats
        /// </summary>
        [InitializeOnLoadMethod]
        public static void EnsureInitialized()
        {
            if (s_Initialized)
                return;

            s_Initialized = true;

            // Register PNG format
            RegisterFormat(format: new FormatSupport(
                primaryExtension: ".png",
                extensions: new[] { ".png" },
                mimeType: "image/png",
                isFormatFunc: FileIO.IsPng,
                isFormatStreamFunc: DetectPng,
                getDimensionsFunc: ImageFileUtilities.TryGetPngDimensions,
                encodeFunc: texture => texture.EncodeToPNG(),
                requiresTemporaryAssetFunc: _ => false));

            // Register JPG format
            RegisterFormat(format: new FormatSupport(
                primaryExtension: ".jpg",
                extensions: new[] { ".jpg", ".jpeg" },
                mimeType: "image/jpeg",
                isFormatFunc: FileIO.IsJpg,
                isFormatStreamFunc: DetectJpg,
                getDimensionsFunc: ImageFileUtilities.TryGetJpegDimensions,
                encodeFunc: texture => texture.EncodeToJPG(quality: 100),
                requiresTemporaryAssetFunc: ImageFileUtilities.HasJpgOrientation));

            // Register EXR format
            RegisterFormat(format: new FormatSupport(
                primaryExtension: ".exr",
                extensions: new[] { ".exr" },
                mimeType: "image/x-exr",
                isFormatFunc: FileIO.IsExr,
                isFormatStreamFunc: DetectExr,
                getDimensionsFunc: null,
                encodeFunc: texture => texture.EncodeToEXR(flags: Texture2D.EXRFlags.CompressRLE),
                requiresTemporaryAssetFunc: _ => false)
            {
                requiresLinearColorSpace = true
            });

            // Register GIF format
            RegisterFormat(format: new FormatSupport(
                    primaryExtension: ".gif",
                    extensions: new[] { ".gif" },
                    mimeType: "image/gif",
                    isFormatFunc: FileIO.IsGif,
                    isFormatStreamFunc: stream =>
                    {
                        var headerBytes = new byte[6];
                        var bytesRead = stream.Read(buffer: headerBytes, offset: 0, count: headerBytes.Length);
                        return bytesRead >= 6 && FileIO.IsGif(imageBytes: headerBytes);
                    },
                    getDimensionsFunc: ImageFileUtilities.TryGetGifDimensions,
                    encodeFunc: texture => texture.EncodeToGIF()));

            // Register BMP format
            RegisterFormat(format: new FormatSupport(
                    primaryExtension: ".bmp",
                    extensions: new[] { ".bmp" },
                    mimeType: "image/bmp",
                    isFormatFunc: bytes => bytes != null && bytes.Count >= 2 && bytes[index: 0] == 'B' && bytes[index: 1] == 'M',
                    isFormatStreamFunc: stream =>
                    {
                        var headerBytes = new byte[2];
                        return stream.Read(buffer: headerBytes, offset: 0, count: 2) == 2 && headerBytes[0] == 'B' && headerBytes[1] == 'M';
                    },
                    getDimensionsFunc: null,
                    encodeFunc: texture => texture.EncodeToBMP()));

            // Register TIFF format
            RegisterFormat(format: new FormatSupport(
                    primaryExtension: ".tiff",
                    extensions: new[] { ".tif", ".tiff" },
                    mimeType: "image/tiff",
                    isFormatFunc: bytes => bytes != null && bytes.Count >= 4 &&
                        ((bytes[index: 0] == 'I' && bytes[index: 1] == 'I' && bytes[index: 2] == 42 && bytes[index: 3] == 0) || // Little endian
                            (bytes[index: 0] == 'M' && bytes[index: 1] == 'M' && bytes[index: 2] == 0 && bytes[index: 3] == 42)), // Big endian
                    isFormatStreamFunc: stream =>
                    {
                        var headerBytes = new byte[4];
                        return stream.Read(buffer: headerBytes, offset: 0, count: 4) == 4 &&
                            ((headerBytes[0] == 'I' && headerBytes[1] == 'I' && headerBytes[2] == 42 && headerBytes[3] == 0) ||
                                (headerBytes[0] == 'M' && headerBytes[1] == 'M' && headerBytes[2] == 0 && headerBytes[3] == 42));
                    },
                    getDimensionsFunc: null,
                    encodeFunc: texture => texture.EncodeToTIFF()));

            // Register PSD format
            RegisterFormat(format: new FormatSupport(
                    primaryExtension: ".psd",
                    extensions: new[] { ".psd" },
                    mimeType: "image/vnd.adobe.photoshop",
                    isFormatFunc: bytes => bytes != null && bytes.Count >= 4 && bytes[index: 0] == '8' && bytes[index: 1] == 'B' &&
                        bytes[index: 2] == 'P' && bytes[index: 3] == 'S',
                    isFormatStreamFunc: stream =>
                    {
                        var headerBytes = new byte[4];
                        return stream.Read(buffer: headerBytes, offset: 0, count: 4) == 4 && headerBytes[0] == '8' &&
                            headerBytes[1] == 'B' && headerBytes[2] == 'P' && headerBytes[3] == 'S';
                    },
                    getDimensionsFunc: null,
                    encodeFunc: texture => texture.EncodeToPSD()));

            // Register HDR format
            RegisterFormat(format: new FormatSupport(
                    primaryExtension: ".hdr",
                    extensions: new[] { ".hdr" },
                    mimeType: "image/vnd.radiance",
                    isFormatFunc: bytes => bytes != null && bytes.Count >= 10 && bytes[index: 0] == '#' && bytes[index: 1] == '?' &&
                        bytes[index: 2] == 'R' && bytes[index: 3] == 'A' && bytes[index: 4] == 'D' && bytes[index: 5] == 'I' &&
                        bytes[index: 6] == 'A' && bytes[index: 7] == 'N' && bytes[index: 8] == 'C' && bytes[index: 9] == 'E',
                    isFormatStreamFunc: stream =>
                    {
                        var headerBytes = new byte[10];
                        return stream.Read(buffer: headerBytes, offset: 0, count: 10) == 10 && headerBytes[0] == '#' && headerBytes[1] == '?' &&
                            headerBytes[2] == 'R' && headerBytes[3] == 'A' && headerBytes[4] == 'D' && headerBytes[5] == 'I' &&
                            headerBytes[6] == 'A' && headerBytes[7] == 'N' && headerBytes[8] == 'C' && headerBytes[9] == 'E';
                    },
                    getDimensionsFunc: null,
                    encodeFunc: texture => texture.EncodeToHDR())
            {
                requiresLinearColorSpace = true
            });
        }

        // Format detection functions that focus on the detection logic only
        // Stream position management is handled by the central methods
        static bool DetectPng(Stream stream)
        {
            var headerBytes = new byte[8];
            var bytesRead = stream.Read(headerBytes, 0, headerBytes.Length);
            return bytesRead >= 8 && FileIO.IsPng(headerBytes);
        }

        static bool DetectJpg(Stream stream)
        {
            var headerBytes = new byte[2];
            var bytesRead = stream.Read(headerBytes, 0, headerBytes.Length);
            return bytesRead >= 2 && FileIO.IsJpg(headerBytes);
        }

        static bool DetectExr(Stream stream)
        {
            var headerBytes = new byte[4];
            var bytesRead = stream.Read(headerBytes, 0, headerBytes.Length);
            return bytesRead >= 4 && FileIO.IsExr(headerBytes);
        }

        /// <summary>
        /// Registers a new image format
        /// </summary>
        static void RegisterFormat(FormatSupport format)
        {
            if (format == null)
                throw new ArgumentNullException(nameof(format));

            k_Formats.Add(format);

            // Register all extensions for this format
            foreach (var ext in format.extensions)
            {
                k_FormatRegistry[ext] = format;
            }

            // Update known extensions list
            var allExtensions = new List<string>();
            foreach (var f in k_Formats)
            {
                allExtensions.AddRange(f.extensions);
            }

            supportedExtensions = allExtensions.ToArray();
        }

        /// <summary>
        /// Tries to get format support for an extension
        /// </summary>
        public static bool TryGetFormatForExtension(string extension, out FormatSupport format)
        {
            EnsureInitialized();

            if (string.IsNullOrEmpty(extension))
            {
                format = null;
                return false;
            }

            // Ensure extension starts with a period
            if (!extension.StartsWith("."))
                extension = "." + extension;

            return k_FormatRegistry.TryGetValue(extension, out format);
        }

        /// <summary>
        /// Try to detect the image format from a byte array
        /// </summary>
        public static bool TryGetImageFormat(IReadOnlyList<byte> bytes, out FormatSupport format)
        {
            EnsureInitialized();

            format = null;

            if (bytes == null || bytes.Count < 4)
                return false;

            foreach (var f in k_Formats)
            {
                if (f.isFormatFunc != null && f.isFormatFunc(bytes))
                {
                    format = f;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Try to detect the image format from a stream
        /// </summary>
        public static bool TryGetImageFormat(Stream stream, out FormatSupport format)
        {
            EnsureInitialized();

            format = null;

            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (!stream.CanSeek)
                throw new NotSupportedException("The specified stream must be seekable.");

            var originalPosition = stream.Position;
            try
            {
                stream.Position = 0;

                foreach (var f in k_Formats)
                {
                    if (f.isFormatStreamFunc != null)
                    {
                        stream.Position = 0;
                        try
                        {
                            if (f.isFormatStreamFunc(stream))
                            {
                                format = f;
                                return true;
                            }
                        }
                        catch
                        {
                            // Continue to the next format if there's an error
                        }
                    }
                }

                return false;
            }
            finally
            {
                // Always restore the original position, even if an exception occurs
                try { stream.Position = originalPosition; }
                catch
                {
                    /* Ignore positioning errors */
                }
            }
        }

        /// <summary>
        /// Try to get image extension from bytes
        /// </summary>
        public static bool TryGetImageExtension(IReadOnlyList<byte> imageBytes, out string extension)
        {
            extension = null;

            if (TryGetImageFormat(imageBytes, out var format))
            {
                extension = format.primaryExtension;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Try to get image extension from a stream
        /// </summary>
        public static bool TryGetImageExtension(Stream stream, out string extension)
        {
            extension = null;

            if (TryGetImageFormat(stream, out var format))
            {
                extension = format.primaryExtension;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Check if the image data matches a specific format
        /// </summary>
        public static bool IsFormat(IReadOnlyList<byte> imageBytes, string extension)
        {
            return TryGetFormatForExtension(extension, out var format) &&
                TryGetImageFormat(imageBytes, out var detectedFormat) && format == detectedFormat;
        }

        /// <summary>
        /// Check if the stream data matches a specific format
        /// </summary>
        public static bool IsFormat(Stream stream, string extension)
        {
            return TryGetFormatForExtension(extension, out var format) &&
                TryGetImageFormat(stream, out var detectedFormat) && format == detectedFormat;
        }

        /// <summary>
        /// Try to get dimensions of an image from bytes
        /// </summary>
        public static bool TryGetImageDimensions(IReadOnlyList<byte> imageBytes, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (TryGetImageFormat(imageBytes, out var format) && format.getDimensionsFunc != null)
            {
                return format.getDimensionsFunc(imageBytes, out width, out height);
            }

            return false;
        }

        /// <summary>
        /// Try to encode a texture to a specific format
        /// </summary>
        /// <param name="texture">The texture to encode</param>
        /// <param name="extension">The target format extension (e.g. ".png")</param>
        /// <param name="encodedData">The resulting encoded image data</param>
        /// <returns>True if encoding was successful, false otherwise</returns>
        public static bool TryEncodeTexture(Texture2D texture, string extension, out byte[] encodedData)
        {
            encodedData = null;

            if (texture == null || string.IsNullOrEmpty(extension))
                return false;

            // Ensure extension starts with a period
            if (!extension.StartsWith("."))
                extension = "." + extension;

            if (TryGetFormatForExtension(extension, out var format) && format.encodeFunc != null)
            {
                try
                {
                    encodedData = format.encodeFunc(texture);
                    return encodedData != null && encodedData.Length > 0;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to encode texture to {extension}: {e.Message}");
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if the format requires special import handling for the given stream
        /// </summary>
        public static bool RequiresTemporaryAsset(Stream stream, string extension)
        {
            if (string.IsNullOrEmpty(extension) || !stream.CanSeek)
                return true;

            if (!TryGetFormatForExtension(extension, out var format) || format.requiresTemporaryAssetFunc == null)
                return true;

            var originalPosition = stream.Position;
            try
            {
                stream.Position = 0;
                return format.requiresTemporaryAssetFunc(stream);
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }
    }
}
