using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Generators.Asset;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.Video;

namespace Unity.AI.Generators.IO.Utilities
{
    static class ImageFileUtilities
    {
        public const string failedDownloadIcon = "Packages/com.unity.ai.assistant/Modules/Unity.AI.Generators.IO/Icons/Warning.png";

        static Material s_AlphaToGrayMaterial;

        public static Material AlphaToGrayMaterial
        {
            get
            {
                if (s_AlphaToGrayMaterial == null)
                {
                    var shader = Shader.Find("Hidden/AIToolkit/AlphaToGray");
                    if (shader != null)
                    {
                        s_AlphaToGrayMaterial = new Material(shader);
                        s_AlphaToGrayMaterial.SetFloat("_GrayValue", 0.5f); // Mid-gray
                    }
                }
                return s_AlphaToGrayMaterial;
            }
        }

        public static IReadOnlyList<string> knownExtensions
        {
            get
            {
                ImageFileTypeSupport.EnsureInitialized();
                return ImageFileTypeSupport.supportedExtensions;
            }
        }

        /// <summary>
        /// Gets the last modified UTC time for a URI as ticks.
        /// </summary>
        /// <param name="uri">The URI to check.</param>
        /// <returns>The last modified time in UTC ticks, or 0 if the URI is not a valid local file.</returns>
        public static long GetLastModifiedUtcTime(Uri uri)
        {
            if (uri == null)
            {
                return 0;
            }

            if (!uri.IsFile)
            {
                return 0;
            }

            var path = uri.GetLocalPath();
            if (string.IsNullOrEmpty(path))
            {
                return 0;
            }

            if (!File.Exists(path))
            {
                return 0;
            }

            return new FileInfo(path).LastWriteTimeUtc.Ticks;
        }

        public static bool TryGetImageExtension(IReadOnlyList<byte> imageBytes, out string extension)
        {
            ImageFileTypeSupport.EnsureInitialized();
            return ImageFileTypeSupport.TryGetImageExtension(imageBytes, out extension);
        }

        public static bool TryGetImageExtension(Stream imageStream, out string extension)
        {
            ImageFileTypeSupport.EnsureInitialized();
            return ImageFileTypeSupport.TryGetImageExtension(imageStream, out extension);
        }

        public static bool TryGetImageDimensions(IReadOnlyList<byte> imageBytes, out int width, out int height)
        {
            ImageFileTypeSupport.EnsureInitialized();
            return ImageFileTypeSupport.TryGetImageDimensions(imageBytes, out width, out height);
        }

        public static bool TryGetImageDimensions(Stream imageStream, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (imageStream is not { CanSeek: true })
            {
                return false;
            }

            var originalPosition = imageStream.Position;
            try
            {
                imageStream.Position = 0;

                const int headerSize = 1024;
                var headerBuffer = new byte[headerSize];
                var bytesRead = imageStream.Read(headerBuffer, 0, headerBuffer.Length);

                if (bytesRead < 24)
                {
                    return false;
                }

                var headerBytes = headerBuffer.Take(bytesRead).ToArray();
                return TryGetImageDimensions(headerBytes, out width, out height);
            }
            finally
            {
                // Restore original position
                imageStream.Position = originalPosition;
            }
        }

        // Keep this public since it's referenced by the registry
        public static bool TryGetPngDimensions(IReadOnlyList<byte> imageBytes, out int width, out int height)
        {
            width = 0;
            height = 0;

            // Check if it's a valid PNG file
            if (imageBytes == null || imageBytes.Count < 24)
            {
                return false;
            }

            // Check PNG signature
            if (!FileIO.IsPng(imageBytes))
            {
                return false;
            }

            // Width: bytes 16-19, Height: bytes 20-23
            width = ReadInt32(imageBytes, 16, false);
            height = ReadInt32(imageBytes, 20, false);
            return true;
        }

        public static bool IsPngIndexedColor(IReadOnlyList<byte> imageBytes)
        {
            if (imageBytes.Count < 29)
            {
                return false;
            }

            // The color type is stored in the IHDR chunk's data at byte index 9.
            // Since the IHDR data starts at index 16 (8 for signature + 4 for length + 4 for type),
            // the color type is at index 16 + 9 = 25.
            var colorType = imageBytes[25];

            // For PNG files, when the color type equals 3, the image uses an indexed color palette.
            return colorType == 3;
        }

        public static bool IsPngIndexedColor(Stream imageStream)
        {
            if (imageStream == null)
            {
                throw new ArgumentNullException(nameof(imageStream));
            }

            if (!imageStream.CanSeek)
            {
                throw new NotSupportedException("The provided stream must be seekable.");
            }

            var originalPosition = imageStream.Position;
            try
            {
                imageStream.Position = 0;

                const int requiredBytes = 29;
                var headerBuffer = new byte[requiredBytes];
                var bytesRead = imageStream.Read(headerBuffer, 0, requiredBytes);
                return bytesRead >= requiredBytes && IsPngIndexedColor(headerBuffer);
            }
            finally
            {
                imageStream.Position = originalPosition;
            }
        }

