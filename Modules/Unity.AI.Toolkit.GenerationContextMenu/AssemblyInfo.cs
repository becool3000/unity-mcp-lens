using System.ComponentModel;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Unity.AI.Toolkit.Tests")]
[assembly: InternalsVisibleTo("Unity.AI.Pbr")]
[assembly: InternalsVisibleTo("Unity.AI.Sound")]
[assembly: InternalsVisibleTo("Unity.AI.Animate")]
[assembly: InternalsVisibleTo("Unity.AI.Mesh")]
[assembly: InternalsVisibleTo("Unity.AI.Image")]

namespace System.Runtime.CompilerServices
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    class IsExternalInit { }
}
