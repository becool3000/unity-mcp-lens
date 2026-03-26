using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor.Checkpoint.Events;
using Unity.AI.Assistant.Editor.Checkpoint.Git;
using Unity.AI.Assistant.Editor.Utils.Event;
using Unity.AI.Assistant.Utils;
using Unity.AI.Toolkit;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Checkpoint
{
    [InitializeOnLoad]
    static class AssistantCheckpoints
    {
        const string k_LibraryFolderName = "Library";
        const string k_RepoFolderName = "AssistantCheckpoints";

        static readonly string k_ProjectPath = Path.GetDirectoryName(Application.dataPath);
        static readonly string k_LibraryPath = Path.Combine(k_ProjectPath, k_LibraryFolderName);
        static readonly string k_RepoPath = Path.Combine(k_LibraryPath, k_RepoFolderName);

        static readonly CheckpointSystem s_System = new(k_RepoPath, k_ProjectPath);
        static readonly SemaphoreSlim s_InitLock = new(1, 1);
        static Task<CheckpointResult<bool>> s_InitTask;

        static AssistantCheckpoints()
        {
            EditorTask.delayCall += OnDomainReloadDelayed;
            AssistantEvents.Subscribe<EventCheckpointRestoreRequested>(OnRestoreRequested);
            AssistantEvents.Subscribe<EventCheckpointDeleteRequested>(OnDeleteRequested);
        }

        public static bool IsInitialized => s_System.IsInitialized;
        public static string RepositoryPath => s_System.RepositoryPath;

        static async void OnDomainReloadDelayed()
        {
            if (!AssistantProjectPreferences.CheckpointEnabled)
            {
                return;
            }

            try
            {
                // Check repository health - reinitialize if missing, corrupted, or deleted
                var health = s_System.GetRepositoryHealth();
                if (health.Status == VcsRepositoryHealthStatus.Missing)
                {
                    // Folder was deleted or never existed - reinitialize
                    InternalLog.Log("Checkpoint repository missing, reinitializing...");
                }
                else if (health.Status == VcsRepositoryHealthStatus.Corrupted)
                {
                    InternalLog.Log("Checkpoint repository corrupted, reinitializing...");
                }

                var result = await InitializeAsync();
                if (!result.Success)
                {
                    InternalLog.LogWarning($"Failed to reconnect after domain reload: {result.InternalMessage}");
                }
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"Error reconnecting after domain reload: {ex.Message}");
            }
        }

        public static VcsRepositoryHealth GetRepositoryHealth()
        {
            return s_System.GetRepositoryHealth();
        }

        public static GitValidationResult ValidateGitInstance()
        {
            var config = new GitInstanceConfig(
                AssistantProjectPreferences.GitInstanceType,
                AssistantProjectPreferences.CustomGitPath);
            return GitInstanceResolver.ValidateConfig(config);
        }

        public static async Task<CheckpointResult<bool>> InitializeAsync(CancellationToken ct = default)
        {
            var existingTask = s_InitTask;
            if (existingTask != null && !existingTask.IsCompleted)
            {
                return await existingTask;
            }

            await s_InitLock.WaitAsync(ct);
            try
            {
                if (s_System.IsInitialized)
                {
                    return CheckpointResult<bool>.Ok(true, "Repository already initialized");
                }

                s_InitTask = InitializeCoreAsync(ct);
                return await s_InitTask;
            }
            finally
            {
                s_InitLock.Release();
            }
        }

        static async Task<CheckpointResult<bool>> InitializeCoreAsync(CancellationToken ct)
        {
            var validation = ValidateGitInstance();

            if (!validation.IsValid)
            {
                var fallback = GitInstanceResolver.FindFirstValidInstance(AssistantProjectPreferences.CustomGitPath);
                if (fallback.HasValue)
                {
                    AssistantProjectPreferences.GitInstanceType = fallback.Value;
                    validation = ValidateGitInstance();
                }
            }

            if (!validation.IsValid)
            {
                var errorType = validation.GitFound
                    ? CheckpointErrorType.VcsExtensionMissing
                    : CheckpointErrorType.VcsNotFound;
                return CheckpointResult<bool>.Fail(errorType, validation.ErrorMessage, validation.ErrorMessage);
            }

            return await s_System.InitializeAsync(ct);
        }

        public static async Task<CheckpointResult<string>> CreateCheckpointAsync(string message, CancellationToken ct = default)
        {
            await DeleteOldCheckpointsAsync(ct);
            return await s_System.CreateCheckpointAsync(message, ct);
        }

        public static async Task<List<CheckpointInfo>> GetCheckpointsAsync(CancellationToken ct = default)
        {
            return await s_System.GetCheckpointsAsync(ct);
        }

        static async Task<CheckpointResult<bool>> RestoreCheckpointAsync(string commitHash, CancellationToken ct = default)
        {
            return await s_System.RestoreCheckpointAsync(commitHash, ct);
        }

        static async Task<CheckpointResult<bool>> DeleteCheckpointAsync(string commitHash, CancellationToken ct = default)
        {
            return await s_System.DeleteCheckpointAsync(commitHash, ct);
        }

        public static async Task UpdateTagAsync(string commitHash, AssistantConversationId conversationId, string oldFragmentId, string newFragmentId, CancellationToken ct = default)
        {
            await s_System.UpdateTagAsync(commitHash, conversationId, oldFragmentId, newFragmentId, ct);
        }

        public static async Task<string> GetCheckpointForMessageAsync(AssistantConversationId conversationId, string fragmentId, CancellationToken ct = default)
        {
            return await s_System.GetCheckpointForMessageAsync(conversationId, fragmentId, ct);
        }

        public static bool HasCheckpointForMessage(AssistantConversationId conversationId, string fragmentId)
        {
            return s_System.HasCheckpointForMessage(conversationId, fragmentId);
        }

        public static async Task RefreshTagsCacheAsync(CancellationToken ct = default)
        {
            await s_System.RefreshTagsCacheAsync(ct);
        }

        public static void SetPendingCheckpoint(AssistantConversationId conversationId, string incompleteFragmentId, string checkpointHash)
        {
            s_System.SetPendingCheckpoint(conversationId, incompleteFragmentId, checkpointHash);
        }

        public static async Task CompletePendingCheckpointAsync(AssistantConversationId conversationId, string incompleteFragmentId, string realFragmentId, CancellationToken ct = default)
        {
            await s_System.CompletePendingCheckpointAsync(conversationId, incompleteFragmentId, realFragmentId, ct);
        }

        public static bool HasPendingCheckpoint(AssistantConversationId conversationId, string incompleteFragmentId)
        {
            return s_System.HasPendingCheckpoint(conversationId, incompleteFragmentId);
        }

        static async Task<CheckpointResult<int>> DeleteOldCheckpointsAsync(CancellationToken ct = default)
        {
            var retentionWeeks = AssistantProjectPreferences.CheckpointRetentionWeeks;
            return await s_System.DeleteOldCheckpointsAsync(retentionWeeks, ct);
        }

        public static async Task<CheckpointResult<bool>> ResetRepositoryAsync(CancellationToken ct = default)
        {
            if (!AssistantProjectPreferences.CheckpointEnabled)
            {
                return CheckpointResult<bool>.Ok(true, "Repository reset (checkpoints disabled)");
            }

            return await s_System.ResetRepositoryAsync(ct);
        }

        static async void OnRestoreRequested(EventCheckpointRestoreRequested evt)
        {
            var messageId = evt.MessageId;

            try
            {
                if (!IsInitialized)
                {
                    AssistantEvents.Send(new EventCheckpointRestoreCompleted(messageId, false, "Checkpoints not initialized"));
                    return;
                }

                if (!messageId.ConversationId.IsValid || string.IsNullOrEmpty(messageId.FragmentId))
                {
                    AssistantEvents.Send(new EventCheckpointRestoreCompleted(messageId, false, "Invalid message ID"));
                    return;
                }

                var hash = await GetCheckpointForMessageAsync(messageId.ConversationId, messageId.FragmentId);
                if (string.IsNullOrEmpty(hash))
                {
                    AssistantEvents.Send(new EventCheckpointRestoreCompleted(messageId, false, "Checkpoint not found"));
                    return;
                }

                var result = await RestoreCheckpointAsync(hash);
                var message = result.Success ? "Reverted to checkpoint" : result.PublicMessage;
                AssistantEvents.Send(new EventCheckpointRestoreCompleted(messageId, result.Success, message));
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"Restore failed: {ex.Message}");
                AssistantEvents.Send(new EventCheckpointRestoreCompleted(messageId, false, "Internal error"));
            }
        }

        static async void OnDeleteRequested(EventCheckpointDeleteRequested evt)
        {
            var messageId = evt.MessageId;

            try
            {
                if (!IsInitialized)
                {
                    AssistantEvents.Send(new EventCheckpointDeleteCompleted(messageId, false, "Checkpoints not initialized"));
                    return;
                }

                if (!messageId.ConversationId.IsValid || string.IsNullOrEmpty(messageId.FragmentId))
                {
                    AssistantEvents.Send(new EventCheckpointDeleteCompleted(messageId, false, "Invalid message ID"));
                    return;
                }

                var hash = await GetCheckpointForMessageAsync(messageId.ConversationId, messageId.FragmentId);
                if (string.IsNullOrEmpty(hash))
                {
                    AssistantEvents.Send(new EventCheckpointDeleteCompleted(messageId, false, "Checkpoint not found"));
                    return;
                }

                var result = await DeleteCheckpointAsync(hash);
                var message = result.Success ? "Deleted checkpoint" : result.PublicMessage;
                AssistantEvents.Send(new EventCheckpointDeleteCompleted(messageId, result.Success, message));
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"Delete failed: {ex.Message}");
                AssistantEvents.Send(new EventCheckpointDeleteCompleted(messageId, false, "Internal error"));
            }
        }
    }
}
