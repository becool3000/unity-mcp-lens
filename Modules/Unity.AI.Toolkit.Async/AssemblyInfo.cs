using System.ComponentModel;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Unity.AI.Toolkit.Accounts")]
[assembly: InternalsVisibleTo("Unity.AI.Toolkit.Asset")]
[assembly: InternalsVisibleTo("Unity.AI.Toolkit.GenerationContextMenu")]
[assembly: InternalsVisibleTo("Unity.AI.Toolkit.GenerationObjectPicker")]
[assembly: InternalsVisibleTo("Unity.AI.Generators.Asset")]
[assembly: InternalsVisibleTo("Unity.AI.Generators.IO")]
[assembly: InternalsVisibleTo("Unity.AI.Generators.Redux")]
[assembly: InternalsVisibleTo("Unity.AI.Generators.Sdk")]
[assembly: InternalsVisibleTo("Unity.AI.Generators.UI")]
[assembly: InternalsVisibleTo("Unity.AI.ModelSelector")]
[assembly: InternalsVisibleTo("Unity.AI.Pbr")]
[assembly: InternalsVisibleTo("Unity.AI.Sound")]
[assembly: InternalsVisibleTo("Unity.AI.Animate")]
[assembly: InternalsVisibleTo("Unity.AI.Mesh")]
[assembly: InternalsVisibleTo("Unity.AI.Image")]
[assembly: InternalsVisibleTo("Unity.AI.Search.Editor")]

// We need to add this to make the record type work in Unity with the init keyword
// The type System.Runtime.CompilerServices.IsExternalInit is defined in .NET 5 and later, which Unity does not support yet
namespace System.Runtime.CompilerServices
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    class IsExternalInit { }
}
