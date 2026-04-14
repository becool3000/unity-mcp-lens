using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.AI.MCP.Editor.Settings.Utilities;
using UnityEngine;

namespace Unity.AI.MCP.Editor.Settings
{
    /// <summary>
    /// Contains constants used throughout the Unity MCP package for configuration,
    /// file paths, and system integration.
    /// </summary>
    static class MCPConstants
    {
        // EditorPrefs Keys
        /// <summary>
        /// EditorPrefs key for storing MCP project settings.
        /// </summary>
        public const string prefProjectSettings = "Unity.AI.MCP.ProjectSettings.v2";

        // Unity Paths and Namespaces
        /// <summary>
        /// Path to MCP server settings in Unity's Project Settings window.
        /// </summary>
        public const string projectSettingsPath = "Project/AI/Unity MCP";

        // Package Configuration
        /// <summary>
        /// The Unity package name for MCP integration.
        /// </summary>
        public static string packageName = "com.unity.ai.assistant";

        public static string moduleName = "Unity.AI.MCP.Editor";

        /// <summary>
        /// Path to the package's Editor directory.
        /// </summary>
        public static string modulePath = $"Packages/{packageName}/Modules/{moduleName}";

        /// <summary>
        /// Path to the relay application directory (contains compiled binaries).
        /// </summary>
        public static string relayAppPath = $"Packages/{packageName}/RelayApp~";

        /// <summary>
        /// Path to the owned MCP-only server source/bundled assets directory.
        /// </summary>
        public static string unityMcpServerAppPath = $"Packages/{packageName}/UnityMcpServerApp~";

        /// <summary>
        /// Path to the UI template files for settings.
        /// </summary>
        public static string uiTemplatesPath = $"{modulePath}/Settings/UI";

        // Client Configuration
        /// <summary>
        /// JSON key used to identify Unity MCP server in MCP client configuration files.
        /// </summary>
        public static string jsonKeyIntegration = "unity-mcp";
        public static string jsonKeyIntegrationLegacy = "unity-mcp-legacy";
        public static string jsonKeyIntegrationVNext = "unity-mcp-vnext";

        // Relay Installation
        /// <summary>
        /// Name of the relay installation directory relative to the user's home directory.
        /// The relay binary is copied here so MCP clients can reference a stable location.
        /// </summary>
        public static string relayBaseDirectoryName = ".unity/relay";
        public static string unityMcpBaseDirectoryName = ".unity/unity-mcp";

