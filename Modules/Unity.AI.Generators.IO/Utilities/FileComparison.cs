using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using Unity.AI.Toolkit.Asset;

namespace Unity.AI.Generators.IO.Utilities
{
    /// <summary>
    /// Options for the file comparison
    /// </summary>
    /// <param name="getBytes1">Retrieves the bytes from path1.</param>
    /// <param name="getBytes2">Retrieves the bytes from path2.</param>
    record FileComparisonOptions(string path1, string path2, bool getBytes1 = false, bool getBytes2 = false)
    {
        public byte[] bytes1;   // Bytes that were read from path1 while comparing (if any)
        public byte[] bytes2;   // Bytes that were read from path2 while comparing (if any)
    }

    static class FileComparison
    {
        // Define the size limit as a constant for clarity and maintainability. 1 GB in bytes.
        const long k_MaxFileSizeForInMemoryRead = 1L * 1024 * 1024 * 1024;

        /// <summary>
        /// Compares two files for identical content.
        /// If <c>getBytes</c> is true, the entire file is read into a <see cref="MemoryStream"/> before computing the hash.
        /// This could be inefficient for large files.
        /// </summary>
        public static bool AreFilesIdentical(string path1, string path2) => AreFilesIdentical(new(path1, path2));

        /// <summary>
        /// Compares two files for identical content using the specified options.
        /// If <c>getBytes1</c> or <c>getBytes2</c> is true, the entire file is read into a <see cref="MemoryStream"/> before computing the hash.
        /// This will throw an <see cref="OutOfMemoryException"/> for files larger than 1GB to prevent application crashes.
        /// </summary>
        public static bool AreFilesIdentical(FileComparisonOptions options)
        {
            if (string.IsNullOrEmpty(options.path1) || string.IsNullOrEmpty(options.path2))
                return false;

            var fileInfo1 = new FileInfo(options.path1);
            var fileInfo2 = new FileInfo(options.path2);

            if (!fileInfo1.Exists || !fileInfo2.Exists)
                return false;

            if (fileInfo1.Length != fileInfo2.Length)
                return false;

            // If the caller wants the file bytes, check if the file size exceeds the defined limit.
            if ((options.getBytes1 || options.getBytes2) && fileInfo1.Length > k_MaxFileSizeForInMemoryRead)
            {
                throw new OutOfMemoryException(
                    $"File '{options.path1}' is too large ({fileInfo1.Length} bytes) to be read entirely into memory. " +
                    $"The maximum allowed size is {k_MaxFileSizeForInMemoryRead} bytes (1 GB).");
            }

            using Stream fileStream1 = FileIO.OpenReadAsync(options.path1);
            using Stream fileStream2 = FileIO.OpenReadAsync(options.path2);

            using Stream readStream1 = options.getBytes1 ? new MemoryStream(options.bytes1 = fileStream1.ReadFully()) : null;
            using Stream readStream2 = options.getBytes2 ? new MemoryStream(options.bytes2 = fileStream2.ReadFully()) : null;

            using var sha256 = SHA256.Create();
            var hash1 = sha256.ComputeHash(readStream1 ?? fileStream1);
            var hash2 = sha256.ComputeHash(readStream2 ?? fileStream2);
            return StructuralComparisons.StructuralEqualityComparer.Equals(hash1, hash2);
        }
    }
}
