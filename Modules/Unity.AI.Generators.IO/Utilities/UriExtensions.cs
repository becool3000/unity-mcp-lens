using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Generators.IO.Utilities
{
    static class UriExtensions
    {
        public static string GetAbsolutePath(this Uri uri) => uri != null ? Uri.UnescapeDataString(uri.AbsolutePath) : string.Empty;
        public static string GetLocalPath(this Uri uri) => uri != null ? uri.LocalPath : string.Empty;

        public static async Task<Uri> DownloadFile(Uri sourceUri, string destinationFolder, HttpClient httpClient, string destinationFileNameWithoutExtension = null, float timeoutSeconds = 60)
        {
            if (sourceUri.IsFile || !sourceUri.IsAbsoluteUri)
                throw new ArgumentException("The URI must represent a remote file (http, https, etc.)", nameof(sourceUri));

            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(sourceUri.Segments[^1]);
            var tempFilePath = Path.GetFullPath(FileUtil.GetUniqueTempPathInProject());
            var tempFilePathPartial = tempFilePath + ".part";
            const int maxRetries = 3;

            try
            {
                // Check destination folder permissions early
                EnsureDirectoryExists(destinationFolder);

                // Perform download with retries
                for (var attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                        using var response = await httpClient.GetAsync(sourceUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken: cancellationTokenSource.Token).ConfigureAwaitMainThread();
                        response.EnsureSuccessStatusCode();

                        // Check if we have enough disk space
                        var contentLength = response.Content.Headers.ContentLength;
                        if (contentLength.HasValue && !EnsureDiskSpace(contentLength.Value, Path.GetDirectoryName(tempFilePath)))
                            throw new IOException($"Insufficient disk space to download {BytesToMegabytes(contentLength.Value):F2}MB file");
                        {
                            await using var writeFileStream = FileIO.OpenWriteAsync(tempFilePathPartial);
#if FULLY_ASYNC
                            await using var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                            await contentStream.CopyToAsync(writeFileStream, cancellationTokenSource.Token).ConfigureAwait(false);
                            await writeFileStream.FlushAsync(cancellationTokenSource.Token).ConfigureAwaitMainThread();
#else
                            // this is safe, fast and tested and currently the best compromise
                            response.Content.CopyToAsync(writeFileStream).Wait();
                            writeFileStream.Flush();
#endif
                        }

                        // Rename from .part to actual temp file once complete
                        SafeMove(tempFilePathPartial, tempFilePath);
                        break; // Success - exit retry loop
                    }
                    catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException && attempt < maxRetries)
                    {
                        var reason = ex is OperationCanceledException ? "timed out" : $"failed: {ex.GetType().Name}";
                        Debug.LogWarning($"Download attempt {attempt}/{maxRetries} {reason}. Retrying...");
                        await EditorTask.Delay((int)TimeSpan.FromSeconds(Math.Pow(2, attempt)).TotalMilliseconds); // Exponential backoff
                    }
                }

                string extension;

                {
                    await using var fileStream = FileIO.OpenReadAsync(tempFilePath);
                    extension = FileTypeSupport.GetFileExtension(fileStream);

                    // Validate file content is as expected
                    if (string.IsNullOrEmpty(extension))
                        throw new InvalidDataException("Downloaded file has an unknown format");
                }

                destinationFileNameWithoutExtension ??= fileNameWithoutExtension;
                var destinationPath = Path.Combine(destinationFolder, $"{destinationFileNameWithoutExtension}{extension}");

                // Use safe copy method instead of move for reliability
                destinationPath = SafeCopyToDestination(tempFilePath, destinationPath);

                return new Uri(Path.GetFullPath(destinationPath));
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error downloading file from {sourceUri}: {ex.Message}");
                throw new HttpRequestException($"Failed to download file: {ex.Message}", ex);
            }
            finally
            {
                SafeDeleteFile(tempFilePathPartial);
                SafeDeleteFile(tempFilePath);
            }
        }

        static void SafeDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to delete temporary file {path}: {ex.Message}");
            }
        }

        static void SafeMove(string sourcePath, string destinationPath)
        {
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);

            // Try to move first (more efficient)
            try
            {
                File.Move(sourcePath, destinationPath);
            }
            catch (IOException)
            {
                // Fall back to copy+delete if move fails (e.g., across drives)
                File.Copy(sourcePath, destinationPath);
                SafeDeleteFile(sourcePath);
            }
        }

        static string SafeCopyToDestination(string sourcePath, string destinationPath)
        {
            try
            {
                if (File.Exists(destinationPath))
                    File.Delete(destinationPath);

                File.Copy(sourcePath, destinationPath);
                return destinationPath; // Success: return the original path
            }
            catch (IOException ex)
            {
                // The original path failed, so log the specific reason and try a fallback.
                var folder = Path.GetDirectoryName(destinationPath);
                var fileNameWithoutExt = Path.GetFileNameWithoutExtension(destinationPath);
                var extension = Path.GetExtension(destinationPath);
                var alternateDestination = Path.Combine(folder, $"{fileNameWithoutExt}_{DateTime.Now:yyyyMMdd_HHmmss}{extension}");

                // Use the exception message for better diagnostics.
                Debug.LogWarning($"Could not save to {destinationPath} ({ex.Message}). Trying alternate: {alternateDestination}");

                // Attempt the copy to the new alternate path.
                // If this fails, it will throw an exception that will be caught by the main try-catch block.
                File.Copy(sourcePath, alternateDestination);

                // Success: return the new, alternate path.
                return alternateDestination;
            }
        }

        static void EnsureDirectoryExists(string path)
        {
            if (Directory.Exists(path))
                return;

            try { Directory.CreateDirectory(path); }
            catch (Exception ex) { throw new IOException($"Cannot create directory at {path}: {ex.Message}", ex); }
        }

        static bool EnsureDiskSpace(long requiredBytes, string directoryPath)
        {
            try { return new DriveInfo(Path.GetPathRoot(directoryPath)).AvailableFreeSpace > requiredBytes * 1.5; } // 50% margin
            catch { return true; } // If we can't determine the space, assume it's sufficient
        }

        static double BytesToMegabytes(long bytes) => bytes / (1024.0 * 1024.0);
    }
}
