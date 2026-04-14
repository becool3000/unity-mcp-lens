using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Editor;
using Unity.AI.MCP.Editor.Settings;
using Unity.AI.MCP.Editor.Settings.Utilities;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.MCP.Editor.Helpers
{
    /// <summary>
    /// Copies the relay binary from the package's RelayApp~ directory to ~/.unity/relay/
    /// so that MCP clients can reference a stable, well-known executable location.
    /// Runs automatically at editor startup and only updates when the relay version changes.
    /// Version is tracked via relay.json (emitted by the relay build, copied alongside binaries).
    /// </summary>
    [InitializeOnLoad]
    static class ServerInstaller
    {
        const string k_RelayMetadataFileName = "relay.json";
        const string k_LensMetadataFileName = "unity-mcp-lens.json";

        static ServerInstaller()
        {
            RefreshInstalledServers();
        }

        public static void RefreshInstalledServers()
        {
            if (AssistantRelayProjectPreferences.LegacyRelayEnabled)
            {
                InstallOrUpdateRelay();
            }
            else
            {
                McpLog.Log("Skipping legacy relay install because this project is configured for MCP-only / unity-mcp-lens.");
            }

            InstallOrUpdateOwnedMcpServer();
        }

        static void InstallOrUpdateRelay()
        {
            try
            {
                string sourceDir = Path.GetFullPath(MCPConstants.relayAppPath);
                if (!Directory.Exists(sourceDir))
                {
                    McpLog.Warning($"Relay app directory not found at {sourceDir}");
                    return;
                }

                string targetDir = MCPConstants.RelayBaseDirectory;
                string bundledVersion = ReadVersionFromMetadata(Path.Combine(sourceDir, k_RelayMetadataFileName));
                string installedVersion = ReadVersionFromMetadata(Path.Combine(targetDir, k_RelayMetadataFileName));

                if (!IsNewerVersion(bundledVersion, installedVersion))
                {
                    McpLog.Log($"Relay is up to date (bundled: {bundledVersion}, installed: {installedVersion})");
                    return;
                }

                if (!Directory.Exists(targetDir))
                    Directory.CreateDirectory(targetDir);

                CopyRelayFiles(sourceDir, targetDir);

                McpLog.Log($"Relay installed to {targetDir} (version {bundledVersion})");
            }
            catch (Exception ex)
            {
                McpLog.Warning($"Could not install relay: {ex.Message}");
            }
        }

        static string ReadVersionFromMetadata(string metadataPath)
        {
            try
            {
                if (!File.Exists(metadataPath))
                    return "0.0.0";

                string json = File.ReadAllText(metadataPath);
                var jsonObj = JObject.Parse(json);
                return jsonObj["version"]?.ToString() ?? "0.0.0";
            }
            catch
            {
                return "0.0.0";
            }
        }

        static bool IsNewerVersion(string packageVersion, string installedVersion)
        {
            try
            {
                var pkgBase = new Version(CleanVersion(packageVersion));
                var instBase = new Version(CleanVersion(installedVersion));

                int cmp = pkgBase.CompareTo(instBase);
                if (cmp != 0)
                    return cmp > 0;

                // Base versions equal — compare build numbers from pre-release tag
                return ExtractBuildNumber(packageVersion) > ExtractBuildNumber(installedVersion);
            }
            catch
            {
                return true;
            }
        }

        static void InstallOrUpdateOwnedMcpServer()
        {
            try
            {
                string sourceDir = Path.GetFullPath(MCPConstants.unityMcpLensAppPath);
                if (!Directory.Exists(sourceDir))
                {
                    McpLog.Warning($"Unity MCP Lens source directory not found at {sourceDir}");
                    return;
                }

                string bundledVersion = ReadVersionFromMetadata(MCPConstants.BundledLensMetadataFile);
                string installedVersion = ReadVersionFromMetadata(MCPConstants.LensInstalledMetadataFile);

                if (!IsNewerVersion(bundledVersion, installedVersion) && File.Exists(MCPConstants.LensInstalledServerMainFile))
                {
                    McpLog.Log($"Unity MCP Lens server is up to date (bundled: {bundledVersion}, installed: {installedVersion})");
                    return;
                }

                if (!Directory.Exists(MCPConstants.UnityMcpBaseDirectory))
                    Directory.CreateDirectory(MCPConstants.UnityMcpBaseDirectory);

                string stagingDirectory = Path.Combine(Path.GetTempPath(), $"unity-mcp-lens-{Guid.NewGuid():N}");
                try
                {
                    PublishOwnedServer(stagingDirectory);
                    CopyDirectoryContents(stagingDirectory, MCPConstants.UnityMcpBaseDirectory);
                    ReconcileOwnedServerBinary(MCPConstants.UnityMcpBaseDirectory);
                    File.Copy(MCPConstants.BundledLensMetadataFile, MCPConstants.LensInstalledMetadataFile, true);

                    if (!PlatformUtils.IsWindows)
                        SetExecutable(MCPConstants.LensInstalledServerMainFile);

                    McpLog.Log($"Unity MCP Lens server installed to {MCPConstants.UnityMcpBaseDirectory} (version {bundledVersion})");
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(stagingDirectory))
                            Directory.Delete(stagingDirectory, true);
                    }
                    catch
                    {
                        // Best-effort cleanup only.
                    }
                }
            }
            catch (Exception ex)
            {
                McpLog.Warning($"Could not install Unity MCP Lens server: {ex.Message}");
            }
        }

        static int ExtractBuildNumber(string version)
        {
            // Parse "X.Y.Z-build.N" → N, or 0 if no tag
            int dashIndex = version.IndexOf('-');
            if (dashIndex < 0) return 0;

            string tag = version.Substring(dashIndex + 1);
            int lastDot = tag.LastIndexOf('.');
            if (lastDot >= 0 && int.TryParse(tag.Substring(lastDot + 1), out int n))
                return n;

            return 0;
        }

        static string CleanVersion(string version)
        {
            int dashIndex = version.IndexOf('-');
            return dashIndex >= 0 ? version.Substring(0, dashIndex) : version;
        }

        static void CopyRelayFiles(string sourceDir, string targetDir)
        {
            bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            bool isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

            foreach (string filePath in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(filePath);

                if (fileName == ".DS_Store")
                    continue;

                if (fileName == k_RelayMetadataFileName)
                {
                    CopyFile(filePath, targetDir, fileName);
                    continue;
                }

                if (fileName == "relay_win.exe" && isWindows)
                {
                    CopyFile(filePath, targetDir, fileName);
                    continue;
                }

                if (fileName == "relay_linux" && isLinux)
                {
                    CopyFile(filePath, targetDir, fileName);
                    SetExecutable(Path.Combine(targetDir, fileName));
                    continue;
                }
            }

            if (isMac)
            {
                foreach (string dirPath in Directory.GetDirectories(sourceDir))
                {
                    string dirName = Path.GetFileName(dirPath);
                    if (!dirName.StartsWith("relay_mac_", StringComparison.Ordinal) || !dirName.EndsWith(".app", StringComparison.Ordinal))
                        continue;

                    string targetAppPath = Path.Combine(targetDir, dirName);

                    if (Directory.Exists(targetAppPath))
                        Directory.Delete(targetAppPath, true);

                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "ditto",
                        Arguments = $"\"{dirPath}\" \"{targetAppPath}\"",
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = System.Diagnostics.Process.Start(startInfo);
                    string errorOutput = process?.StandardError.ReadToEnd();
                    process?.WaitForExit();
                    if (process == null || process.ExitCode != 0)
                        throw new Exception($"Failed to copy macOS app bundle via ditto: {errorOutput}");
                }
            }
        }

        static void CopyFile(string sourcePath, string targetDir, string fileName)
        {
            string targetPath = Path.Combine(targetDir, fileName);
            File.Copy(sourcePath, targetPath, true);
        }

        static void SetExecutable(string filePath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = System.Diagnostics.Process.Start(startInfo);
                process?.WaitForExit(5000);
            }
            catch
            {
                // chmod not available on this platform
            }
        }

        static void PublishOwnedServer(string stagingDirectory)
        {
            string runtimeIdentifier = GetCurrentRuntimeIdentifier();
            string prebuiltDirectory = Path.Combine(Path.GetFullPath(MCPConstants.unityMcpLensAppPath), "prebuilt", runtimeIdentifier);
            if (Directory.Exists(prebuiltDirectory))
            {
                CopyDirectoryContents(prebuiltDirectory, stagingDirectory);
                ReconcileOwnedServerBinary(stagingDirectory);
                return;
            }

            string projectFile = MCPConstants.BundledLensProjectFile;
            if (!File.Exists(projectFile))
                throw new FileNotFoundException("Unity MCP Lens project file not found.", projectFile);

            string dotnetExecutable = ResolveDotNetExecutable();
            if (string.IsNullOrWhiteSpace(dotnetExecutable))
                throw new InvalidOperationException("dotnet SDK/runtime executable was not found. Install .NET SDK 8+ or bundle a prebuilt unity-mcp-lens binary.");

            Directory.CreateDirectory(stagingDirectory);
            string arguments =
                $"publish \"{projectFile}\" -c Release -r {runtimeIdentifier} --self-contained true /p:PublishSingleFile=true /p:DebugType=None /p:DebugSymbols=false -o \"{stagingDirectory}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = dotnetExecutable,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(projectFile)
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                throw new InvalidOperationException("Failed to start dotnet publish for Unity MCP Lens server.");

            string standardOutput = process.StandardOutput.ReadToEnd();
            string standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"dotnet publish failed for Unity MCP Lens server (exit {process.ExitCode}).\n{standardOutput}\n{standardError}".Trim());
            }

            ReconcileOwnedServerBinary(stagingDirectory);
        }

        static string ResolveDotNetExecutable()
        {
            string bundledPath = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrWhiteSpace(bundledPath))
            {
                string candidate = Path.Combine(bundledPath, PlatformUtils.IsWindows ? "dotnet.exe" : "dotnet");
                if (File.Exists(candidate))
                    return candidate;
            }

            return PlatformUtils.IsWindows ? "dotnet.exe" : "dotnet";
        }

        static string GetCurrentRuntimeIdentifier()
        {
            if (PlatformUtils.IsWindows)
            {
                return RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.Arm64 => "win-arm64",
                    Architecture.X86 => "win-x86",
                    _ => "win-x64"
                };
            }

            if (PlatformUtils.IsMacOS)
            {
                return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
            }

            if (PlatformUtils.IsLinux)
            {
                return RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.Arm64 => "linux-arm64",
                    Architecture.X86 => "linux-x86",
                    _ => "linux-x64"
                };
            }

            throw new PlatformNotSupportedException("Unsupported platform for Unity MCP Lens server installation.");
        }

        static void ReconcileOwnedServerBinary(string outputDirectory)
        {
            string expectedPath = MCPConstants.LensInstalledServerMainFile;
            string expectedFileName = Path.GetFileName(expectedPath);
            string installedExpectedPath = Path.Combine(outputDirectory, expectedFileName);

            string publishedDefaultPath = Path.Combine(outputDirectory, GetPublishedDefaultServerBinaryName());
            if (!File.Exists(publishedDefaultPath))
                return;

            if (!File.Exists(installedExpectedPath) ||
                File.GetLastWriteTimeUtc(publishedDefaultPath) >= File.GetLastWriteTimeUtc(installedExpectedPath))
            {
                File.Copy(publishedDefaultPath, installedExpectedPath, true);
            }

            if (!string.Equals(publishedDefaultPath, installedExpectedPath, StringComparison.OrdinalIgnoreCase))
                File.Delete(publishedDefaultPath);
        }

        static string GetPublishedDefaultServerBinaryName()
        {
            return PlatformUtils.IsWindows ? "UnityMcpLens.exe" : "UnityMcpLens";
        }

        static void CopyDirectoryContents(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (string directory in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(sourceDir, directory);
                Directory.CreateDirectory(Path.Combine(targetDir, relativePath));
            }

            foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(sourceDir, file);
                string destination = Path.Combine(targetDir, relativePath);
                string destinationDirectory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(destinationDirectory))
                    Directory.CreateDirectory(destinationDirectory);
                File.Copy(file, destination, true);
            }
        }
    }
}
