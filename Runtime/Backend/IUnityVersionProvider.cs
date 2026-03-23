using System.Collections.Generic;

namespace Unity.AI.Assistant.Backend
{
    interface IUnityVersionProvider
    {
        IReadOnlyList<string> Version { get; }
    }
}