        // Make public to be accessible from registry
        public static bool TryGetJpegDimensions(IReadOnlyList<byte> imageBytes, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (imageBytes == null || imageBytes.Count < 2)
            {
                return false;
            }

            // Check JPEG signature
            if (!FileIO.IsJpg(imageBytes))
            {
                return false;
            }

            var offset = 2;

            while (offset < imageBytes.Count - 1)
            {
                // Find the next marker prefixed by 0xFF
                if (imageBytes[offset] != 0xFF)
                {
                    offset++;
                    continue;
                }

                // Skip any padding FF bytes
                while (offset < imageBytes.Count && imageBytes[offset] == 0xFF) offset++;

                if (offset >= imageBytes.Count)
                {
                    break;
                }

                var marker = imageBytes[offset++];

                // Read segment length
                if (offset + 1 >= imageBytes.Count)
                {
                    break;
                }

                var length = ReadInt16(imageBytes, offset, false); // Use the shared method, always big-endian
                offset += 2;

                if (length < 2)
                {
                    break;
                }

                // Check for SOF markers (Start Of Frame)
                if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
                {
                    // Ensure there are enough bytes to read
                    if (offset + length - 2 > imageBytes.Count)
                    {
                        break;
                    }

                    // The length includes the 2 bytes of the length field, so we subtract 2
                    var segmentEnd = offset + length - 2;

                    // Sample Precision (1 byte)
                    if (offset >= segmentEnd)
                    {
                        break;
                    }

                    offset++;

                    // Image Height (2 bytes)
                    if (offset + 1 >= segmentEnd)
                    {
                        break;
                    }

                    height = ReadInt16(imageBytes, offset, false); // Use the shared method, always big-endian
                    offset += 2;

                    // Image Width (2 bytes)
                    if (offset + 1 >= segmentEnd)
                    {
                        break;
                    }

                    width = ReadInt16(imageBytes, offset, false); // Use the shared method, always big-endian

                    //offset += 2;

                    return true;
                }

                // Skip over other markers
                offset += length - 2;
            }

            return false;
        }

        // Make public to be accessible from registry
        public static bool TryGetGifDimensions(IReadOnlyList<byte> imageBytes, out int width, out int height)
        {
            width = 0;
            height = 0;

            // Check if it's a valid PNG file
            if (imageBytes == null || imageBytes.Count < 10)
            {
                return false;
            }

            // Check PNG signature
            if (!FileIO.IsGif(imageBytes))
            {
                return false;
            }

            // 3. Validate the GIF version ("87a" or "89a")
            // ASCII: 8=0x38, 7=0x37, 9=0x39, a=0x61
            var isVersionValid = (imageBytes[3] == 0x38 && imageBytes[4] == 0x37 && imageBytes[5] == 0x61) ||
                (imageBytes[3] == 0x38 && imageBytes[4] == 0x39 && imageBytes[5] == 0x61);

            if (!isVersionValid)
            {
                return false;
            }

            // 4. Read the dimensions. They are stored as 16-bit little-endian integers.
            // Width is at offset 6 (bytes 6 and 7).
            width = imageBytes[6] | (imageBytes[7] << 8);

            // Height is at offset 8 (bytes 8 and 9).
            height = imageBytes[8] | (imageBytes[9] << 8);

            return true;
        }

        public static bool IsPng(Stream imageStream)
        {
            ImageFileTypeSupport.EnsureInitialized();
            return ImageFileTypeSupport.IsFormat(imageStream, ".png");
        }

        public static bool IsJpg(Stream imageStream)
        {
            ImageFileTypeSupport.EnsureInitialized();
            return ImageFileTypeSupport.IsFormat(imageStream, ".jpg");
        }

        public static bool IsGif(Stream imageStream)
        {
            ImageFileTypeSupport.EnsureInitialized();
            return ImageFileTypeSupport.IsFormat(imageStream, ".gif");
        }

        public static bool IsExr(Stream imageStream)
        {
            ImageFileTypeSupport.EnsureInitialized();
            return ImageFileTypeSupport.IsFormat(imageStream, ".exr");
        }

        public static bool HasPngAlphaChannel(byte[] headerBytes)
        {
            if (headerBytes == null || headerBytes.Length < 26)
            {
                return true;
            }

            var colorType = headerBytes[25];
            return colorType is 4 or 6;
        }

        public static bool HasPngAlphaChannel(Stream imageStream)
        {
            if (imageStream == null)
                throw new ArgumentNullException(nameof(imageStream));

            if (!imageStream.CanSeek)
                throw new NotSupportedException("The provided stream must be seekable.");

            var originalPosition = imageStream.Position;
            try
            {
                imageStream.Position = 0;

                // We need at least 26 bytes to read the color type from the IHDR chunk.
                const int requiredBytes = 26;
                var headerBuffer = new byte[requiredBytes];
                var bytesRead = imageStream.Read(headerBuffer, 0, requiredBytes);

                // If we can't read enough bytes, we can't be certain.
                // The byte[] version defaults to true in this case.
                if (bytesRead < requiredBytes)
                    return true;

                // Check if it's a PNG before checking for alpha
                if (!FileIO.IsPng(headerBuffer))
                    return false;

                return HasPngAlphaChannel(headerBuffer);
            }
            finally
            {
                try { imageStream.Position = originalPosition; }
                catch { /* ignored */ }
            }
        }

