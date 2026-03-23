using System.ComponentModel;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Unity.AI.Assistant.UI.Editor")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.DeveloperTools")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.Tests")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.Tests.UITestFramework")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.Tests.E2E")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.Automation.Editor")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.Benchmark.Tests")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.CodeLibrary.Editor")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.API.Editor")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.Tools.Editor")]

[assembly: InternalsVisibleTo("Unity.AI.Agents.Shared.Tests")]
[assembly: InternalsVisibleTo("Unity.AI.Agents.Profiler.Editor")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.Integrations.Profiler.Editor")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.Integrations.Sample.Editor")]

[assembly: InternalsVisibleTo("Unity.AI.Assistant.AssetGenerators.Editor")]
[assembly: InternalsVisibleTo("Unity.AI.Search.Editor")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.GameDataCollection.Editor")]
[assembly: InternalsVisibleTo("Unity.AI.MCP.Editor")]
[assembly: InternalsVisibleTo("Unity.AI.Assistant.Annotations.Editor")]

// Required for advanced mocking with Moq
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

// We need to add this to make the record type work in Unity with the init keyword
// The type System.Runtime.CompilerServices.IsExternalInit is defined in .NET 5 and later, which Unity does not support yet
namespace System.Runtime.CompilerServices
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    class IsExternalInit { }
}