using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.Editor.Checkpoint.Git
{
    sealed class GitAdapter : IVcsAdapter
    {
        const string k_GitOptions = "-c http.lowSpeedTime=600 -c http.lowSpeedLimit=1000";
        const string k_GitDirName = ".git";
        const string k_IndexLockFileName = "index.lock";
        const string k_HeadFileName = "HEAD";
        const string k_SparseCheckoutDir = "info";
        const string k_SparseCheckoutFileName = "sparse-checkout";
        const string k_UserName = "Unity Assistant";
        const string k_UserEmail = "assistant@unity.com";

        static readonly string[] k_TrackedPaths = { "Assets", "ProjectSettings", "Packages" };

        const int k_StaleLockThresholdSeconds = 60;
        const int k_DefaultTimeoutMs = 5 * 60 * 1000;

        readonly string m_GitDir;
        readonly string m_WorkTree;
        
        bool m_Disposed;

        public GitAdapter(string repositoryRoot, string workTree)
        {
            if (string.IsNullOrEmpty(repositoryRoot))
            {
                throw new ArgumentNullException(nameof(repositoryRoot));
            }
            if (string.IsNullOrEmpty(workTree))
            {
                throw new ArgumentNullException(nameof(workTree));
            }

            m_GitDir = Path.Combine(repositoryRoot, k_GitDirName);
            m_WorkTree = workTree;
        }
        
        public bool IsInitialized => Directory.Exists(m_GitDir);
        public string RepositoryPath => Path.GetDirectoryName(m_GitDir);

        static string GetGitPath()
        {
            var config = new GitInstanceConfig(
                AssistantProjectPreferences.GitInstanceType,
                AssistantProjectPreferences.CustomGitPath);
            return GitInstanceResolver.ResolvePath(config);
        }

        public VcsRepositoryHealth CheckHealth()
        {
            var repoRoot = Path.GetDirectoryName(m_GitDir);

            if (!Directory.Exists(repoRoot) || !Directory.Exists(m_GitDir))
            {
                return VcsRepositoryHealth.Missing();
            }

            var headFile = Path.Combine(m_GitDir, k_HeadFileName);
            if (!File.Exists(headFile))
            {
                return VcsRepositoryHealth.Corrupted("Missing HEAD file");
            }

            var lockPath = GetLockFilePath();
            if (File.Exists(lockPath) && IsLockStale(lockPath))
            {
                return VcsRepositoryHealth.Locked(lockPath);
            }

            return VcsRepositoryHealth.Healthy();
        }

        public bool TryUnlock()
        {
            var lockPath = GetLockFilePath();

            if (!File.Exists(lockPath))
            {
                return true;
            }

            if (!IsLockStale(lockPath))
            {
                InternalLog.Log("Lock file exists but is not stale, waiting...");
                return false;
            }

            return TryRemoveStaleLock(lockPath);
        }

        string GetLockFilePath()
        {
            return Path.Combine(m_GitDir, k_IndexLockFileName);
        }

        bool IsLockStale(string lockPath)
        {
            if (!File.Exists(lockPath))
            {
                return false;
            }

            try
            {
                var lastWrite = File.GetLastWriteTimeUtc(lockPath);
                var age = DateTime.UtcNow - lastWrite;
                return age.TotalSeconds > k_StaleLockThresholdSeconds;
            }
            catch (Exception ex)
            {
                InternalLog.LogWarning($"Failed to check lock age: {ex.Message}");
                return false;
            }
        }

        bool TryRemoveStaleLock(string lockPath)
        {
            if (!File.Exists(lockPath))
            {
                return true;
            }

            try
            {
                File.Delete(lockPath);
                InternalLog.Log($"Removed stale lock file: {lockPath}");
                return true;
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"Failed to remove lock file: {ex.Message}");
                return false;
            }
        }

        public async Task<VcsResult> InitializeRepositoryAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            var parentDir = Path.GetDirectoryName(m_GitDir);
            if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            var initResult = await RunGitAsync("init", ct);
            if (!initResult.Success)
            {
                return initResult;
            }

            var sparseResult = await ConfigureSparseCheckoutAsync(ct);
            if (!sparseResult.Success)
            {
                InternalLog.LogWarning($"Failed to configure sparse checkout: {sparseResult.Error}");
            }

            var userResult = await ConfigureUserAsync(ct);
            if (!userResult.Success)
            {
                InternalLog.LogWarning($"Failed to configure user: {userResult.Error}");
            }

            return initResult;
        }

        async Task<VcsResult> ConfigureSparseCheckoutAsync(CancellationToken ct)
        {
            var enableResult = await RunGitAsync("config core.sparseCheckout true", ct);
            if (!enableResult.Success)
            {
                return enableResult;
            }

            var sparseDir = Path.Combine(m_GitDir, k_SparseCheckoutDir);
            if (!Directory.Exists(sparseDir))
            {
                Directory.CreateDirectory(sparseDir);
            }

            var sparsePath = Path.Combine(sparseDir, k_SparseCheckoutFileName);
            var sparseCheckoutContent = string.Join("\n", Array.ConvertAll(k_TrackedPaths, p => $"/{p}/")) + "\n";
            try
            {
                await File.WriteAllTextAsync(sparsePath, sparseCheckoutContent, ct);
            }
            catch (Exception ex)
            {
                return VcsResult.Fail($"Failed to write sparse-checkout file: {ex.Message}");
            }

            return VcsResult.Ok();
        }

        async Task<VcsResult> ConfigureUserAsync(CancellationToken ct)
        {
            var emailResult = await RunGitAsync($"config user.email \"{EscapeArgument(k_UserEmail)}\"", ct);
            if (!emailResult.Success)
            {
                return emailResult;
            }

            return await RunGitAsync($"config user.name \"{EscapeArgument(k_UserName)}\"", ct);
        }

        public async Task<VcsResult> StageAllAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            var pathArgs = string.Join(" ", k_TrackedPaths);
            return await RunGitAsync($"add -A {pathArgs}", ct);
        }

        public async Task<VcsResult> CommitAsync(string message, bool allowEmpty = false, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            var escapedMessage = EscapeArgument(message);
            var args = allowEmpty
                ? $"commit --allow-empty -m \"{escapedMessage}\""
                : $"commit -m \"{escapedMessage}\"";

            return await RunGitAsync(args, ct);
        }

        public async Task<string> GetHeadCommitHashAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            var result = await RunGitAsync("rev-parse HEAD", ct);
            return result.Success ? result.Output.Trim() : null;
        }

        public async Task<bool> HasStagedChangesAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            var result = await RunGitAsync("diff --cached --quiet", ct);
            // --quiet returns exit code 1 if there are staged changes, 0 if none
            return result.ExitCode == 1;
        }

        public async Task<IReadOnlyList<VcsCommitInfo>> GetCommitHistoryAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            var commits = new List<VcsCommitInfo>();
            var result = await RunGitAsync("log --all --pretty=format:\"%H|%s|%ct\" --no-merges", ct);

            if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
            {
                return commits;
            }

            var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split('|');
                if (parts.Length >= 3 && long.TryParse(parts[2], out var timestamp))
                {
                    commits.Add(new VcsCommitInfo(parts[0], parts[1], timestamp));
                }
            }

            return commits;
        }

        public async Task<VcsResult> ResetHardAsync(string commitHash = "HEAD", CancellationToken ct = default)
        {
            ThrowIfDisposed();
            return await RunGitAsync($"reset --hard {commitHash}", ct);
        }

        public async Task<VcsResult> CleanUntrackedAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            var pathArgs = string.Join(" ", k_TrackedPaths);
            return await RunGitAsync($"clean -fdx -- {pathArgs}", ct);
        }

        public async Task<VcsResult> CheckoutFilesAsync(string commitHash, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            var pathArgs = string.Join(" ", k_TrackedPaths);

            // Reset index to match target commit (this stages deletions for files that don't exist in target)
            var resetResult = await RunGitAsync($"reset {commitHash} -- {pathArgs}", ct);
            if (!resetResult.Success)
            {
                return resetResult;
            }

            // Checkout working tree from index (applies both file changes and deletions)
            return await RunGitAsync($"checkout -- {pathArgs}", ct);
        }

        public async Task<VcsResult> CreateTagAsync(string tagName, string commitHash, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            return await RunGitAsync($"tag \"{EscapeArgument(tagName)}\" {commitHash}", ct);
        }

        public async Task<VcsResult> DeleteTagAsync(string tagName, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            return await RunGitAsync($"tag -d \"{EscapeArgument(tagName)}\"", ct);
        }

        public async Task<IReadOnlyList<string>> GetTagsWithPrefixAsync(string prefix, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            var tags = new List<string>();
            var result = await RunGitAsync($"tag -l \"{EscapeArgument(prefix)}*\"", ct);

            if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
                return tags;

            var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            tags.AddRange(lines);
            return tags;
        }

        public async Task<string> GetCommitForTagAsync(string tagName, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            // Use rev-parse which reliably resolves refs to commit hashes
            // Tag names only contain alphanumeric characters and hyphens, so no quoting needed
            var result = await RunGitAsync($"rev-parse {tagName}^{{commit}}", ct);
            return result.Success && !string.IsNullOrWhiteSpace(result.Output)
                ? result.Output.Trim()
                : null;
        }

        public async Task<IReadOnlyList<string>> GetTagsAtCommitAsync(string commitHash, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            var tags = new List<string>();
            var result = await RunGitAsync($"tag --points-at {commitHash}", ct);

            if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
                return tags;

            var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            tags.AddRange(lines);
            return tags;
        }

        public async Task<VcsResult> PruneUnreferencedObjectsAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            await RunGitAsync("reflog expire --expire=now --all", ct);
            return await RunGitAsync("gc --prune=now --aggressive", ct);
        }

        public static CheckpointErrorType ClassifyError(VcsResult result)
        {
            if (result.Success)
            {
                return CheckpointErrorType.None;
            }

            var error = result.Error?.ToLowerInvariant() ?? string.Empty;

            if (error.Contains("index.lock") || error.Contains("unable to create") && error.Contains(".lock"))
            {
                return CheckpointErrorType.LockConflict;
            }

            if (error.Contains("not a git repository"))
            {
                return CheckpointErrorType.RepositoryMissing;
            }

            if (error.Contains("permission denied") || error.Contains("access denied"))
            {
                return CheckpointErrorType.PermissionDenied;
            }

            if (error.Contains("timed out") || error.Contains("timeout"))
            {
                return CheckpointErrorType.Timeout;
            }

            if (error.Contains("cancelled") || error.Contains("canceled"))
            {
                return CheckpointErrorType.Cancelled;
            }

            if (error.Contains("corrupt") || error.Contains("bad signature") || error.Contains("invalid object"))
            {
                return CheckpointErrorType.RepositoryCorrupted;
            }

            return CheckpointErrorType.VcsCommandFailed;
        }

        async Task<VcsResult> RunGitAsync(string arguments, CancellationToken ct)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = GetGitPath(),
                Arguments = $"{k_GitOptions} --git-dir=\"{m_GitDir}\" --work-tree=\"{m_WorkTree}\" {arguments}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = m_WorkTree,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            try
            {
                using var process = new Process();
                process.StartInfo = startInfo;
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(k_DefaultTimeoutMs);

                process.Start();

                // Start reading output/error in background tasks (these won't complete until process exits)
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                try
                {
                    // Wait for process exit with timeout - if timeout triggers, we kill the process
                    await WaitForExitAsync(process, cts.Token).ConfigureAwait(false);

                    // Process exited normally, get outputs (should complete quickly now that process has exited)
                    var output = await outputTask.ConfigureAwait(false);
                    var error = await errorTask.ConfigureAwait(false);

                    return VcsResult.FromCommand(process.ExitCode == 0, output, error, process.ExitCode);
                }
                catch (OperationCanceledException)
                {
                    TryKillProcess(process);
                    return ct.IsCancellationRequested
                        ? VcsResult.Fail("Git command was cancelled")
                        : VcsResult.Fail("Git command timed out");
                }
            }
            catch (OperationCanceledException)
            {
                return VcsResult.Fail("Git command was cancelled");
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"Failed to execute git command: {ex.Message}");
                return VcsResult.Fail(ex.Message);
            }
        }

        static async Task WaitForExitAsync(Process process, CancellationToken ct)
        {
            await Task.Run(process.WaitForExit, ct).ConfigureAwait(false);
        }

        static void TryKillProcess(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch(Exception e)
            {
                InternalLog.LogError("Failed to kill Git Process: " + e);
            }
        }

        static string EscapeArgument(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
        }

        void ThrowIfDisposed()
        {
            if (m_Disposed)
            {
                throw new ObjectDisposedException(nameof(GitAdapter));
            }
        }

        public void Dispose()
        {
            m_Disposed = true;
        }
    }
}
