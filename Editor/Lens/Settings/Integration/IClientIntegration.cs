using Becool.UnityMcpLens.Editor.Models;

namespace Becool.UnityMcpLens.Editor.Settings.Integration
{
    interface IClientIntegration
    {
        McpClient Client { get; }

        bool Configure();
        bool Disable();

        void CheckConfiguration();

        bool HasMissingDependencies(out string warningText, out string helpUrl);
    }
}