        /// <summary>
        /// Gets the relay installation directory (~/.unity/relay).
        /// </summary>
        public static string RelayBaseDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), relayBaseDirectoryName);

        public static string UnityMcpBaseDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), unityMcpBaseDirectoryName);

        // Status File Configuration
        /// <summary>
        /// Name of the base MCP directory relative to the user's home directory.
        /// </summary>
        public static string mcpBaseDirectoryName = ".unity/mcp";

        /// <summary>
        /// Subdirectory within the MCP base directory for connection status files.
        /// </summary>
        public static string connectionsSubdirectory = "connections";

        /// <summary>
        /// File pattern for locating bridge status JSON files.
        /// </summary>
        public static string statusFilePattern = "bridge-status-*.json";

        /// <summary>
        /// Environment variable name for overriding the status directory location.
        /// </summary>
        public static string statusDirEnvVar = "UNITY_MCP_STATUS_DIR";

        /// <summary>
        /// Gets the base MCP directory (~/.unity/mcp)
        /// </summary>
        public static string McpBaseDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), mcpBaseDirectoryName);

        /// <summary>
        /// Gets the full path to the connections directory where bridge status files are stored.
        /// Can be overridden via UNITY_MCP_STATUS_DIR environment variable.
        /// </summary>
        public static string StatusDirectory
        {
            get
            {
                string dir = Environment.GetEnvironmentVariable(statusDirEnvVar);
                if (string.IsNullOrWhiteSpace(dir))
                {
                    dir = Path.Combine(McpBaseDirectory, connectionsSubdirectory);
                }
                return dir;
            }
        }

        /// <summary>
        /// Gets the path to the relay binary installed at ~/.unity/relay.
        /// MCP client configurations reference this stable location.
        /// </summary>
        public static string InstalledServerMainFile
        {
            get
            {
                string relayPath = RelayBaseDirectory;

                if (PlatformUtils.IsWindows)
                    return Path.Combine(relayPath, "relay_win.exe");
                if (PlatformUtils.IsLinux)
                    return Path.Combine(relayPath, "relay_linux");
                if (PlatformUtils.IsMacOS)
                {
                    string arch = RuntimeInformation.ProcessArchitecture ==
                        Architecture.Arm64 ? "arm64" : "x64";
                    return Path.Combine(relayPath, $"relay_mac_{arch}.app/Contents/MacOS/relay_mac_{arch}");
                }

                return Path.Combine(relayPath, "relay_linux");
            }
        }

        public static string VNextMetadataFileName => "unity-mcp-server.json";

        public static string VNextInstalledMetadataFile => Path.Combine(UnityMcpBaseDirectory, VNextMetadataFileName);

        public static string VNextInstalledServerMainFile
        {
            get
            {
                string serverPath = UnityMcpBaseDirectory;

                if (PlatformUtils.IsWindows)
                    return Path.Combine(serverPath, "unity_mcp_server_win.exe");
                if (PlatformUtils.IsLinux)
                    return Path.Combine(serverPath, "unity_mcp_server_linux");
                if (PlatformUtils.IsMacOS)
                {
                    string arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
                    return Path.Combine(serverPath, $"unity_mcp_server_mac_{arch}");
                }

                return Path.Combine(serverPath, "unity_mcp_server_linux");
            }
        }

        /// <summary>
        /// Gets the path to the relay binary bundled with the package (source for installation).
        /// </summary>
        internal static string BundledRelayMainFile
        {
            get
            {
                string relayPath = Path.GetFullPath(relayAppPath);

                if (PlatformUtils.IsWindows)
                    return Path.Combine(relayPath, "relay_win.exe");
                if (PlatformUtils.IsLinux)
                    return Path.Combine(relayPath, "relay_linux");
                if (PlatformUtils.IsMacOS)
                {
                    string arch = RuntimeInformation.ProcessArchitecture ==
                        Architecture.Arm64 ? "arm64" : "x64";
                    return Path.Combine(relayPath, $"relay_mac_{arch}.app/Contents/MacOS/relay_mac_{arch}");
                }

                return Path.Combine(relayPath, "relay_linux");
            }
        }

        internal static string BundledVNextProjectFile =>
            Path.Combine(Path.GetFullPath(unityMcpServerAppPath), "src", "UnityMcpServer", "UnityMcpServer.csproj");

        internal static string BundledVNextMetadataFile =>
            Path.Combine(Path.GetFullPath(unityMcpServerAppPath), VNextMetadataFileName);


        /// <summary>
        /// Gets all bridge status files in the status directory, ordered by most recently modified.
        /// </summary>
        public static string[] StatusFiles =>
            Directory.GetFiles(StatusDirectory, statusFilePattern)
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToArray();

        /// <summary>
        /// Gets the path to the heartbeat file for the current Unity project.
        /// </summary>
        public static string HeartbeatFilePath =>
            Path.Combine(StatusDirectory, $"bridge-status-{ComputeProjectHash(Application.dataPath)}.json");

        /// <summary>
        /// Gets the path to the port registry file for the current Unity project.
        /// </summary>
        public static string PortRegistryFilePath =>
            Path.Combine(StatusDirectory, $"bridge-port-{ComputeProjectHash(Application.dataPath)}.json");

        /// <summary>
        /// Computes a stable hash for a project path.
        /// </summary>
        /// <param name="projectPath">The project path to hash.</param>
        /// <returns>A 16-character lowercase hexadecimal hash string.</returns>
        static string ComputeProjectHash(string projectPath)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(projectPath);
                var hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 16).ToLowerInvariant();
            }
        }
    }
}
