using System.ComponentModel;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Unity.AI.Assistant.DeveloperTools")]
[assembly: InternalsVisibleTo("Unity.AI.Development.Search")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.Tests")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.Tests.UITestFramework")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.Tests.E2E")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.Automation.Editor")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.Benchmark.Tests")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.CodeLibrary.Editor")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.API.Editor")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.Tools.Editor")]

[assembly: InternalsVisibleTo("Unity.AI.Assistant.AssetGenerators.Editor")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.GameDataCollection.Editor")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.Annotations.Editor")]

namespace System.Runtime.CompilerServices
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    class IsExternalInit { }
}