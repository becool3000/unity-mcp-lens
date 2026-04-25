#nullable disable
using Becool.UnityMcpLens.Editor.Services;

namespace Becool.UnityMcpLens.Editor.Services.GameObjects
{
    sealed class GameObjectToolTiming : ToolOperationTiming
    {
        public GameObjectToolTiming(string toolName, string action, int requestBytes)
            : base(toolName, action, requestBytes)
        {
        }
    }
}
