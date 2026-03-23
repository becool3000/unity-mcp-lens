using System;
using System.Threading;

namespace Unity.AI.Generators.Redux.Toolkit
{
    /// <summary>
    /// Additional query options
    /// </summary>
    /// <param name="refetchOnFocus"></param>
    /// <param name="pollingInterval"></param>
    /// <param name="startOnAwait">
    /// Start the query only if something actually `await` for it.
    /// This provide more predictability and control over when exactly a query operation starts after being created.
    ///
    /// For instance, this allows a client to guarantee always receiving every result state from the start
    /// of the whole operation lifecycle (uninitialized through fulfilled...).
    ///
    /// If false, the query operation will start right away.
    /// </param>
    /// <param name="logThunkException">Log unhandled exception in the related AsyncThunk.</param>
    record QueryOptions(
        bool? refetchOnFocus = null,
        int? pollingInterval = null,
        bool? startOnAwait = null,
        bool? logThunkException = null,
        int? keepUnusedDataFor = null,
        CancellationToken? cancellationToken = null);
}
