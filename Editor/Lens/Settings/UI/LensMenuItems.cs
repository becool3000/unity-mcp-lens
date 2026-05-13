using Becool.UnityMcpLens.Editor.Settings;
using UnityEditor;

namespace Becool.UnityMcpLens.Editor.Settings.UI
{
    static class LensMenuItems
    {
        const string k_MenuRoot = "Tools/Unity MCP Lens/";

        [MenuItem(k_MenuRoot + "Open Command Center", false, 1000)]
        static void OpenCommandCenter()
        {
            CommandCenterLauncher.Open();
        }
    }
}
