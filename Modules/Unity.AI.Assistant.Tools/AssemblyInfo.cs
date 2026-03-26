using System.ComponentModel;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Unity.AI.Assistant.Tests")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.Integrations.Profiler.Editor")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.Integrations.Sample.Editor")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.UI.Editor")]
[assembly: InternalsVisibleTo("Unity.AI.MCP.Editor")]

namespace System.Runtime.CompilerServices
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    class IsExternalInit { }
}
