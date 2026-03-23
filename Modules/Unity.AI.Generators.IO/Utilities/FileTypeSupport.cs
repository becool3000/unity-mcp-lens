using System;
using System.Collections.Generic;
using System.IO;
using Unity.AI.Toolkit.Asset;

namespace Unity.AI.Generators.IO.Utilities
{
    static class FileTypeSupport
    {
        public static string GetFileExtension(Stream stream, string defaultExtension = ".bin")
        {
            if (stream is not { CanSeek: true })
                throw new ArgumentException("Stream must be non-null and seekable", nameof(stream));

            var originalPosition = stream.Position;

            try
            {
                stream.Position = 0;

                // Read a buffer large enough for all signature checks (1024 should be more than enough)
                var buffer = new byte[1024];
                var bytesRead = stream.Read(buffer, 0, buffer.Length);
                var headerBytes = new byte[bytesRead];
                Array.Copy(buffer, headerBytes, bytesRead);

                // Use the centralized image format detection system
                if (ImageFileTypeSupport.TryGetImageExtension(headerBytes, out var imageExt))
                    return imageExt;

                // Check for audio types
                if (FileIO.IsWav(headerBytes))
                    return ".wav";

                if (FileIO.IsMp3(headerBytes))
                    return ".mp3";

                // Check for FBX
                if (FileIO.IsBinaryFbx(headerBytes))
                    return ".fbx";

                // Check for GLB
                if (FileIO.IsGlb(headerBytes))
                    return ".glb";

                // Check for JSON
                if (FileIO.IsJson(headerBytes))
                {
                    // Check for our animation format
                    if (FileIO.IsJsonPose(headerBytes))
                        return ".pose.json";

                    return ".json";
                }

                if (FileIO.IsMp4(headerBytes))
                    return ".mp4";

                // Unknown file type
                return defaultExtension;
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }

        // try to detect mime type from a stream header.
        public static bool TryGetMimeTypeFromStream(Stream stream, out string mimeType)
        {
            mimeType = null;

            if (stream is not { CanSeek: true })
                return false;

            var originalPosition = stream.Position;
            try
            {
                stream.Position = 0;

                var buffer = new byte[1024];
                var bytesRead = stream.Read(buffer, 0, buffer.Length);
                var headerBytes = new byte[bytesRead];
                Array.Copy(buffer, headerBytes, bytesRead);
                IReadOnlyList<byte> header = headerBytes;

                if (ImageFileTypeSupport.TryGetImageFormat(header, out var imageFormat))
                {
                    mimeType = imageFormat.mimeType;
                    return true;
                }

                if (FileIO.IsWav(header))
                {
                    mimeType = "audio/wav";
                    return true;
                }

                if (FileIO.IsMp3(header))
                {
                    mimeType = "audio/mpeg";
                    return true;
                }

                if (FileIO.IsBinaryFbx(header))
                {
                    mimeType = "application/octet-stream";
                    return true;
                }

                if (FileIO.IsGlb(header))
                {
                    mimeType = "model/gltf-binary";
                    return true;
                }

                if (FileIO.IsJson(header))
                {
                    mimeType = "application/json";
                    return true;
                }

                if (FileIO.IsMp4(header))
                {
                    mimeType = "video/mp4";
                    return true;
                }

                return false;
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }
    }
}