        public static byte[] StripPngAlphaToGray(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                return imageBytes;

            var previousActive = RenderTexture.active;
            Texture2D sourceTexture = null;
            Texture2D resultTexture = null;
            RenderTexture rt = null;

            try
            {
                sourceTexture = new Texture2D(2, 2);
                sourceTexture.LoadImage(imageBytes);

                if (!HasAlphaChannel(sourceTexture))
                    return imageBytes;

                rt = RenderTexture.GetTemporary(sourceTexture.width, sourceTexture.height);

                Graphics.Blit(sourceTexture, rt, AlphaToGrayMaterial);

                resultTexture = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGB24, false);
                RenderTexture.active = rt;
                resultTexture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                resultTexture.Apply();

                return resultTexture.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (rt)
                    RenderTexture.ReleaseTemporary(rt);
                sourceTexture?.SafeDestroy();
                resultTexture?.SafeDestroy();
            }
        }

        public static byte[] StripPngAlphaToGray(Stream imageStream)
        {
            if (imageStream == null || imageStream.Length == 0)
                return null;

            var imageBytes = imageStream.ReadFully();
            return StripPngAlphaToGray(imageBytes);
        }

        public static bool HasAlphaChannel(byte[] imageBytes)
        {
            if (!ImageFileTypeSupport.TryGetImageFormat(imageBytes, out var format))
            {
                // Unknown format, assume alpha channel to be safe.
                return true;
            }

            switch (format.primaryExtension)
            {
                case ".png":
                    return HasPngAlphaChannel(imageBytes);
                case ".jpg":
                case ".bmp":
                    return false;
                // For other formats like TIFF, GIF, PSD, EXR, HDR it's safer to assume they might have an alpha channel,
                // as parsing them fully is complex.
                default:
                    return true;
            }
        }

        public static bool HasAlphaChannel(Stream imageStream)
        {
            if (imageStream == null)
                throw new ArgumentNullException(nameof(imageStream));

            if (!imageStream.CanSeek)
                throw new NotSupportedException("The provided stream must be seekable.");

            var originalPosition = imageStream.Position;
            try
            {
                imageStream.Position = 0;

                const int headerSize = 1024;
                var headerBuffer = new byte[headerSize];
                var bytesRead = imageStream.Read(headerBuffer, 0, headerBuffer.Length);

                if (bytesRead == 0)
                {
                    // Empty stream, no alpha.
                    return false;
                }

                // The methods using this buffer are safe to use with a buffer that might be
                // larger than the actual content, as they only check the first few bytes for signatures.
                return HasAlphaChannel(headerBuffer);
            }
            finally
            {
                try { imageStream.Position = originalPosition; }
                catch { /* ignored */ }
            }
        }

        /// <summary>
        /// Checks if a texture's format has an alpha channel.
        /// </summary>
        /// <param name="texture">The texture to check.</param>
        /// <returns>True if an alpha channel is present.</returns>
        public static bool HasAlphaChannel(Texture2D texture)
        {
            if (texture == null)
                throw new ArgumentNullException(nameof(texture));            
            return GraphicsFormatUtility.HasAlphaChannel(texture.graphicsFormat);
        }

        // Explanation for the different endianness handling:

        /*
         * The methods handle endianness differently because:
         *
         * 1. TryGetJpegDimensions: Always uses big-endian (false parameter) because the JPEG file format
         *    specification requires that all multi-byte integers in the JPEG header structure are stored
         *    in big-endian (network byte order).
         *
         * 2. HasJpgOrientation: Uses variable endianness for EXIF metadata because the EXIF specification
         *    supports both endianness formats. The EXIF data block begins with either "II" (Intel, little-endian)
         *    or "MM" (Motorola, big-endian) to indicate which byte order is used.
         *
         *    - JPEG structure elements (like segment length) are still read as big-endian
         *    - EXIF data elements are read using the endianness specified in the EXIF header
         */

