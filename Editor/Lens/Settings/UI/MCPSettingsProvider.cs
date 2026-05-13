using UnityEditor;
using UnityEngine.UIElements;
using Becool.UnityMcpLens.Editor.Settings.UI;

namespace Becool.UnityMcpLens.Editor.Settings
{
    class MCPSettingsProvider : SettingsProvider
    {
        public MCPSettingsProvider(string path, SettingsScope scope = SettingsScope.Project)
            : base(path, scope) { }

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new MCPSettingsProvider(MCPConstants.projectSettingsPath);
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            rootElement.Clear();
            rootElement.AddToClassList("umcp-redirect-root");

            var title = new Label("Unity MCP Lens");
            title.AddToClassList("umcp-redirect-title");
            title.style.fontSize = 18;
            title.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            title.style.marginBottom = 8;
            rootElement.Add(title);

            var body = new Label("Lens settings, bridge status, server refresh, and diagnostics now live in the external Lens Command Center.");
            body.AddToClassList("umcp-redirect-body");
            body.style.whiteSpace = WhiteSpace.Normal;
            body.style.marginBottom = 8;
            rootElement.Add(body);

            var path = new Label($"Settings file: {MCPConstants.ProjectSettingsJsonPath}");
            path.AddToClassList("umcp-redirect-path");
            path.style.opacity = 0.65f;
            path.style.marginBottom = 12;
            rootElement.Add(path);

            var button = new Button(CommandCenterLauncher.Open)
            {
                text = "Open Command Center"
            };
            button.AddToClassList("umcp-primary-button");
            rootElement.Add(button);
        }
    }
}
