using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Unity.AI.Assistant.Utils;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Checkpoint.Git
{
    static class GitInstanceResolver
    {
#if UNITY_EDITOR_WIN
        const string k_GitExecutable = "git.exe";
        const string k_GitLFSExecutable = "git-lfs.exe";
        const string k_PlatformFolder = "win";
#elif UNITY_EDITOR_OSX
        const string k_GitExecutable = "git";
        const string k_GitLFSExecutable = "git-lfs";
        const string k_PlatformFolder = "osx";
        static readonly string[] k_EmbeddedExecutables = { "git", "git-lfs", "git-credential-osxkeychain", "scalar", "git-filter-branch" };
        static readonly string[] k_AdditionalPaths = { "/opt/homebrew/bin", "/usr/local/bin", "/opt/local/bin", "/usr/local/git/bin" };
#else
        const string k_GitExecutable = "git";
        const string k_GitLFSExecutable = "git-lfs";
        const string k_PlatformFolder = "linux";
        static readonly string[] k_EmbeddedExecutables = { "git", "git-lfs" };
#endif

        const int k_ValidationTimeoutMs = 10000;

        static string s_CachedEmbeddedPath;
        static bool s_EmbeddedPermissionsSet;

        public static string ResolvePath(GitInstanceConfig config)
        {
            return config.Type switch
            {
                GitInstanceType.Embedded => GetEmbeddedGitPath(),
                GitInstanceType.System => GetSystemGitPath(),
                GitInstanceType.Custom => config.CustomPath ?? string.Empty,
                _ => GetEmbeddedGitPath()
            };
        }

        /// <summary>
        /// Finds the first valid git instance type, checking in order: Embedded, System, Custom.
        /// Returns null if no valid instance is found.
        /// </summary>
        public static GitInstanceType? FindFirstValidInstance(string customPath = null)
        {
            if (ValidateGitInstance(GetEmbeddedGitPath()).IsValid)
                return GitInstanceType.Embedded;

            if (ValidateGitInstance(GetSystemGitPath()).IsValid)
                return GitInstanceType.System;

            if (!string.IsNullOrEmpty(customPath) && ValidateGitInstance(customPath).IsValid)
                return GitInstanceType.Custom;

            return null;
        }

        public static string GetEmbeddedGitPath()
        {
            if (!string.IsNullOrEmpty(s_CachedEmbeddedPath))
            {
                return s_CachedEmbeddedPath;
            }

            var relativePath = $"Packages/{AssistantConstants.PackageName}/git-instance/{k_PlatformFolder}/bin";

            try
            {
                var absolutePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", relativePath));

                if (Directory.Exists(absolutePath))
                {
                    s_CachedEmbeddedPath = Path.Combine(absolutePath, k_GitExecutable);
                    EnsureEmbeddedPermissions(absolutePath);
                    return s_CachedEmbeddedPath;
                }
            }
            catch (Exception ex)
            {
                InternalLog.LogWarning($"Failed to resolve embedded git path: {ex.Message}");
            }

            return Path.Combine(relativePath, k_GitExecutable);
        }

        static void EnsureEmbeddedPermissions(string binDirectory)
        {
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            if (s_EmbeddedPermissionsSet)
                return;

            try
            {
                foreach (var exec in k_EmbeddedExecutables)
                {
                    var execPath = Path.Combine(binDirectory, exec);
                    if (File.Exists(execPath))
                        SetExecutePermission(execPath);
                }

                s_EmbeddedPermissionsSet = true;
            }
            catch (Exception ex)
            {
                InternalLog.LogWarning($"Failed to set execute permissions: {ex.Message}");
            }
#endif
        }

#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
        static void SetExecutePermission(string filePath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "/bin/chmod",
                    Arguments = $"+x \"{filePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                process?.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                InternalLog.LogWarning($"Failed to set execute permission on {filePath}: {ex.Message}");
            }
        }
#endif

#if UNITY_EDITOR_OSX
        static void AugmentPath(ProcessStartInfo startInfo)
        {
            var path = startInfo.EnvironmentVariables["PATH"] ?? "";

            foreach (var dir in k_AdditionalPaths)
            {
                if (!path.Contains(dir))
                    path = $"{path}:{dir}";
            }

            startInfo.EnvironmentVariables["PATH"] = path;
        }
#endif

        public static string GetSystemGitPath()
        {
            return k_GitExecutable;
        }

        public static (bool found, string version, string path) DetectSystemGit()
        {
            try
            {
                var result = RunGitCommand(k_GitExecutable, "--version");
                if (result.success)
                {
                    var version = ParseGitVersion(result.output);
                    return (true, version, k_GitExecutable);
                }
            }
            catch (Exception ex)
            {
                InternalLog.LogWarning($"System git detection failed: {ex.Message}");
            }

            return (false, null, null);
        }

        public static string GetEmbeddedGitVersion()
        {
            var path = GetEmbeddedGitPath();
            var result = RunGitCommand(path, "--version");
            return result.success ? ParseGitVersion(result.output) : null;
        }

        public static GitValidationResult ValidateGitInstance(string gitPath)
        {
            if (string.IsNullOrEmpty(gitPath))
            {
                return GitValidationResult.GitNotFound(gitPath);
            }

            if (!gitPath.Equals(k_GitExecutable, StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(gitPath))
                {
                    return GitValidationResult.GitNotFound(gitPath);
                }
            }

            var gitResult = RunGitCommand(gitPath, "--version");
            if (!gitResult.success)
            {
                if (!string.IsNullOrEmpty(gitResult.error) && gitResult.error.IndexOf("Cannot find the specified file", StringComparison.OrdinalIgnoreCase) != -1)
                {
                    return GitValidationResult.GitNotFound(gitPath);
                }
                
                return GitValidationResult.Error(gitPath, $"Git execution failed: {gitResult.error}");
            }

            var gitVersion = ParseGitVersion(gitResult.output);
            if (string.IsNullOrEmpty(gitVersion))
            {
                return GitValidationResult.Error(gitPath, "Could not parse Git version");
            }
            
            var lfsResultDirect = RunGitCommand(k_GitLFSExecutable, "version");
            var lfsResultParam = RunGitCommand(gitPath, "lfs version");
            if (!lfsResultDirect.success && !lfsResultParam.success)
            {
                return GitValidationResult.LfsMissing(gitPath, gitVersion);
            }

            var lfsOutput = lfsResultDirect.success ? lfsResultDirect.output : lfsResultParam.output;
            var lfsVersion = ParseLfsVersion(lfsOutput);
            if (string.IsNullOrEmpty(lfsVersion))
            {
                return GitValidationResult.LfsMissing(gitPath, gitVersion);
            }

            return GitValidationResult.Valid(gitPath, gitVersion, lfsVersion);
        }

        public static GitValidationResult ValidateConfig(GitInstanceConfig config)
        {
            var path = ResolvePath(config);
            return ValidateGitInstance(path);
        }

        static (bool success, string output, string error) RunGitCommand(string gitPath, string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = gitPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

#if UNITY_EDITOR_OSX
                AugmentPath(startInfo);
#endif

                using var process = Process.Start(startInfo);
                if (process == null)
                    return (false, null, "Failed to start process");

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();

                if (!process.WaitForExit(k_ValidationTimeoutMs))
                {
                    try { process.Kill(); }
                    catch { }
                    return (false, null, "Command timed out");
                }

                return (process.ExitCode == 0, output.Trim(), error.Trim());
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        static string ParseGitVersion(string output)
        {
            if (string.IsNullOrEmpty(output))
            {
                return null;
            }

            var match = Regex.Match(output, @"git version (\d+\.\d+\.\d+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        static string ParseLfsVersion(string output)
        {
            if (string.IsNullOrEmpty(output))
            {
                return null;
            }

            var match = Regex.Match(output, @"git-lfs/(\d+\.\d+\.\d+)");
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}
