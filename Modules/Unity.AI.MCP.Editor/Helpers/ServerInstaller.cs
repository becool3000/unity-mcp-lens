using System;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using Unity.AI.MCP.Editor.Settings;
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

        static ServerInstaller()
        {
            InstallOrUpdateRelay();
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
                string bundledVersion = ReadRelayVersion(Path.Combine(sourceDir, k_RelayMetadataFileName));
                string installedVersion = ReadRelayVersion(Path.Combine(targetDir, k_RelayMetadataFileName));

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

        static string ReadRelayVersion(string relayJsonPath)
        {
            try
            {
                if (!File.Exists(relayJsonPath))
                    return "0.0.0";

                string json = File.ReadAllText(relayJsonPath);
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
                var startInfo = new System.Diagnostics.ProcessStartInfo
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
    }
}
