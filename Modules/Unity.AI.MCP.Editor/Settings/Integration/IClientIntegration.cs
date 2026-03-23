using Unity.AI.MCP.Editor.Models;

namespace Unity.AI.MCP.Editor.Settings.Integration
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
