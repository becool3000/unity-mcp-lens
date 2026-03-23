using System;

namespace Unity.AI.Generators.UI.Utilities
{
    interface IInterruptedDownloadBase
    {
        int progressTaskId { get; }
        string uniqueTaskId { get; }
    }

    interface IInterruptedDownloadData : IInterruptedDownloadBase
    {
        ImmutableStringList jobIds { get; set; }
    }
}
