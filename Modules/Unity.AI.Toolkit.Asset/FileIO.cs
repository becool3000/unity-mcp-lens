using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace Unity.AI.Toolkit.Asset
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    static class FileIO
    {
        /// <summary>
        /// FileStream wrapper that logs creation and disposal with lock information
        /// </summary>
        class InstrumentedFileStream : FileStream
        {
#if LOG_TRACE
            readonly string m_Path;
            readonly bool m_LikelyCausesLock;
#endif
            public InstrumentedFileStream(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options)
                : base(path, mode, access, share, bufferSize, options)
            {
#if LOG_TRACE
                m_Path = path;
                // Write access without share write, or exclusive access (FileShare.None) likely causes locks
                m_LikelyCausesLock = (access == FileAccess.Write || access == FileAccess.ReadWrite || share == FileShare.None);

                Debug.Log($"FileStream opened: {m_Path} (Lock potential: {(m_LikelyCausesLock ? "High" : "Low")})");
#endif
            }
#if LOG_TRACE
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Debug.Log($"FileStream disposed: {m_Path} (Lock potential: {(m_LikelyCausesLock ? "High" : "Low")})");
                }
                base.Dispose(disposing);
            }
#endif
        }

        public static bool IsFileDirectChildOfFolder(string folderPath, string filePath)
        {
            folderPath = Path.GetFullPath(folderPath);
            filePath = Path.GetFullPath(filePath);

            var fileParentDirectory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(fileParentDirectory))
                return false;

            fileParentDirectory = Path.GetFullPath(fileParentDirectory);

            return string.Equals(folderPath, fileParentDirectory, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsJson(IReadOnlyList<byte> headerBytes)
        {
            // Skip any leading whitespace
            var i = 0;
            while (i < headerBytes.Count && (headerBytes[i] == ' ' || headerBytes[i] == '\t' || headerBytes[i] == '\n' || headerBytes[i] == '\r'))
                i++;

            // Check if the first non-whitespace character indicates JSON
            if (i >= headerBytes.Count || (headerBytes[i] != '{' && headerBytes[i] != '['))
                return false;

            // Basic JSON validation: try to read a bit further to make sure it's not just a lone bracket
            var hasContent = false;
            for (var j = i + 1; j < headerBytes.Count && !hasContent; j++)
            {
                var c = (char)headerBytes[j];
                if (c == '"' || c == 't' || c == 'f' || c == 'n' || (c >= '0' && c <= '9') || c == '-' || c == '{' || c == '[')
                    hasContent = true;
                else if (!(c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == ':' || c == ','))
                    break;
            }
            return hasContent;
        }

        public static bool IsJsonPose(IReadOnlyList<byte> headerBytes)
        {
            // First check if it looks like JSON (this is a quick pre-check)
            if (!IsJson(headerBytes))
                return false;

            try
            {
                // Convert just enough bytes to check for the frames property
                // No need for the full 1KB, we just need the beginning
                var maxLength = Math.Min(headerBytes.Count, 256);
                var jsonStart = Encoding.UTF8.GetString(headerBytes as byte[] ?? headerBytes.ToArray(), 0, maxLength);

                using var reader = new JsonTextReader(new StringReader(jsonStart));
                reader.DateParseHandling = DateParseHandling.None;

                // We only need to verify the beginning structure, not the entire document
                if (!reader.Read() || reader.TokenType != JsonToken.StartObject)
                    return false;

                if (!reader.Read() || reader.TokenType != JsonToken.PropertyName)
                    return false;

                var propertyName = reader.Value?.ToString();
                if (propertyName != "frames")
                    return false;

                if (!reader.Read() || reader.TokenType != JsonToken.StartArray)
                    return false;

                // We've found {"frames":[, which is enough to identify this as a pose JSON
                return true;
            }
            catch
            {
                // If we can't parse even this much, it's not what we're looking for
                return false;
            }
        }

        public static bool IsWav(IReadOnlyList<byte> data) =>
            data is { Count: >= 12 } &&
            data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F' &&
            data[8] == (byte)'W' && data[9] == (byte)'A' && data[10] == (byte)'V' && data[11] == (byte)'E';

        public static bool IsMp3(IReadOnlyList<byte> data)
        {
            if (data is not { Count: >= 3 })
                return false;

            // Check for ID3 tag
            if (data[0] == 0x49 && data[1] == 0x44 && data[2] == 0x33) // "ID3"
                return true;

            // Check for frame sync (common for MP3 without ID3 tag)
            // The first 11 bits of a frame header are all 1s.
            // This means the first byte is 0xFF and the second byte starts with 0b111... (>= 0xE0)
            if (data[0] == 0xFF && (data[1] & 0xE0) == 0xE0)
                return true;

            return false;
        }

        /// <summary>
        /// Checks if the byte sequence represents an MP4 file.
        /// An MP4 file should contain the 'ftyp' signature at offset 4.
        /// </summary>
        /// <param name="data">The first few bytes of the file (at least 8 bytes are required).</param>
        public static bool IsMp4(IReadOnlyList<byte> data) =>
            data is { Count: >= 8 } &&
            data[4] == (byte)'f' &&
            data[5] == (byte)'t' &&
            data[6] == (byte)'y' &&
            data[7] == (byte)'p';

        public static bool IsPng(IReadOnlyList<byte> imageBytes) =>
            imageBytes.Count >= 8 &&
            imageBytes[0] == 0x89 &&
            imageBytes[1] == 0x50 &&
            imageBytes[2] == 0x4E &&
            imageBytes[3] == 0x47 &&
            imageBytes[4] == 0x0D &&
            imageBytes[5] == 0x0A &&
            imageBytes[6] == 0x1A &&
            imageBytes[7] == 0x0A;

        public static bool IsJpg(IReadOnlyList<byte> imageBytes) => imageBytes[0] == 0xFF && imageBytes[1] == 0xD8;

        /// <summary>
        /// Checks if the byte sequence represents a GIF file by checking its 6-byte header.
        /// A GIF file must start with the ASCII characters "GIF87a" or "GIF89a".
        /// </summary>
        /// <param name="imageBytes">The first few bytes of the file (at least 6 bytes are required).</param>
        public static bool IsGif(IReadOnlyList<byte> imageBytes)
        {
            // 1. Basic validation: ensure we have enough bytes to check the header.
            if (imageBytes == null || imageBytes.Count < 6)
            {
                return false;
            }

            // 2. Check for the "GIF" signature (ASCII: G=0x47, I=0x49, F=0x46)
            var hasGifSignature = imageBytes[0] == 0x47 &&
                imageBytes[1] == 0x49 &&
                imageBytes[2] == 0x46;

            if (!hasGifSignature)
            {
                return false;
            }

            // 3. Check for a valid version string: "87a" or "89a"
            // ASCII: 8=0x38, 7=0x37, 9=0x39, a=0x61
            var isVersion87A = imageBytes[3] == 0x38 &&
                imageBytes[4] == 0x37 &&
                imageBytes[5] == 0x61;

            var isVersion89A = imageBytes[3] == 0x38 &&
                imageBytes[4] == 0x39 &&
                imageBytes[5] == 0x61;

            // The file is a GIF if the signature is correct AND the version is one of the valid ones.
            return isVersion87A || isVersion89A;
        }

        public static bool IsExr(IReadOnlyList<byte> imageBytes) => imageBytes.Count >= 4 && imageBytes[0] == 0x76 && imageBytes[1] == 0x2F &&
            imageBytes[2] == 0x31 && imageBytes[3] == 0x01;

        const string k_FbxHeader = "Kaydara FBX Binary";

        public static bool IsBinaryFbx(IReadOnlyList<byte> data) =>
            data != null && data.Count >= k_FbxHeader.Length &&
            Encoding.ASCII.GetString(data.ToArray(), 0, k_FbxHeader.Length).Equals(k_FbxHeader, StringComparison.Ordinal);

        public static bool IsGlb(IReadOnlyList<byte> data) =>
            data is { Count: >= 4 } &&
            data[0] == (byte)'g' && data[1] == (byte)'l' && data[2] == (byte)'T' && data[3] == (byte)'F';

        const string k_ExtendedPathPrefix = @"\\?\";

        static string GetFullPathWithExtendedPrefix(string path)
        {
            if (path.StartsWith(k_ExtendedPathPrefix))
                return path;

            var fullPath = Path.GetFullPath(path);
            if (fullPath.Length >= 260 && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return k_ExtendedPathPrefix + fullPath;

            return fullPath;
        }

        public static byte[] ReadAllBytes(string path)
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (Exception originalException)
            {
                // Only apply Windows-specific workarounds if file exists and on Windows
                if (!File.Exists(path) || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    throw;

                // Don't retry paths that already have the prefix
                if (path.StartsWith(k_ExtendedPathPrefix))
                    throw;

                try
                {
                    // Convert to full path and apply the prefix
                    var extendedPath = GetFullPathWithExtendedPrefix(path);
                    if (extendedPath == path) // No change needed
                        throw;

                    return File.ReadAllBytes(extendedPath);
                }
                catch (Exception)
                {
                    // If our recovery attempt failed, throw the original exception
                    // to preserve the root cause
                    throw originalException;
                }
            }
        }

        public static async Task<byte[]> ReadAllBytesAsync(string path)
        {
            try
            {
                return await File.ReadAllBytesAsync(path).ConfigureAwaitMainThread();
            }
            catch (Exception originalException)
            {
                // Only apply Windows-specific workarounds if file exists and on Windows
                if (!File.Exists(path) || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    throw;

                // Don't retry paths that already have the prefix
                if (path.StartsWith(k_ExtendedPathPrefix))
                    throw;

                try
                {
                    // Convert to full path and apply the prefix
                    var extendedPath = GetFullPathWithExtendedPrefix(path);
                    if (extendedPath == path) // No change needed
                        throw;

                    return await File.ReadAllBytesAsync(extendedPath).ConfigureAwaitMainThread();
                }
                catch (Exception)
                {
                    // If our recovery attempt failed, throw the original exception
                    // to preserve the root cause
                    throw originalException;
                }
            }
        }

        public static string ReadAllText(string path)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception originalException)
            {
                // Only apply Windows-specific workarounds if file exists and on Windows
                if (!File.Exists(path) || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    throw;

                // Don't retry paths that already have the prefix
                if (path.StartsWith(k_ExtendedPathPrefix))
                    throw;

                try
                {
                    // Convert to full path and apply the prefix
                    var extendedPath = GetFullPathWithExtendedPrefix(path);
                    if (extendedPath == path) // No change needed
                        throw;

                    return File.ReadAllText(extendedPath);
                }
                catch (Exception)
                {
                    // If our recovery attempt failed, throw the original exception
                    // to preserve the root cause
                    throw originalException;
                }
            }
        }

        public static async Task<string> ReadAllTextAsync(string path)
        {
            try
            {
                return await File.ReadAllTextAsync(path).ConfigureAwaitMainThread();
            }
            catch (Exception originalException)
            {
                // Only apply Windows-specific workarounds if file exists and on Windows
                if (!File.Exists(path) || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    throw;

                // Don't retry paths that already have the prefix
                if (path.StartsWith(k_ExtendedPathPrefix))
                    throw;

                try
                {
                    // Convert to full path and apply the prefix
                    var extendedPath = GetFullPathWithExtendedPrefix(path);
                    if (extendedPath == path) // No change needed
                        throw;

                    return await File.ReadAllTextAsync(extendedPath).ConfigureAwaitMainThread();
                }
                catch (Exception)
                {
                    // If our recovery attempt failed, throw the original exception
                    // to preserve the root cause
                    throw originalException;
                }
            }
        }

        public static void WriteAllBytes(string path, byte[] bytes)
        {
            try
            {
                File.WriteAllBytes(path, bytes);
            }
            catch (Exception originalException)
            {
                // Only apply Windows-specific workarounds on Windows
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    throw;

                // Don't retry paths that already have the prefix
                if (path.StartsWith(k_ExtendedPathPrefix))
                    throw;

                try
                {
                    // Convert to full path and apply the prefix
                    var extendedPath = GetFullPathWithExtendedPrefix(path);
                    if (extendedPath == path) // No change needed
                        throw;

                    File.WriteAllBytes(extendedPath, bytes);
                }
                catch (Exception)
                {
                    // If our recovery attempt failed, throw the original exception
                    // to preserve the root cause
                    throw originalException;
                }
            }
        }

        public static async Task WriteAllBytesAsync(string path, byte[] bytes)
        {
            try
            {
                await File.WriteAllBytesAsync(path, bytes).ConfigureAwaitMainThread();
            }
            catch (Exception originalException)
            {
                // Only apply Windows-specific workarounds on Windows
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    throw;

                // Don't retry paths that already have the prefix
                if (path.StartsWith(k_ExtendedPathPrefix))
                    throw;

                try
                {
                    // Convert to full path and apply the prefix
                    var extendedPath = GetFullPathWithExtendedPrefix(path);
                    if (extendedPath == path) // No change needed
                        throw;

                    await File.WriteAllBytesAsync(extendedPath, bytes).ConfigureAwaitMainThread();
                }
                catch (Exception)
                {
                    // If our recovery attempt failed, throw the original exception
                    // to preserve the root cause
                    throw originalException;
                }
            }
        }

        public static void WriteAllText(string path, string contents)
        {
            try
            {
                File.WriteAllText(path, contents);
            }
            catch (Exception originalException)
            {
                // Only apply Windows-specific workarounds on Windows
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    throw;

                // Don't retry paths that already have the prefix
                if (path.StartsWith(k_ExtendedPathPrefix))
                    throw;

                try
                {
                    // Convert to full path and apply the prefix
                    var extendedPath = GetFullPathWithExtendedPrefix(path);
                    if (extendedPath == path) // No change needed
                        throw;

                    File.WriteAllText(extendedPath, contents);
                }
                catch (Exception)
                {
                    // If our recovery attempt failed, throw the original exception
                    // to preserve the root cause
                    throw originalException;
                }
            }
        }

        public static async Task WriteAllTextAsync(string path, string contents)
        {
            try
            {
                await File.WriteAllTextAsync(path, contents).ConfigureAwaitMainThread();
            }
            catch (Exception originalException)
            {
                // Only apply Windows-specific workarounds on Windows
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    throw;

                // Don't retry paths that already have the prefix
                if (path.StartsWith(k_ExtendedPathPrefix))
                    throw;

                try
                {
                    // Convert to full path and apply the prefix
                    var extendedPath = GetFullPathWithExtendedPrefix(path);
                    if (extendedPath == path) // No change needed
                        throw;

                    await File.WriteAllTextAsync(extendedPath, contents).ConfigureAwaitMainThread();
                }
                catch (Exception)
                {
                    // If our recovery attempt failed, throw the original exception
                    // to preserve the root cause
                    throw originalException;
                }
            }
        }

        public static void WriteAllBytes(string path, Stream inputStream)
        {
            long originalPosition = 0;
            var canSeek = inputStream.CanSeek;
            if (canSeek)
                originalPosition = inputStream.Position;

            try
            {
                if (canSeek)
                    inputStream.Position = 0;

                using var fileStream = OpenFileStreamInternal(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, false ? FileOptions.Asynchronous : FileOptions.None);
                inputStream.CopyTo(fileStream);
            }
            finally
            {
                if (canSeek)
                {
                    try { inputStream.Position = originalPosition; }
                    catch { /* ignored */ }
                }
            }
        }

        public static async Task WriteAllBytesAsync(string path, Stream inputStream)
        {
            long originalPosition = 0;
            var canSeek = inputStream.CanSeek;
            if (canSeek)
                originalPosition = inputStream.Position;

            try
            {
                if (canSeek)
                    inputStream.Position = 0;

                await using var fileStream = OpenFileStreamInternal(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
                await inputStream.CopyToAsync(fileStream).ConfigureAwaitMainThread();
            }
            finally
            {
                if (canSeek)
                {
                    try { inputStream.Position = originalPosition; }
                    catch { /* ignored */ }
                }
            }
        }

        public static void CopyFile(string sourceFileName, string destFileName, bool overwrite)
        {
            try
            {
                File.Copy(sourceFileName, destFileName, overwrite);
            }
            catch (Exception originalException)
            {
                // Handle Path.GetFullPath errors, access issues, etc.
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                    sourceFileName.StartsWith(k_ExtendedPathPrefix) ||
                    destFileName.StartsWith(k_ExtendedPathPrefix))
                    throw;

                try
                {
                    // Apply extended path prefix for Windows long paths
                    var extendedSourcePath = GetFullPathWithExtendedPrefix(sourceFileName);
                    var extendedDestPath = GetFullPathWithExtendedPrefix(destFileName);

                    // Only retry if paths were actually changed
                    if (extendedSourcePath == sourceFileName && extendedDestPath == destFileName)
                        throw;

                    File.Copy(extendedSourcePath, extendedDestPath, overwrite);
                }
                catch (Exception)
                {
                    throw originalException;
                }
            }
        }

        public static async Task CopyFileAsync(string sourceFileName, string destFileName, bool overwrite,
            int timeoutSeconds = 30, int retryDelayMs = 250)
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            var token = timeoutCts.Token;

            var success = await TryCopyWithRetryAsync(sourceFileName, destFileName, overwrite, token, retryDelayMs);

            // First try standard paths
            if (!success &&
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                !sourceFileName.StartsWith(k_ExtendedPathPrefix) &&
                !destFileName.StartsWith(k_ExtendedPathPrefix))
            {
                // If that fails and we're on Windows, try with extended paths
                var extendedSourcePath = GetFullPathWithExtendedPrefix(sourceFileName);
                var extendedDestPath = GetFullPathWithExtendedPrefix(destFileName);

                // Only retry if paths were actually changed
                if (extendedSourcePath != sourceFileName || extendedDestPath != destFileName)
                {
                    success = await TryCopyWithRetryAsync(extendedSourcePath, extendedDestPath, overwrite, token, retryDelayMs);
                }
            }

            // If all attempts have failed, log a descriptive error.
            if (!success)
            {
                Debug.LogError($"Failed to copy file from '{sourceFileName}' to '{destFileName}'. The operation timed out or encountered a persistent error. Please check the preceding exception log for details.");
            }
        }

        static async Task<bool> TryCopyWithRetryAsync(string sourceFileName, string destFileName, bool overwrite, CancellationToken token, int retryDelayMs)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        File.Copy(sourceFileName, destFileName, overwrite);
                        return true;
                    }
                    catch (IOException)
                    {
                        try { await EditorTask.Delay(retryDelayMs, token); } // Back off briefly before retry
                        catch (OperationCanceledException) { break; } // Exit loop on cancellation during delay
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            return false;
        }

        static FileStream OpenFileStreamInternal(string path, FileMode fileMode, FileAccess fileAccess, FileShare fileShare, int bufferSize, FileOptions options)
        {
            try
            {
                return new InstrumentedFileStream(path, fileMode, fileAccess, fileShare, bufferSize, options);
            }
            catch (Exception originalException)
            {
                // Only apply Windows-specific workarounds if we're on Windows
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    throw;

                // Don't retry paths that already have the prefix
                if (path.StartsWith(k_ExtendedPathPrefix))
                    throw;

                try
                {
                    // Convert to full path and apply the prefix
                    var extendedPath = GetFullPathWithExtendedPrefix(path);
                    if (extendedPath == path) // No change needed
                        throw;

                    return new InstrumentedFileStream(extendedPath, fileMode, fileAccess, fileShare, bufferSize, options);
                }
                catch (Exception)
                {
                    // If our recovery attempt failed, throw the original exception
                    // to preserve the root cause
                    throw originalException;
                }
            }
        }

        public static FileStream OpenFileStream(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options) =>
            OpenFileStreamInternal(path, mode, access, share, bufferSize, options);

        public static FileStream OpenRead(string path) =>
            OpenFileStreamInternal(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.None);

        public static FileStream OpenReadAsync(string path) =>
            OpenFileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

        public static FileStream OpenWrite(string path) =>
            OpenFileStreamInternal(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.None);

        public static FileStream OpenWriteAsync(string path) =>
            OpenFileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);

        public static async Task<FileStream> OpenReadWithRetryAsync(string path, CancellationToken token,
            int maxRetries = 5, int initialRetryDelayMs = 100)
        {
            Exception lastException = null;
            var retryDelay = initialRetryDelayMs;

            for (var attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    return OpenReadAsync(path);
                }
                catch (IOException ex) when (ex.Message.Contains("Sharing violation"))
                {
                    lastException = ex;
#if LOG_TRACE
                    Debug.LogWarning($"Sharing violation on attempt {attempt + 1}/{maxRetries} for file {path}. Retrying...");
#endif
                }
                catch (Exception ex) when (attempt < maxRetries - 1 && (ex is IOException || ex is UnauthorizedAccessException))
                {
                    lastException = ex;
#if LOG_TRACE
                    Debug.LogWarning($"IO error on attempt {attempt + 1}/{maxRetries} for file {path}: {ex.Message}. Retrying...");
#endif
                }

                if (attempt < maxRetries - 1)
                {
                    try
                    {
                        await EditorTask.Delay(retryDelay, token);
                        // Use exponential backoff with a slight randomization
                        retryDelay = (int)(retryDelay * 1.5 + UnityEngine.Random.Range(0, 50));
                    }
                    catch (OperationCanceledException)
                    {
                        throw new OperationCanceledException("File operation was canceled", token);
                    }
                }
            }

            throw new IOException($"Failed to complete file operation after {maxRetries} attempts.", lastException);
        }
    }
}
