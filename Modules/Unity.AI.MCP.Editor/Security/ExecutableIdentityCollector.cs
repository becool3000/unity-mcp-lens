using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.Models;
using UnityEngine;

namespace Unity.AI.MCP.Editor.Security
{
    /// <summary>
    /// Collects cryptographic identity information for executables.
    /// Includes file hash (SHA256) and code signature information (platform-specific).
    /// Uses caching to avoid expensive re-computation for unchanged executables.
    /// </summary>
    static class ExecutableIdentityCollector
    {
        /// <summary>
        /// Cached identity information for executables
        /// </summary>
        class CachedIdentity
        {
            public ExecutableIdentity Identity;
            public DateTime CachedAt;
            public DateTime FileModTime;
        }

        // Cache keyed by executable path
        static readonly Dictionary<string, CachedIdentity> identityCache = new();
        static readonly object cacheLock = new();

        // Cache entries older than this are invalidated
        const double CacheExpirationSeconds = 60.0;

        /// <summary>
        /// Collect full cryptographic identity for an executable (with caching)
        /// </summary>
        public static ExecutableIdentity CollectIdentity(string executablePath)
        {
            if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
            {
                return new ExecutableIdentity
                {
                    Path = executablePath,
                    IsSigned = false,
                    SignatureValid = false
                };
            }

            // Check cache first
            DateTime fileModTime = File.GetLastWriteTime(executablePath);
            lock (cacheLock)
            {
                if (identityCache.TryGetValue(executablePath, out var cached))
                {
                    // Cache hit: same file modification time and not expired
                    bool sameFile = cached.FileModTime == fileModTime;
                    bool notExpired = (DateTime.UtcNow - cached.CachedAt).TotalSeconds < CacheExpirationSeconds;

                    if (sameFile && notExpired)
                    {
                        return cached.Identity;
                    }
                }
            }

            // Cache miss: compute expensive identity
            var identity = new ExecutableIdentity
            {
                Path = executablePath,
                LastModified = fileModTime,
                SHA256Hash = ComputeSHA256(executablePath)
            };

            // Collect signature information (platform-specific)
            CollectSignatureInfo(executablePath, identity);

            // Update cache
            lock (cacheLock)
            {
                identityCache[executablePath] = new CachedIdentity
                {
                    Identity = identity,
                    CachedAt = DateTime.UtcNow,
                    FileModTime = fileModTime
                };
            }

            return identity;
        }

        /// <summary>
        /// Compute SHA256 hash of the executable file
        /// </summary>
        static string ComputeSHA256(string filePath)
        {
            try
            {
                using (var sha256 = SHA256.Create())
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hashBytes = sha256.ComputeHash(stream);
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }
            }
            catch (Exception ex)
            {
                McpLog.Warning($"Failed to compute SHA256 for {filePath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Collect code signature information (platform-specific)
        /// </summary>
        static void CollectSignatureInfo(string executablePath, ExecutableIdentity identity)
        {
            #if UNITY_EDITOR_WIN
            CollectWindowsSignature(executablePath, identity);
            #elif UNITY_EDITOR_OSX
            CollectMacSignature(executablePath, identity);
            #elif UNITY_EDITOR_LINUX
            // Linux doesn't have standard code signing
            identity.IsSigned = false;
            identity.SignatureValid = false;
            #endif
        }

        #if UNITY_EDITOR_WIN
        /// <summary>
        /// Collect Windows Authenticode signature information
        /// </summary>
        private static void CollectWindowsSignature(string executablePath, ExecutableIdentity identity)
        {
            try
            {
                X509Certificate cert = X509Certificate.CreateFromSignedFile(executablePath);
                if (cert == null)
                {
                    identity.IsSigned = false;
                    identity.SignatureValid = false;
                    return;
                }

                using (cert)
                {
                    identity.IsSigned = true;
                    identity.SignatureSubject = cert.Subject;

                    // Extract CN (Common Name) as publisher and friendly name
                    if (cert.Subject.Contains("CN="))
                    {
                        int start = cert.Subject.IndexOf("CN=") + 3;
                        int end = cert.Subject.IndexOf(",", start);
                        if (end == -1) end = cert.Subject.Length;
                        string cn = cert.Subject.Substring(start, end - start).Trim();
                        identity.SignaturePublisher = "CN=" + cn;
                        identity.SignatureFriendlyName = cn; // Friendly name without "CN=" prefix
                    }

                    // Validate certificate chain
                    var cert2 = new X509Certificate2(cert);
                    using (cert2)
                    {
                        var chain = new X509Chain();
                        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
                        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

                        identity.SignatureValid = chain.Build(cert2);
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"Failed to collect Windows signature for {executablePath}: {ex.Message}");
                identity.IsSigned = false;
                identity.SignatureValid = false;
            }
        }
        #endif

        #if UNITY_EDITOR_OSX
        /// <summary>
        /// Collect Mac codesign signature information
        /// </summary>
        static void CollectMacSignature(string executablePath, ExecutableIdentity identity)
        {
            try
            {
                // Step 1: Verify signature validity
                var verifyProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/usr/bin/codesign",
                        Arguments = $"--verify --deep --strict \"{executablePath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                verifyProcess.Start();
                string verifyOutput = verifyProcess.StandardError.ReadToEnd();
                verifyProcess.WaitForExit();

                identity.IsSigned = verifyProcess.ExitCode == 0;
                identity.SignatureValid = verifyProcess.ExitCode == 0;

                if (!identity.IsSigned)
                {
                    return;
                }

                // Step 2: Extract Team ID and other info
                var displayProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/usr/bin/codesign",
                        Arguments = $"--display --verbose=4 \"{executablePath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                displayProcess.Start();
                string displayOutput = displayProcess.StandardError.ReadToEnd();
                displayProcess.WaitForExit();

                // Parse Team ID and Authority (friendly name)
                foreach (var line in displayOutput.Split('\n'))
                {
                    if (line.Contains("TeamIdentifier="))
                    {
                        int start = line.IndexOf("TeamIdentifier=") + 15;
                        identity.SignaturePublisher = line.Substring(start).Trim();
                    }
                    else if (line.Contains("Authority=Developer ID Application:") || line.Contains("Authority=Apple Development:"))
                    {
                        // Extract friendly name from Authority line
                        // Format: "Authority=Developer ID Application: Company Name (TEAMID)"
                        int start = line.IndexOf("Authority=") + 10;
                        string authority = line.Substring(start).Trim();

                        // Remove the prefix (e.g., "Developer ID Application: ")
                        if (authority.Contains(":"))
                        {
                            authority = authority.Substring(authority.IndexOf(":") + 1).Trim();
                        }

                        // Remove the Team ID in parentheses if present
                        if (authority.Contains("(") && authority.Contains(")"))
                        {
                            int parenStart = authority.LastIndexOf("(");
                            authority = authority.Substring(0, parenStart).Trim();
                        }

                        identity.SignatureFriendlyName = authority;
                    }
                }

                identity.SignatureSubject = displayOutput;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"Failed to collect Mac signature for {executablePath}: {ex.Message}");
                identity.IsSigned = false;
                identity.SignatureValid = false;
            }
        }
        #endif
    }
}
