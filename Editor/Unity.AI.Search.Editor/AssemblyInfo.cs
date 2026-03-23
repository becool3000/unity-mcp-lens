using System.ComponentModel;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Unity.AI.Search.Tests")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.Tests")]
[assembly: InternalsVisibleTo("Unity.AI.Development.Search")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.Tools.Editor")]

// We need to add this to make the record type work in Unity with the init keyword
// The type System.Runtime.CompilerServices.IsExternalInit is defined in .NET 5 and later, which Unity does not support yet
namespace System.Runtime.CompilerServices
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    class IsExternalInit { }
}