        public static bool HasJpgOrientation(Stream jpegStream)
        {
            var originalPosition = jpegStream.Position;
            const int headerSize = 1024;

            try
            {
                var buffer = new byte[12];

                _ = jpegStream.Read(buffer, 0, 2);
                if (buffer[0] != 0xFF || buffer[1] != 0xD8)
                {
                    return false;
                }

                long bytesRead = 2;
                while (bytesRead < headerSize && jpegStream.Position < jpegStream.Length)
                {
                    var markerStart = jpegStream.ReadByte();
                    var markerType = jpegStream.ReadByte();
                    bytesRead += 2;

                    if (markerStart != 0xFF)
                    {
                        break;
                    }

                    if (markerType == 0xE1)
                    {
                        _ = jpegStream.Read(buffer, 0, 2);
                        _ = ReadInt16(buffer, 0, false); // Use helper method consistently, always big-endian
                        bytesRead += 2;

                        _ = jpegStream.Read(buffer, 0, 6);
                        bytesRead += 6;

                        if (buffer[0] == 'E' && buffer[1] == 'x' && buffer[2] == 'i' &&
                            buffer[3] == 'f' && buffer[4] == 0 && buffer[5] == 0)
                        {
                            _ = jpegStream.Read(buffer, 0, 8);
                            bytesRead += 8;

                            var isLittleEndian = buffer[0] == 'I' && buffer[1] == 'I';
                            var ifdOffset = ReadInt32(buffer, 4, isLittleEndian);

                            jpegStream.Seek(ifdOffset - 8, SeekOrigin.Current);
                            bytesRead += ifdOffset - 8;

                            _ = jpegStream.Read(buffer, 0, 2);
                            bytesRead += 2;

                            var numEntries = ReadInt16(buffer, 0, isLittleEndian);
                            for (var i = 0; i < numEntries; i++)
                            {
                                _ = jpegStream.Read(buffer, 0, 12);
                                bytesRead += 12;

                                var tagId = ReadInt16(buffer, 0, isLittleEndian);
                                if (tagId != 0x0112)
                                {
                                    continue;
                                }

                                var orientationValue = ReadInt16(buffer, 8, isLittleEndian);
                                return orientationValue != 1;
                            }

                            return false;
                        }
                    }

                    if (markerType == 0xDA)
                    {
                        break;
                    }

                    if (markerType is 0xD9 or < 0xE0)
                    {
                        continue;
                    }

                    _ = jpegStream.Read(buffer, 0, 2);
                    var segmentLength = ReadInt16(buffer, 0, false); // Use helper method consistently, always big-endian
                    bytesRead += 2;

                    jpegStream.Seek(segmentLength - 2, SeekOrigin.Current);
                    bytesRead += segmentLength - 2;
                }

                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                jpegStream.Position = originalPosition;
            }
        }

        static int ReadInt16<T>(T bytes, int offset, bool isLittleEndian) where T : IReadOnlyList<byte>
        {
            return isLittleEndian
                ? bytes[offset] | (bytes[offset + 1] << 8)
                : (bytes[offset] << 8) | bytes[offset + 1];
        }

        static int ReadInt32<T>(T bytes, int offset, bool isLittleEndian) where T : IReadOnlyList<byte>
        {
            return isLittleEndian
                ? bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24)
                : (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
        }

        public static bool TryConvert(byte[] imageBytes, out byte[] destData, string toType = ".png")
        {
            destData = null;
            if (imageBytes == null || imageBytes.Length == 0)
            {
                return false;
            }

            Texture2D texture = null;
            Texture2D destTexture = null;
            RenderTexture renderTexture = null;

            try
            {
                // Ensure extension starts with a period
                var normalizedToType = toType.ToLowerInvariant();
                if (!normalizedToType.StartsWith("."))
                {
                    normalizedToType = "." + normalizedToType;
                }

                ImageFileTypeSupport.TryGetFormatForExtension(normalizedToType, out var format);

                // Detect source format
                TryGetImageExtension(imageBytes, out var sourceExtension);

                using var imageStream = new MemoryStream(imageBytes);
                var useAssetImporter = false;

                if (!string.IsNullOrEmpty(sourceExtension))
                {
                    // Check if we need to use asset importer based on content
                    useAssetImporter = ImageFileTypeSupport.RequiresTemporaryAsset(imageStream, sourceExtension);
                }

                if (useAssetImporter)
                {
                    // Use asset importer for special cases
                    texture = LoadWithAssetImporter(imageBytes);
                }
                else
                {
                    // For formats that support runtime loading
                    texture = new Texture2D(1, 1);
                    try
                    {
                        if (!texture.LoadImage(imageBytes))
                        {
                            texture.SafeDestroy();
                            texture = LoadWithAssetImporter(imageBytes);
                        }
                    }
                    catch
                    {
                        texture?.SafeDestroy();
                        texture = LoadWithAssetImporter(imageBytes);
                    }
                }

                var requiresLinearColorSpace = format?.requiresLinearColorSpace ?? false;
                if (requiresLinearColorSpace)
                {
                    renderTexture = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                    Graphics.Blit(texture, renderTexture);
                    destTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGBAHalf, false, true);

                    var prevRT = RenderTexture.active;
                    RenderTexture.active = renderTexture;
                    destTexture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
                    RenderTexture.active = prevRT;

                    if (ImageFileTypeSupport.TryEncodeTexture(destTexture, normalizedToType, out destData))
                    {
                        return true;
                    }
                }
                else
                {
                    if (ImageFileTypeSupport.TryEncodeTexture(texture, normalizedToType, out destData))
                    {
                        return true;
                    }
                }
            }
            finally
            {
                if (renderTexture != null)
                {
                    RenderTexture.ReleaseTemporary(renderTexture);
                }

                texture?.SafeDestroy();
                destTexture?.SafeDestroy();
            }

