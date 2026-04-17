using System.IO;
using Becool.UnityMcpLens.Editor;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Settings;
using UnityEditor;

namespace Becool.UnityMcpLens.Editor.Settings.UI
{
    static class LensMenuItems
    {
        const string k_MenuRoot = "Tools/Unity MCP Lens/";

        [MenuItem(k_MenuRoot + "Open Settings", false, 1000)]
        static void OpenSettings()
        {
            SettingsService.OpenProjectSettings(MCPConstants.projectSettingsPath);
        }

        [MenuItem(k_MenuRoot + "Start Bridge", false, 1010)]
        static void StartBridge()
        {
            UnityMCPBridge.Start();
            EditorPrefs.SetBool("MCPBridge.ExplicitlyStopped", false);
        }

        [MenuItem(k_MenuRoot + "Start Bridge", true)]
        static bool CanStartBridge() => !UnityMCPBridge.IsRunning;

        [MenuItem(k_MenuRoot + "Stop Bridge", false, 1011)]
        static void StopBridge()
        {
            UnityMCPBridge.Stop();
            EditorPrefs.SetBool("MCPBridge.ExplicitlyStopped", true);
        }

        [MenuItem(k_MenuRoot + "Stop Bridge", true)]
        static bool CanStopBridge() => UnityMCPBridge.IsRunning;

        [MenuItem(k_MenuRoot + "Install/Refresh Lens Server", false, 1020)]
        static void InstallOrRefreshLensServer()
        {
            ServerInstaller.RefreshInstalledServers();
        }

        [MenuItem(k_MenuRoot + "Open Server Folder", false, 1030)]
        static void OpenServerFolder()
        {
            RevealDirectory(MCPConstants.UnityMcpBaseDirectory);
        }

        [MenuItem(k_MenuRoot + "Open Status Folder", false, 1031)]
        static void OpenStatusFolder()
        {
            RevealDirectory(MCPConstants.StatusDirectory);
        }

        static void RevealDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                return;

            Directory.CreateDirectory(directory);
            EditorUtility.RevealInFinder(directory);
        }
    }
}
