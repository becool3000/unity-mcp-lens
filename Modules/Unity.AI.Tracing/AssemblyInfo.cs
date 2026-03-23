using System.ComponentModel;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Unity.AI.Assistant.Runtime")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.Editor")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.UI.Editor")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.DeveloperTools")]
[assembly: InternalsVisibleTo("Unity.AI.MCP.Editor")]

namespace System.Runtime.CompilerServices
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    class IsExternalInit { }
}