            return false;
        }

        const string k_ToolkitTemp = "Assets/AI Toolkit/Temp";

        static Texture2D LoadWithAssetImporter(byte[] imageBytes)
        {
            // Detect the file extension from the image bytes
            TryGetImageExtension(imageBytes, out var extension);
            if (string.IsNullOrEmpty(extension))
            {
                throw new NotSupportedException();
            }

            // Use Unity's asset import system to properly handle the image
            using var tempAsset = TemporaryAssetUtilities.ImportAssets(new[] { ($"{k_ToolkitTemp}/{Guid.NewGuid():N}{extension}", imageBytes) });
            var asset = tempAsset.assets[0].asset;
            var importedTexture = asset.GetObject<Texture2D>();

            // Create properly oriented readable texture
            return TryGetAspectRatio(asset, out var aspect)
                ? MakeTextureReadable(importedTexture, aspect)
                : MakeTextureReadable(importedTexture);
        }

        public static bool TryConvert(Stream imageStream, out Stream destStream, string toType = ".png")
        {
            if (imageStream == null)
            {
                throw new ArgumentNullException(nameof(imageStream));
            }

            if (!imageStream.CanSeek)
            {
                throw new NotSupportedException("The provided stream must be seekable.");
            }

            var normalizedToType = toType.ToLowerInvariant();
            if (!normalizedToType.StartsWith("."))
            {
                normalizedToType = "." + normalizedToType;
            }

            var originalPosition = imageStream.Position;
            try
            {
                imageStream.Position = 0;
                var formatMatches = TryGetImageExtension(imageStream, out var extension) &&
                    extension.Equals(normalizedToType, StringComparison.OrdinalIgnoreCase);
                if (formatMatches)
                {
                    imageStream.Position = 0;
                    destStream = imageStream;
                    return true;
                }

                imageStream.Position = 0;
                var imageBytes = imageStream.ReadFully();
                var success = TryConvert(imageBytes, out var destData, normalizedToType);
                destStream = success ? new MemoryStream(destData) : null;
                return success;
            }
            finally
            {
                if (imageStream.CanSeek)
                {
                    imageStream.Position = originalPosition;
                }
            }
        }

        public static byte[] CheckImageSize(byte[] imageBytes, int minimumSize = 32, int maximumSize = 8192)
        {
            if (!TryGetImageDimensions(imageBytes, out var width, out var height))
            {
                return imageBytes;
            }

            if (width < minimumSize || height < minimumSize)
            {
                var widthScale = minimumSize / (float)width;
                var heightScale = minimumSize / (float)height;
                var scale = Mathf.Max(widthScale, heightScale);

                var outputWidth = Mathf.RoundToInt(width * scale);
                var outputHeight = Mathf.RoundToInt(height * scale);

                return Resize(imageBytes, outputWidth, outputHeight);
            }

            if (width > maximumSize || height > maximumSize)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumSize),
                    $"Image size must be less than or equal to {maximumSize}x{maximumSize}. Actual: {width}x{height}.");
            }

            return imageBytes;
        }

        public static Stream CheckImageSize(Stream imageStream, int minimumSize = 32, int maximumSize = 8192)
        {
            if (imageStream == null)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumSize), "Image size must be at least 2x2.");
            }

            if (!imageStream.CanSeek)
            {
                throw new NotSupportedException("The provided stream must be seekable.");
            }

            if (!TryGetImageDimensions(imageStream, out var width, out var height))
            {
                return imageStream;
            }

            if (width < minimumSize || height < minimumSize)
            {
                var widthScale = minimumSize / (float)width;
                var heightScale = minimumSize / (float)height;
                var scale = Mathf.Max(widthScale, heightScale);

                var outputWidth = Mathf.RoundToInt(width * scale);
                var outputHeight = Mathf.RoundToInt(height * scale);

                var imageBytes = imageStream.ReadFully();
                imageBytes = Resize(imageBytes, outputWidth, outputHeight);
                imageStream.Dispose();
                return new MemoryStream(imageBytes);
            }

            if (width > maximumSize || height > maximumSize)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumSize),
                    $"Image size must be less than or equal to {maximumSize}x{maximumSize}. Actual: {width}x{height}.");
            }

            return imageStream;
        }

        /// <summary>
        /// Convert any texture into a readable Texture2D, optionally correcting the aspect if the texture is not already within tolerance of the given aspect (and the given aspect is > 0)
        /// by stretching to restore the original aspect ratio.
        /// </summary>
        public static Texture2D MakeTextureReadable(Texture sourceTexture, float aspect = -1)
        {
            const float aspectTolerance = 0.001f;

            if (!sourceTexture)
            {
                return null;
            }

            // Early return if texture is already readable AND (no aspect correction needed OR aspect already matches)
            if (sourceTexture is Texture2D { isReadable: true } texture2D)
            {
                var currentAspect = (float)texture2D.width / texture2D.height;
                if (aspect <= 0 || Mathf.Abs(currentAspect - aspect) < aspectTolerance)
                {
                    return texture2D;
                }
            }

            var previousActive = RenderTexture.active;
            RenderTexture rt = null;

            try
            {
                var outputWidth = sourceTexture.width;
                var outputHeight = sourceTexture.height;

                var currentAspect = (float)sourceTexture.width / sourceTexture.height;

                // Calculate new dimensions if aspect ratio correction is needed
                if (aspect > 0 && Mathf.Abs(currentAspect - aspect) >= aspectTolerance)
                {
                    // Determine new dimensions while maintaining approximately the same pixel count
                    float pixelCount = sourceTexture.width * sourceTexture.height;
                    outputHeight = Mathf.RoundToInt(Mathf.Sqrt(pixelCount / aspect));
                    outputWidth = Mathf.RoundToInt(outputHeight * aspect);
                }

                rt = RenderTexture.GetTemporary(outputWidth, outputHeight, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(sourceTexture, rt);
                RenderTexture.active = rt;

                // Create new readable texture
                var readableTexture = new Texture2D(outputWidth, outputHeight, TextureFormat.RGBA32, false);
                readableTexture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                readableTexture.Apply();

                return readableTexture;
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (rt)
                {
                    RenderTexture.ReleaseTemporary(rt);
                }
            }
        }

        public static byte[] Resize(byte[] imageBytes, int width, int height)
        {
            if (imageBytes == null || imageBytes.Length == 0)
            {
                return imageBytes;
            }

            var previousActive = RenderTexture.active;
            Texture2D sourceTexture = null;
            RenderTexture rt = null;

            try
            {
                sourceTexture = new Texture2D(2, 2);
                sourceTexture.LoadImage(imageBytes);

                rt = RenderTexture.GetTemporary(width, height);

                Graphics.Blit(sourceTexture, rt);

                var resultTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                resultTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                resultTexture.Apply();

                return resultTexture.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (rt)
                {
                    RenderTexture.ReleaseTemporary(rt);
                }

                sourceTexture?.SafeDestroy();
            }
        }

        public static async Task<(Texture2D texture, long timestamp)> GetCompatibleImageTextureAsync(Uri uri, bool linear = false)
        {
            var timestamp = GetLastModifiedUtcTime(uri);

            if (!uri.IsFile)
            {
                using var httpClientLease = HttpClientManager.instance.AcquireLease();

                var data = await DownloadImageWithFallback(uri, httpClientLease.client);
                await using var candidateStream = new MemoryStream(data);
                return (await LoadImageTextureAsync(candidateStream, linear), timestamp);
            }

            if (File.Exists(uri.GetLocalPath()))
            {
                await using Stream candidateStream = await FileIO.OpenReadWithRetryAsync(uri.GetLocalPath(), CancellationToken.None);
                return (await LoadImageTextureAsync(candidateStream, linear), timestamp);
            }

            return (null, timestamp);

            async Task<Texture2D> LoadImageTextureAsync(Stream stream, bool useLinear)
            {
                // Check if the image requires special import handling based on registered format handlers
                TryGetImageExtension(stream, out var extension);
                var requiresSpecialImport = string.IsNullOrEmpty(extension) ||
                    ImageFileTypeSupport.RequiresTemporaryAsset(stream, extension);

                if (!requiresSpecialImport)
                {
                    var loaded = new Texture2D(1, 1, TextureFormat.RGBA32, false, useLinear) { hideFlags = HideFlags.HideAndDontSave };
                    loaded.LoadImage(await stream.ReadFullyAsync());
                    return loaded;
                }

                using var temporaryAsset = await TemporaryAssetUtilities.ImportAssetsAsync(new[] { (uri.GetLocalPath(), stream) });

                var asset = temporaryAsset.assets[0].asset;
                var assetObj = asset.GetObject<UnityEngine.Object>();
                switch (assetObj)
                {
                    case Texture2D texture:
                    {
                        return TryGetAspectRatio(asset, out var aspect)
                            ? MakeTextureReadable(texture, aspect)
                            : MakeTextureReadable(texture);
                    }
                    case VideoClip videoClip:
                        return await videoClip.GetCompatiblePreviewAsync();
                    case GameObject gameObject:
                        return await gameObject.GetCompatiblePreviewAsync();
                }

                return null;
            }
        }

        public static (Texture2D texture, long timestamp) GetCompatibleImageTexture(Uri uri, bool linear = false)
        {
            var timestamp = GetLastModifiedUtcTime(uri);

            if (!uri.IsFile)
            {
                return (null, timestamp);
            }

            if (File.Exists(uri.GetLocalPath()))
            {
                using Stream candidateStream = FileIO.OpenReadAsync(uri.GetLocalPath());
                return (LoadImageTexture(candidateStream, linear), timestamp);
            }

            return (null, timestamp);

            Texture2D LoadImageTexture(Stream stream, bool useLinear)
            {
                // Check if the image requires special import handling based on registered format handlers
                TryGetImageExtension(stream, out var extension);
                var requiresSpecialImport = string.IsNullOrEmpty(extension) ||
                    ImageFileTypeSupport.RequiresTemporaryAsset(stream, extension);

                if (!requiresSpecialImport)
                {
                    var loaded = new Texture2D(1, 1, TextureFormat.RGBA32, false, useLinear) { hideFlags = HideFlags.HideAndDontSave };
                    loaded.LoadImage(stream.ReadFully());
                    return loaded;
                }

                using var temporaryAsset = TemporaryAssetUtilities.ImportAssets(new[] { (uri.GetLocalPath(), stream) });

                var asset = temporaryAsset.assets[0].asset;
                var referenceTexture = asset.GetObject<Texture2D>();
                var readableTexture = TryGetAspectRatio(asset, out var aspect)
                    ? MakeTextureReadable(referenceTexture, aspect)
                    : MakeTextureReadable(referenceTexture);

                return readableTexture;
            }
        }

        public static Stream GetCompatibleImageStream(Uri uri)
        {
            if (!uri.IsFile || !File.Exists(uri.GetLocalPath()))
            {
                return null;
            }

            Stream candidateStream = FileIO.OpenReadAsync(uri.GetLocalPath());
            return LoadCompatibleStream(candidateStream, uri.GetLocalPath());

            Stream LoadCompatibleStream(Stream stream, string filePath)
            {
                // Check if the image requires special import handling based on registered format handlers
                TryGetImageExtension(stream, out var extension);
                var requiresSpecialImport = string.IsNullOrEmpty(extension) ||
                    ImageFileTypeSupport.RequiresTemporaryAsset(stream, extension);

                if (!requiresSpecialImport)
                {
                    return stream;
                }

                using var temporaryAsset = TemporaryAssetUtilities.ImportAssets(new[] { filePath });

                var asset = temporaryAsset.assets[0].asset;
                var referenceTexture = asset.GetObject<Texture2D>();
                var readableTexture = TryGetAspectRatio(asset, out var aspect)
                    ? MakeTextureReadable(referenceTexture, aspect)
                    : MakeTextureReadable(referenceTexture);

                var bytes = readableTexture.EncodeToPNG();
                stream.Dispose();
                stream = new MemoryStream(bytes);

                if (readableTexture != referenceTexture)
                {
                    readableTexture.SafeDestroy();
                }

                return stream;
            }
        }

        public static async Task<Stream> GetCompatibleImageStreamAsync(Uri uri)
        {
            if (!uri.IsFile || !File.Exists(uri.GetLocalPath()))
            {
                return null;
            }

            Stream candidateStream = await FileIO.OpenReadWithRetryAsync(uri.GetLocalPath(), CancellationToken.None);
            return await LoadCompatibleStreamAsync(candidateStream, uri.GetLocalPath());

            async Task<Stream> LoadCompatibleStreamAsync(Stream stream, string filePath)
            {
                // Check if the image requires special import handling based on registered format handlers
                TryGetImageExtension(stream, out var extension);
                var requiresSpecialImport = string.IsNullOrEmpty(extension) ||
                    ImageFileTypeSupport.RequiresTemporaryAsset(stream, extension);

                if (!requiresSpecialImport)
                {
                    return stream;
                }

                using var temporaryAsset = await TemporaryAssetUtilities.ImportAssetsAsync(new[] { filePath });

                var asset = temporaryAsset.assets[0].asset;
                var assetObj = asset.GetObject<UnityEngine.Object>();
                switch (assetObj)
                {
                    case Texture2D texture:
                    {
                        var readableTexture = TryGetAspectRatio(asset, out var aspect)
                            ? MakeTextureReadable(texture, aspect)
                            : MakeTextureReadable(texture);

                        var bytes = readableTexture.EncodeToPNG();
                        await stream.DisposeAsync();
                        stream = new MemoryStream(bytes);

                        if (readableTexture != texture)
                        {
                            readableTexture.SafeDestroy();
                        }

                        break;
                    }
                    case VideoClip videoClip:
                    {
                        var readableTexture = await videoClip.GetCompatiblePreviewAsync();

                        var bytes = readableTexture.EncodeToPNG();
                        await stream.DisposeAsync();
                        stream = new MemoryStream(bytes);

                        readableTexture.SafeDestroy();
                        break;
                    }
                    case GameObject gameObject:
                    {
                        var readableTexture = await gameObject.GetCompatiblePreviewAsync();

                        var bytes = readableTexture.EncodeToPNG();
                        await stream.DisposeAsync();
                        stream = new MemoryStream(bytes);

                        readableTexture.SafeDestroy();
                        break;
                    }
                }

                return stream;
            }
        }

        public static async Task<byte[]> GetCompatibleBytesAsync(UnityEngine.Object assetObj)
        {
            switch (assetObj)
            {
                case Texture2D texture:
                {
                    var readableTexture = TryGetAspectRatio(texture, out var aspect)
                        ? MakeTextureReadable(texture, aspect)
                        : MakeTextureReadable(texture);

                    var bytes = readableTexture.EncodeToPNG();

                    if (readableTexture != texture)
                    {
                        readableTexture.SafeDestroy();
                    }

                    return bytes;
                }
                case VideoClip videoClip:
                {
                    var readableTexture = await videoClip.GetCompatiblePreviewAsync();

                    var bytes = readableTexture.EncodeToPNG();

                    readableTexture.SafeDestroy();
                    return bytes;
                }
                case GameObject gameObject:
                {
                    var readableTexture = await gameObject.GetCompatiblePreviewAsync();

                    var bytes = readableTexture.EncodeToPNG();

                    readableTexture.SafeDestroy();
                    return bytes;
                }
            }

            throw new NotSupportedException($"Asset type '{assetObj.GetType().Name}' is not supported for byte extraction.");
        }

        static byte[] s_FailImageBytes;

        static async Task<byte[]> DownloadImageWithFallback(Uri uri, HttpClient _)
        {
            using var uwr = UnityWebRequest.Get(uri.AbsoluteUri);
            await uwr.SendWebRequest();

            if (uwr.result != UnityWebRequest.Result.Success)
            {
                s_FailImageBytes ??= await FileIO.ReadAllBytesAsync("Packages/com.unity.ai.assistant/Modules/Unity.AI.Generators.IO/Icons/Fail.png");
                return s_FailImageBytes;
            }

            return uwr.downloadHandler.data;
        }

        static bool TryGetAspectRatio(string assetPath, out float aspect)
        {
            aspect = 1.0f;
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (!importer)
            {
                return false;
            }

            importer.GetSourceTextureWidthAndHeight(out var width, out var height);
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            aspect = (float)width / height;
            return true;
        }

        public static bool TryGetAspectRatio(AssetReference asset, out float aspect)
        {
            return TryGetAspectRatio(asset.GetPath(), out aspect);
        }

        public static bool TryGetAspectRatio(Texture asset, out float aspect)
        {
            return TryGetAspectRatio(AssetDatabase.GetAssetPath(asset), out aspect);
        }

        /// <summary>
        /// Asynchronously retrieves the first frame of a video clip as a Texture.
        /// This method uses the robust VideoProcessorJob to handle potential failures and retries.
        /// </summary>
        public static async Task<Texture2D> GetCompatiblePreviewAsync(this VideoClip videoClip)
        {
            if (!videoClip)
                return null;

            // The TaskCompletionSource is the bridge between the job's callback-style
            // execution and the async/await pattern of this method.
            var tcs = new TaskCompletionSource<Texture2D>();
            var job = new FirstFrameCaptureJob(videoClip, tcs);

            try
            {
                // Start the job. It will run in the background via EditorApplication.update.
                job.Start();
                return await tcs.Task;
            }
            catch (Exception e)
            {
                // If the job's TaskCompletionSource was set with an exception, it will be caught here.
                Debug.LogError($"Failed to get video preview for '{videoClip.name}'. Exception: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Asynchronously renders a static preview of a GameObject and returns it as a new, persistent 512x512 Texture2D.
        /// The caller of this method is responsible for destroying the returned Texture2D when it is no longer needed.
        /// </summary>
        /// <param name="gameObject">The GameObject to render.</param>
        /// <returns>A Task that resolves to a new 512x512 Texture2D, or null if rendering fails.</returns>
        public static async Task<Texture2D> GetCompatiblePreviewAsync(this GameObject gameObject)
        {
            if (gameObject == null)
                return null;

            RenderTexture renderTexture = null;
            try
            {
                const int previewSize = 512;

                renderTexture = RenderTexture.GetTemporary(previewSize, previewSize, 24, RenderTextureFormat.ARGB32);

                var tcs = new TaskCompletionSource<bool>();
                var job = new SingleFrameRenderJob(gameObject, tcs, 0, renderTexture);
                job.Start();
                await tcs.Task;

                // asynchronous readback from the GPU RenderTexture to a CPU-side Texture2D.
                return await ReadbackToTexture2DAsync(renderTexture);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to get mesh preview for '{gameObject.name}'. Exception: {e.Message}");
                return null;
            }
            finally
            {
                // release the temporary RenderTexture to prevent memory leaks.
                if (renderTexture != null)
                {
                    RenderTexture.ReleaseTemporary(renderTexture);
                }
            }

            static Task<Texture2D> ReadbackToTexture2DAsync(RenderTexture source)
            {
                var tcs = new TaskCompletionSource<Texture2D>();

                AsyncGPUReadback.Request(source, 0, request =>
                {
                    if (request.hasError)
                    {
                        tcs.TrySetException(new Exception("GPU readback failed."));
                        return;
                    }

                    try
                    {
                        // Create the final, persistent Texture2D that the caller will own.
                        var finalTexture = new Texture2D(request.width, request.height, TextureFormat.RGBA32, false);

                        // Load the raw pixel data from the GPU request into the texture's CPU buffer.
                        finalTexture.LoadRawTextureData(request.GetData<byte>());

                        // Upload the data from the CPU buffer to the GPU representation of the texture, making it renderable.
                        finalTexture.Apply();

                        // Complete the task, handing ownership of the new texture to the caller.
                        tcs.TrySetResult(finalTexture);
                    }
                    catch (Exception e)
                    {
                        tcs.TrySetException(e);
                    }
                });

                return tcs.Task;
            }
        }
    }
}
