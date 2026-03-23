using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor.Checkpoint.Events;
using Unity.AI.Assistant.Editor.Checkpoint.Git;
using Unity.AI.Assistant.Editor.Utils.Event;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Unity.AI.Assistant.Editor.Checkpoint
{
    sealed class CheckpointSystem : IDisposable
    {
        const string k_TagPrefix = "msg-";
        
        readonly string m_RepoPath;
        readonly string m_ProjectPath;
        readonly object m_CacheLock = new();
        readonly HashSet<string> m_CachedMessageTags = new();
        readonly Dictionary<string, string> m_PendingCheckpoints = new();

        IVcsAdapter m_Adapter;
        bool m_TagsCacheLoaded;
        bool m_Disposed;

        public bool IsInitialized => m_Adapter?.IsInitialized ?? false;
        public string RepositoryPath => m_Adapter?.RepositoryPath ?? string.Empty;

        public CheckpointSystem(string repoPath, string projectPath)
        {
            if (string.IsNullOrEmpty(repoPath))
            {
                throw new ArgumentNullException(nameof(repoPath));
            }
            if (string.IsNullOrEmpty(projectPath))
            {
                throw new ArgumentNullException(nameof(projectPath));
            }

            m_RepoPath = repoPath;
            m_ProjectPath = projectPath;
        }

        public VcsRepositoryHealth GetRepositoryHealth()
        {
            if (m_Adapter == null)
            {
                return VcsRepositoryHealth.Missing();
            }

            return m_Adapter.CheckHealth();
        }

        public async Task<CheckpointResult<bool>> InitializeAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (IsInitialized)
            {
                InternalLog.Log("Repository already initialized");
                return CheckpointResult<bool>.Ok(true, "Repository already initialized");
            }

            try
            {
                var repoExists = Directory.Exists(Path.Combine(m_RepoPath, ".git"));

                m_Adapter?.Dispose();
                m_Adapter = CreateAdapter();

                var health = m_Adapter.CheckHealth();
                if (health.Status == VcsRepositoryHealthStatus.Locked)
                {
                    InternalLog.Log("Cleaning up stale lock files");
                    if (!m_Adapter.TryUnlock())
                    {
                        InternalLog.LogError("Failed to unlock VCS for Checkpointing");
                    }
                }

                if (repoExists && health.Status == VcsRepositoryHealthStatus.Healthy)
                {
                    InternalLog.Log("Reconnected to existing repository");
                    await RefreshTagsCacheAsync(ct);
                    return CheckpointResult<bool>.Ok(true, "Reconnected to existing repository");
                }

                if (repoExists && health.Status == VcsRepositoryHealthStatus.Corrupted)
                {
                    InternalLog.Log("Repository corrupted, reinitializing...");
                    await DeleteRepositoryAsync();
                }

                Directory.CreateDirectory(m_RepoPath);

                var initResult = await m_Adapter.InitializeRepositoryAsync(ct);
                if (!initResult.Success)
                {
                    var errorType = ClassifyVcsError(initResult);
                    return CheckpointResult<bool>.Fail(errorType, "Failed to initialize repository", initResult.Error);
                }

                var initialHash = await CreateCheckpointInternalAsync("Initial checkpoint - Project snapshot", ct);
                if (string.IsNullOrEmpty(initialHash))
                {
                    InternalLog.Log("Initial commit created (or no files to commit)");
                }

                InternalLog.Log("Repository initialized successfully");
                return CheckpointResult<bool>.Ok(true, "Repository initialized");
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"Failed to initialize: {ex.Message}");
                return CheckpointResult<bool>.Fail(CheckpointErrorType.VcsCommandFailed, "Failed to initialize checkpoints", ex.Message);
            }
        }

        public async Task<CheckpointResult<string>> CreateCheckpointAsync(string message, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            var hash = await CreateCheckpointInternalAsync(message, ct);
            if (string.IsNullOrEmpty(hash))
            {
                return CheckpointResult<string>.Fail(CheckpointErrorType.VcsCommandFailed, "Failed to create checkpoint");
            }
            return CheckpointResult<string>.Ok(hash);
        }

        async Task<string> CreateCheckpointInternalAsync(string message, CancellationToken ct)
        {
            if (!IsInitialized)
            {
                InternalLog.LogError("Repository not initialized");
                return null;
            }

            try
            {
                await SaveAllAssetsAsync();

                var stageResult = await m_Adapter.StageAllAsync(ct);
                if (!stageResult.Success)
                {
                    InternalLog.LogError($"Failed to stage files: {stageResult.Error}");
                    return null;
                }

                var hasChanges = await m_Adapter.HasStagedChangesAsync(ct);
                var commitResult = await m_Adapter.CommitAsync(message, allowEmpty: !hasChanges, ct);
                if (!commitResult.Success)
                {
                    InternalLog.LogError($"Failed to commit: {commitResult.Error}");
                    return null;
                }

                var hash = await m_Adapter.GetHeadCommitHashAsync(ct);
                if (string.IsNullOrEmpty(hash))
                {
                    InternalLog.LogError("Failed to get commit hash");
                    return null;
                }

                InternalLog.Log($"Created checkpoint: {hash} - {message}");
                AssistantEvents.Send(new EventCheckpointsChanged());
                return hash;
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"Failed to create checkpoint: {ex.Message}");
                return null;
            }
        }

        public async Task<List<CheckpointInfo>> GetCheckpointsAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            var checkpoints = new List<CheckpointInfo>();

            if (!IsInitialized)
            {
                return checkpoints;
            }

            try
            {
                var tagToCommit = await BuildTagToCommitMapAsync(ct);
                var commits = await m_Adapter.GetCommitHistoryAsync(ct);

                foreach (var commit in commits)
                {
                    string conversationId = null;
                    string fragmentId = null;

                    if (tagToCommit.TryGetValue(commit.Hash, out var tagInfoList) && tagInfoList.Count > 0)
                    {
                        // Use the first tag for this commit
                        var tagInfo = tagInfoList[0];
                        conversationId = tagInfo.ConversationId;
                        fragmentId = tagInfo.FragmentId;
                    }

                    var checkpoint = new CheckpointInfo(
                        commit.Hash,
                        commit.Message,
                        commit.TimestampUnixSeconds * 1000,
                        conversationId,
                        fragmentId);

                    checkpoints.Add(checkpoint);
                }
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"Failed to get checkpoints: {ex.Message}");
            }

            return checkpoints;
        }

        public async Task<CheckpointResult<bool>> RestoreCheckpointAsync(string commitHash, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (!IsInitialized)
            {
                return CheckpointResult<bool>.Fail(CheckpointErrorType.RepositoryMissing, "Checkpoints not initialized");
            }

            if (string.IsNullOrEmpty(commitHash))
            {
                return CheckpointResult<bool>.Fail(CheckpointErrorType.VcsCommandFailed, "Invalid commit hash");
            }

            try
            {
                await SaveAllAssetsAsync();

                await m_Adapter.ResetHardAsync("HEAD", ct);
                await m_Adapter.CleanUntrackedAsync(ct);

                var checkoutResult = await m_Adapter.CheckoutFilesAsync(commitHash, ct);
                if (!checkoutResult.Success)
                {
                    var errorType = ClassifyVcsError(checkoutResult);
                    return CheckpointResult<bool>.Fail(errorType, "Failed to restore checkpoint", checkoutResult.Error);
                }

                await m_Adapter.CleanUntrackedAsync(ct);

                InternalLog.Log($"Restored to checkpoint: {commitHash}");
                MainThread.DispatchAndForget(() => AssetDatabase.Refresh());
                return CheckpointResult<bool>.Ok(true, "Restored to checkpoint");
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"Failed to restore: {ex.Message}");
                return CheckpointResult<bool>.Fail(CheckpointErrorType.VcsCommandFailed, "Failed to restore checkpoint", ex.Message);
            }
        }

        public async Task<CheckpointResult<bool>> DeleteCheckpointAsync(string commitHash, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (!IsInitialized)
            {
                return CheckpointResult<bool>.Fail(CheckpointErrorType.RepositoryMissing, "Checkpoints not initialized");
            }

            if (string.IsNullOrEmpty(commitHash))
            {
                return CheckpointResult<bool>.Fail(CheckpointErrorType.VcsCommandFailed, "Invalid commit hash");
            }

            try
            {
                var tags = await m_Adapter.GetTagsAtCommitAsync(commitHash, ct);
                foreach (var tag in tags)
                {
                    if (!tag.StartsWith(k_TagPrefix))
                    {
                        continue;
                    }

                    await m_Adapter.DeleteTagAsync(tag, ct);

                    var parsed = ParseTag(tag);
                    if (parsed.HasValue)
                    {
                        var conversationId = new AssistantConversationId(parsed.Value.ConversationId);
                        RemoveFromTagsCache(conversationId, parsed.Value.FragmentId);
                    }
                }

                InternalLog.Log($"Deleted checkpoint tag for: {commitHash}");
                AssistantEvents.Send(new EventCheckpointsChanged());
                return CheckpointResult<bool>.Ok(true, "Deleted checkpoint");
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"Failed to delete: {ex.Message}");
                return CheckpointResult<bool>.Fail(CheckpointErrorType.VcsCommandFailed, "Failed to delete checkpoint", ex.Message);
            }
        }

        async Task TagCheckpointAsync(string commitHash, AssistantConversationId conversationId, string fragmentId, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (!IsInitialized || string.IsNullOrEmpty(commitHash))
            {
                return;
            }

            try
            {
                var tagName = BuildTagName(conversationId, fragmentId);
                var result = await m_Adapter.CreateTagAsync(tagName, commitHash, ct);

                if (result.Success)
                {
                    AddToTagsCache(conversationId, fragmentId);
                }
                else if (result.Error.Contains("already exists"))
                {
                    InternalLog.Log($"Tag {tagName} already exists, skipping");
                }
                else
                {
                    InternalLog.LogWarning($"Failed to create tag: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                InternalLog.LogWarning($"Failed to tag checkpoint: {ex.Message}");
            }
        }

        public async Task UpdateTagAsync(string commitHash, AssistantConversationId conversationId, string oldFragmentId, string newFragmentId, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (!IsInitialized || string.IsNullOrEmpty(commitHash))
            {
                return;
            }

            try
            {
                var oldTagName = BuildTagName(conversationId, oldFragmentId);
                var newTagName = BuildTagName(conversationId, newFragmentId);

                await m_Adapter.DeleteTagAsync(oldTagName, ct);
                await m_Adapter.CreateTagAsync(newTagName, commitHash, ct);

                RemoveFromTagsCache(conversationId, oldFragmentId);
                AddToTagsCache(conversationId, newFragmentId);

                AssistantEvents.Send(new EventCheckpointTagsChanged(conversationId, newFragmentId));
            }
            catch (Exception ex)
            {
                InternalLog.LogWarning($"Failed to update tag: {ex.Message}");
            }
        }

        public async Task<string> GetCheckpointForMessageAsync(AssistantConversationId conversationId, string fragmentId, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (!IsInitialized || conversationId == AssistantConversationId.Invalid || string.IsNullOrEmpty(fragmentId))
            {
                return null;
            }

            try
            {
                var tagName = BuildTagName(conversationId, fragmentId);
                return await m_Adapter.GetCommitForTagAsync(tagName, ct);
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"Failed to find checkpoint for message: {ex.Message}");
                return null;
            }
        }

        public bool HasCheckpointForMessage(AssistantConversationId conversationId, string fragmentId)
        {
            if (!IsInitialized || conversationId == AssistantConversationId.Invalid || string.IsNullOrEmpty(fragmentId))
            {
                return false;
            }

            var tagKey = BuildTagKey(conversationId, fragmentId);
            lock (m_CacheLock)
            {
                return m_TagsCacheLoaded && m_CachedMessageTags.Contains(tagKey);
            }
        }

        public async Task RefreshTagsCacheAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (!IsInitialized)
            {
                lock (m_CacheLock)
                {
                    m_TagsCacheLoaded = false;
                }
                return;
            }

            try
            {
                var tags = await m_Adapter.GetTagsWithPrefixAsync(k_TagPrefix, ct);

                lock (m_CacheLock)
                {
                    m_CachedMessageTags.Clear();

                    foreach (var tag in tags)
                    {
                        if (tag.StartsWith(k_TagPrefix))
                        {
                            var key = tag.Substring(k_TagPrefix.Length);
                            m_CachedMessageTags.Add(key);
                        }
                    }

                    m_TagsCacheLoaded = true;
                }
            }
            catch (Exception ex)
            {
                InternalLog.LogWarning($"Failed to refresh tags cache: {ex.Message}");
                lock (m_CacheLock)
                {
                    m_TagsCacheLoaded = false;
                }
            }
        }

        public void SetPendingCheckpoint(AssistantConversationId conversationId, string incompleteFragmentId, string checkpointHash)
        {
            var key = BuildTagKey(conversationId, incompleteFragmentId);
            lock (m_CacheLock)
            {
                m_PendingCheckpoints[key] = checkpointHash;
            }
        }

        public async Task CompletePendingCheckpointAsync(AssistantConversationId conversationId, string incompleteFragmentId, string realFragmentId, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            var key = BuildTagKey(conversationId, incompleteFragmentId);
            string checkpointHash;
            lock (m_CacheLock)
            {
                if (!m_PendingCheckpoints.TryGetValue(key, out checkpointHash))
                {
                    return;
                }
                m_PendingCheckpoints.Remove(key);
            }

            await TagCheckpointAsync(checkpointHash, conversationId, realFragmentId, ct);
            
            AssistantEvents.Send(new EventCheckpointsChanged());
        }

        public bool HasPendingCheckpoint(AssistantConversationId conversationId, string incompleteFragmentId)
        {
            var key = BuildTagKey(conversationId, incompleteFragmentId);
            lock (m_CacheLock)
            {
                return m_PendingCheckpoints.ContainsKey(key);
            }
        }

        public async Task<CheckpointResult<int>> DeleteOldCheckpointsAsync(int retentionWeeks, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (!IsInitialized)
            {
                return CheckpointResult<int>.Fail(CheckpointErrorType.RepositoryMissing, "Checkpoints not initialized");
            }

            if (retentionWeeks < 1)
            {
                return CheckpointResult<int>.Ok(0, "Invalid retention period");
            }

            try
            {
                var cutoffTime = DateTimeOffset.UtcNow.AddDays(-retentionWeeks * 7);
                var cutoffUnixSeconds = cutoffTime.ToUnixTimeSeconds();

                var commits = await m_Adapter.GetCommitHistoryAsync(ct);
                var deletedCount = 0;

                foreach (var commit in commits)
                {
                    if (commit.TimestampUnixSeconds >= cutoffUnixSeconds)
                    {
                        continue;
                    }

                    var tags = await m_Adapter.GetTagsAtCommitAsync(commit.Hash, ct);
                    foreach (var tag in tags)
                    {
                        if (!tag.StartsWith(k_TagPrefix))
                        {
                            continue;
                        }

                        await m_Adapter.DeleteTagAsync(tag, ct);

                        var parsed = ParseTag(tag);
                        if (parsed.HasValue)
                        {
                            var conversationId = new AssistantConversationId(parsed.Value.ConversationId);
                            RemoveFromTagsCache(conversationId, parsed.Value.FragmentId);
                        }

                        deletedCount++;
                    }
                }

                if (deletedCount > 0)
                {
                    InternalLog.Log($"Deleted {deletedCount} old checkpoint tags (older than {retentionWeeks} weeks)");

                    var pruneResult = await m_Adapter.PruneUnreferencedObjectsAsync(ct);
                    if (!pruneResult.Success)
                    {
                        InternalLog.LogWarning($"Failed to prune repository: {pruneResult.Error}");
                    }

                    AssistantEvents.Send(new EventCheckpointsChanged());
                }

                return CheckpointResult<int>.Ok(deletedCount, $"Deleted {deletedCount} old checkpoints");
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"Failed to delete old checkpoints: {ex.Message}");
                return CheckpointResult<int>.Fail(CheckpointErrorType.VcsCommandFailed, "Failed to delete old checkpoints", ex.Message);
            }
        }

        public async Task<CheckpointResult<bool>> ResetRepositoryAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            m_Adapter?.Dispose();
            m_Adapter = null;

            lock (m_CacheLock)
            {
                m_CachedMessageTags.Clear();
                m_TagsCacheLoaded = false;
                m_PendingCheckpoints.Clear();
            }

            await DeleteRepositoryAsync();

            return await InitializeAsync(ct);
        }

        async Task DeleteRepositoryAsync()
        {
            if (!Directory.Exists(m_RepoPath))
            {
                return;
            }

            try
            {
                await Task.Run(() => DeleteDirectoryRecursive(m_RepoPath));
                InternalLog.Log("Repository directory deleted");
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"Failed to delete repository: {ex.Message}");
            }
        }

        static void DeleteDirectoryRecursive(string path)
        {
            var di = new DirectoryInfo(path);
            if (!di.Exists)
            {
                return;
            }

            foreach (var file in di.GetFiles("*", SearchOption.AllDirectories))
            {
                file.Attributes = FileAttributes.Normal;
            }

            di.Delete(true);
        }

        async Task<Dictionary<string, List<(string ConversationId, string FragmentId)>>> BuildTagToCommitMapAsync(CancellationToken ct)
        {
            var map = new Dictionary<string, List<(string, string)>>();
            var tags = await m_Adapter.GetTagsWithPrefixAsync(k_TagPrefix, ct);

            foreach (var tag in tags)
            {
                var hash = await m_Adapter.GetCommitForTagAsync(tag, ct);
                if (string.IsNullOrEmpty(hash))
                {
                    continue;
                }

                var parsed = ParseTag(tag);
                if (parsed.HasValue)
                {
                    if (!map.TryGetValue(hash, out var list))
                    {
                        list = new List<(string, string)>();
                        map[hash] = list;
                    }
                    list.Add(parsed.Value);
                }
            }

            return map;
        }

        static (string ConversationId, string FragmentId)? ParseTag(string tag)
        {
            if (!tag.StartsWith(k_TagPrefix))
            {
                return null;
            }

            var content = tag.Substring(k_TagPrefix.Length);
            var parts = content.Split('-');

            if (parts.Length < 6)
            {
                return null;
            }

            // GUID has 5 parts (8-4-4-4-12 format)
            var convId = $"{parts[0]}-{parts[1]}-{parts[2]}-{parts[3]}-{parts[4]}";

            // FragmentId is everything after the GUID (may contain dashes)
            var fragId = string.Join("-", parts.Skip(5));
            return (convId, fragId);
        }

        static string BuildTagName(AssistantConversationId conversationId, string fragmentId)
        {
            return $"{k_TagPrefix}{conversationId}-{fragmentId}";
        }

        static string BuildTagKey(AssistantConversationId conversationId, string fragmentId)
        {
            return $"{conversationId}-{fragmentId}";
        }

        void AddToTagsCache(AssistantConversationId conversationId, string fragmentId)
        {
            var tagKey = BuildTagKey(conversationId, fragmentId);
            lock (m_CacheLock)
            {
                m_CachedMessageTags.Add(tagKey);
            }
        }

        void RemoveFromTagsCache(AssistantConversationId conversationId, string fragmentId)
        {
            var tagKey = BuildTagKey(conversationId, fragmentId);
            lock (m_CacheLock)
            {
                m_CachedMessageTags.Remove(tagKey);
            }
        }

        IVcsAdapter CreateAdapter()
        {
            return new GitAdapter(m_RepoPath, m_ProjectPath);
        }

        CheckpointErrorType ClassifyVcsError(VcsResult result)
        {
            return GitAdapter.ClassifyError(result);
        }

        static async Task SaveAllAssetsAsync()
        {
            var tcs = new TaskCompletionSource<bool>();

            MainThread.DispatchAndForget(() =>
            {
                try
                {
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        var scene = SceneManager.GetSceneAt(i);
                        if (scene.isLoaded)
                        {
                            EditorSceneManager.MarkSceneDirty(scene);
                        }
                    }

                    EditorSceneManager.SaveOpenScenes();
                    AssetDatabase.SaveAssets();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    InternalLog.LogWarning($"Failed to save assets: {ex.Message}");
                    tcs.TrySetResult(false);
                }
            });

            using var cts = new CancellationTokenSource(5000);
            try
            {
                await Task.WhenAny(tcs.Task, Task.Delay(-1, cts.Token));
            }
            catch (OperationCanceledException)
            {
                InternalLog.LogWarning("SaveAllAssets timed out");
            }
        }

        void ThrowIfDisposed()
        {
            if (m_Disposed)
            {
                throw new ObjectDisposedException(nameof(CheckpointSystem));
            }
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }

            m_Disposed = true;
            m_Adapter?.Dispose();
            m_Adapter = null;

            lock (m_CacheLock)
            {
                m_CachedMessageTags.Clear();
                m_PendingCheckpoints.Clear();
            }
        }
    }
}
