using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Unity.AI.MCP.Editor.Settings;
using UnityEngine;

namespace Unity.AI.MCP.Editor.Helpers
{
    /// <summary>
    /// Manages connection discovery files for Unity MCP Bridge.
    /// Replaces TCP port-based discovery with named pipe/Unix socket path discovery.
    /// </summary>
    static class ServerDiscovery
    {
        /// <summary>
        /// Contains information about the MCP Bridge connection for discovery by MCP servers.
        /// </summary>
        [Serializable]
        class ConnectionInfo
        {
            /// <summary>
            /// Type of connection used ("named_pipe" or "unix_socket").
            /// </summary>
            public string connection_type; // "named_pipe" or "unix_socket"

            /// <summary>
            /// Full path to the named pipe or Unix socket.
            /// </summary>
            public string connection_path; // Full path to pipe or socket

            /// <summary>
            /// ISO 8601 timestamp of when the connection was created.
            /// </summary>
            public string created_date;

            /// <summary>
            /// Full path to the Unity project's Assets folder.
            /// </summary>
            public string project_path;

            /// <summary>
            /// Version of the MCP protocol being used.
            /// </summary>
            public string protocol_version;
        }

        /// <summary>
        /// Get connection path for the current Unity project
        /// </summary>
        /// <returns>Platform-specific connection path</returns>
        public static string GetConnectionPath()
        {
            string projectHash = ComputeProjectHash(Application.dataPath);
            return GenerateConnectionPath(projectHash);
        }

        /// <summary>
        /// Generate platform-specific connection path from project hash
        /// NOTE: Using named pipes for all platforms due to Unity's .NET runtime limitations
        /// </summary>
        static string GenerateConnectionPath(string projectHash)
        {
            if (IsWindows())
            {
                // Windows: Named pipe
                // Format: \\.\pipe\unity-mcp-{hash}
                return $"\\\\.\\pipe\\unity-mcp-{projectHash}";
            }
            else
            {
                // Mac/Linux: Named pipe path (cross-platform named pipes in .NET)
                // Format: /tmp/unity-mcp-{hash}
                return $"/tmp/unity-mcp-{projectHash}";
            }
        }

        /// <summary>
        /// Save connection info to discovery file
        /// </summary>
        /// <param name="connectionPath">Platform-specific connection path to the named pipe or Unix socket.</param>
        public static void SaveConnectionInfo(string connectionPath)
        {
            try
            {
                var connectionInfo = new ConnectionInfo
                {
                    connection_type = "named_pipe", // Using named pipes for all platforms
                    connection_path = connectionPath,
                    created_date = DateTime.UtcNow.ToString("O"),
                    project_path = Application.dataPath,
                    protocol_version = "2.0"
                };

                string registryDir = GetRegistryDirectory();
                Directory.CreateDirectory(registryDir);

                string registryFile = GetRegistryFilePath();
                string json = JsonConvert.SerializeObject(connectionInfo, Formatting.Indented);
                File.WriteAllText(registryFile, json, new UTF8Encoding(false));

                #if UNITY_EDITOR_LINUX
                // Ubuntu CI: Ensure file is flushed to disk before MCP server tries to read it
                // File system operations can be slower on CI environments
                try
                {
                    using (var fs = new FileStream(registryFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        fs.Flush(true); // Flush OS buffers to disk
                    }
                    McpLog.Log("[Ubuntu] Flushed registry file to disk");
                }
                catch (Exception flushEx)
                {
                    McpLog.Warning($"Could not flush registry file: {flushEx.Message}");
                }
                #endif

                McpLog.Log($"Saved connection info to {registryFile}");
            }
            catch (Exception ex)
            {
                McpLog.Warning($"Could not save connection info: {ex.Message}");
            }
        }

        /// <summary>
        /// Save heartbeat/status file (for quick discovery)
        /// </summary>
        /// <param name="connectionPath">Platform-specific connection path to the named pipe or Unix socket.</param>
        /// <param name="status">Current status of the connection (default is "ready").</param>
        public static void SaveStatusFile(
            string connectionPath,
            string status = "ready",
            string reason = null,
            bool expectedRecovery = false,
            string toolDiscoveryMode = null,
            int toolCount = -1,
            string toolsHash = null,
            string toolDiscoveryReason = null,
            string toolSnapshotUtc = null)
        {
            try
            {
                string statusFile = GetStatusFilePath();
                var statusInfo = new
                {
                    connection_type = "named_pipe", // Using named pipes for all platforms
                    connection_path = connectionPath,
                    status = status,
                    reason = reason,
                    expected_recovery = expectedRecovery,
                    tool_discovery_mode = toolDiscoveryMode,
                    tool_count = toolCount,
                    tools_hash = toolsHash,
                    tool_discovery_reason = toolDiscoveryReason,
                    tool_snapshot_utc = toolSnapshotUtc,
                    project_path = Application.dataPath,
                    last_heartbeat = DateTime.UtcNow.ToString("O"),
                    protocol_version = "2.0"
                };

                string json = JsonConvert.SerializeObject(statusInfo, Formatting.Indented);
                Directory.CreateDirectory(GetRegistryDirectory());
                File.WriteAllText(statusFile, json, new UTF8Encoding(false));

                #if UNITY_EDITOR_LINUX
                // Ubuntu CI: Ensure status file is flushed to disk
                try
                {
                    using (var fs = new FileStream(statusFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        fs.Flush(true); // Flush OS buffers to disk
                    }
                }
                catch
                {
                    // Ignore flush errors in heartbeat
                }
                #endif
            }
            catch
            {
                // Heartbeat failures should not crash the bridge
            }
        }

        /// <summary>
        /// Delete discovery files on shutdown
        /// </summary>
        public static void DeleteDiscoveryFiles()
        {
            try
            {
                string registryFile = GetRegistryFilePath();
                if (File.Exists(registryFile))
                    File.Delete(registryFile);

                string statusFile = GetStatusFilePath();
                if (File.Exists(statusFile))
                    File.Delete(statusFile);

                McpLog.Log("Deleted discovery files");
            }
            catch (Exception ex)
            {
                McpLog.Warning($"Could not delete discovery files: {ex.Message}");
            }
        }

        static string GetRegistryDirectory()
        {
            return MCPConstants.StatusDirectory;
        }

        static string GetRegistryFilePath()
        {
            string dir = GetRegistryDirectory();
            string hash = ComputeProjectHash(Application.dataPath);
            string fileName = $"bridge-{hash}.json";
            return Path.Combine(dir, fileName);
        }

        static string GetStatusFilePath()
        {
            string dir = GetRegistryDirectory();
            string hash = ComputeProjectHash(Application.dataPath);
            string fileName = $"bridge-status-{hash}.json";
            return Path.Combine(dir, fileName);
        }

        static string ComputeProjectHash(string input)
        {
            try
            {
                using (SHA1 sha1 = SHA1.Create())
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
                    byte[] hashBytes = sha1.ComputeHash(bytes);
                    var sb = new StringBuilder();
                    foreach (byte b in hashBytes)
                    {
                        sb.Append(b.ToString("x2"));
                    }
                    return sb.ToString().Substring(0, 8); // short, sufficient for uniqueness
                }
            }
            catch
            {
                return "default";
            }
        }

        static bool IsWindows()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }
    }
}
