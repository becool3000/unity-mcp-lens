using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.AI.Assistant.Editor.Checkpoint
{
    interface IVcsAdapter : IDisposable
    {
        bool IsInitialized { get; }
        string RepositoryPath { get; }

        VcsRepositoryHealth CheckHealth();
        bool TryUnlock();

        Task<VcsResult> InitializeRepositoryAsync(CancellationToken ct = default);

        Task<VcsResult> StageAllAsync(CancellationToken ct = default);
        Task<VcsResult> CommitAsync(string message, bool allowEmpty = false, CancellationToken ct = default);
        Task<string> GetHeadCommitHashAsync(CancellationToken ct = default);
        Task<bool> HasStagedChangesAsync(CancellationToken ct = default);

        Task<IReadOnlyList<VcsCommitInfo>> GetCommitHistoryAsync(CancellationToken ct = default);

        Task<VcsResult> ResetHardAsync(string commitHash = "HEAD", CancellationToken ct = default);
        Task<VcsResult> CleanUntrackedAsync(CancellationToken ct = default);
        Task<VcsResult> CheckoutFilesAsync(string commitHash, CancellationToken ct = default);

        Task<VcsResult> CreateTagAsync(string tagName, string commitHash, CancellationToken ct = default);
        Task<VcsResult> DeleteTagAsync(string tagName, CancellationToken ct = default);
        Task<IReadOnlyList<string>> GetTagsWithPrefixAsync(string prefix, CancellationToken ct = default);
        Task<string> GetCommitForTagAsync(string tagName, CancellationToken ct = default);
        Task<IReadOnlyList<string>> GetTagsAtCommitAsync(string commitHash, CancellationToken ct = default);

        Task<VcsResult> PruneUnreferencedObjectsAsync(CancellationToken ct = default);
    }
}
