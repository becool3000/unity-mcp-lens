using System;
using UnityEditor;

namespace Unity.AI.MCP.Editor.Helpers
{
    readonly struct BridgeStatusSnapshot
    {
        public readonly string Status;
        public readonly string Reason;
        public readonly bool ExpectedRecovery;
        public readonly string ExpectedRecoveryExpiresUtc;
        public readonly string ToolDiscoveryMode;
        public readonly int ToolCount;
        public readonly string ToolsHash;
        public readonly string ToolDiscoveryReason;
        public readonly string ToolSnapshotUtc;
        public readonly string DirectCommandHealth;
        public readonly string LastCommandSuccessUtc;
        public readonly string LastCommandFailureUtc;
        public readonly string LastCommandFailureReason;
        public readonly string BridgeSessionId;
        public readonly long ManifestVersion;
        public readonly string ProfileCatalogVersion;
        public readonly bool SupportsToolSyncLens;
        public readonly string LastToolsChangedUtc;

        public BridgeStatusSnapshot(
            string status,
            string reason,
            bool expectedRecovery,
            string expectedRecoveryExpiresUtc,
            string toolDiscoveryMode,
            int toolCount,
            string toolsHash,
            string toolDiscoveryReason,
            string toolSnapshotUtc,
            string directCommandHealth,
            string lastCommandSuccessUtc,
            string lastCommandFailureUtc,
            string lastCommandFailureReason,
            string bridgeSessionId,
            long manifestVersion,
            string profileCatalogVersion,
            bool supportsToolSyncLens,
            string lastToolsChangedUtc)
        {
            Status = status;
            Reason = reason;
            ExpectedRecovery = expectedRecovery;
            ExpectedRecoveryExpiresUtc = expectedRecoveryExpiresUtc;
            ToolDiscoveryMode = toolDiscoveryMode;
            ToolCount = toolCount;
            ToolsHash = toolsHash;
            ToolDiscoveryReason = toolDiscoveryReason;
            ToolSnapshotUtc = toolSnapshotUtc;
            DirectCommandHealth = directCommandHealth;
            LastCommandSuccessUtc = lastCommandSuccessUtc;
            LastCommandFailureUtc = lastCommandFailureUtc;
            LastCommandFailureReason = lastCommandFailureReason;
            BridgeSessionId = bridgeSessionId;
            ManifestVersion = manifestVersion;
            ProfileCatalogVersion = profileCatalogVersion;
            SupportsToolSyncLens = supportsToolSyncLens;
            LastToolsChangedUtc = lastToolsChangedUtc;
        }
    }

    static class BridgeStatusTracker
    {
        const double DefaultTransitionTtlSeconds = 10.0;
        const double DefaultEditorReloadTtlSeconds = 15.0;
        static readonly object s_Lock = new();
        static string s_ConnectionPath;
        static string s_Status = "idle";
        static string s_Reason;
        static bool s_ExpectedRecovery;
        static double s_TransitionExpiresAt;
        static string s_ToolDiscoveryMode = "uninitialized";
        static int s_ToolCount = -1;
        static string s_ToolsHash;
        static string s_ToolDiscoveryReason;
        static string s_ToolSnapshotUtc;
        static string s_DirectCommandHealth = "unknown";
        static string s_LastCommandSuccessUtc;
        static string s_LastCommandFailureUtc;
        static string s_LastCommandFailureReason;
        static string s_BridgeSessionId;
        static long s_ManifestVersion;
        static string s_ProfileCatalogVersion;
        static bool s_SupportsToolSyncLens;
        static string s_LastToolsChangedUtc;

        public static void SetConnectionPath(string connectionPath)
        {
            lock (s_Lock)
            {
                s_ConnectionPath = connectionPath;
            }
        }

        public static BridgeStatusSnapshot GetSnapshot()
        {
            lock (s_Lock)
            {
                string expectedRecoveryExpiresUtc = s_ExpectedRecovery && s_TransitionExpiresAt > 0
                    ? DateTime.UtcNow.AddSeconds(Math.Max(0d, s_TransitionExpiresAt - EditorApplication.timeSinceStartup)).ToString("O")
                    : null;

                return new BridgeStatusSnapshot(
                    s_Status,
                    s_Reason,
                    s_ExpectedRecovery,
                    expectedRecoveryExpiresUtc,
                    s_ToolDiscoveryMode,
                    s_ToolCount,
                    s_ToolsHash,
                    s_ToolDiscoveryReason,
                    s_ToolSnapshotUtc,
                    s_DirectCommandHealth,
                    s_LastCommandSuccessUtc,
                    s_LastCommandFailureUtc,
                    s_LastCommandFailureReason,
                    s_BridgeSessionId,
                    s_ManifestVersion,
                    s_ProfileCatalogVersion,
                    s_SupportsToolSyncLens,
                    s_LastToolsChangedUtc);
            }
        }

        public static void SetToolSyncState(
            string bridgeSessionId,
            long manifestVersion,
            string profileCatalogVersion,
            bool supportsToolSyncLens,
            string lastToolsChangedUtc)
        {
            lock (s_Lock)
            {
                s_BridgeSessionId = bridgeSessionId;
                s_ManifestVersion = manifestVersion;
                s_ProfileCatalogVersion = profileCatalogVersion;
                s_SupportsToolSyncLens = supportsToolSyncLens;
                s_LastToolsChangedUtc = lastToolsChangedUtc;
                SaveLocked();
            }
        }

        public static void SetToolDiscoveryState(string mode, int toolCount = -1, string toolsHash = null, string reason = null)
        {
            lock (s_Lock)
            {
                s_ToolDiscoveryMode = string.IsNullOrWhiteSpace(mode) ? "unknown" : mode;
                s_ToolCount = toolCount;
                s_ToolsHash = toolsHash;
                s_ToolDiscoveryReason = reason;
                s_ToolSnapshotUtc = DateTime.UtcNow.ToString("O");
                SaveLocked();
            }
        }

        public static void MarkReady(bool resetCommandHealth = false)
        {
            lock (s_Lock)
            {
                s_Status = "ready";
                s_Reason = null;
                s_ExpectedRecovery = false;
                s_TransitionExpiresAt = 0;
                if (resetCommandHealth)
                {
                    s_DirectCommandHealth = "unknown";
                    s_LastCommandSuccessUtc = null;
                    s_LastCommandFailureUtc = null;
                    s_LastCommandFailureReason = null;
                }
                SaveLocked();
            }
        }

        public static void MarkTransition(string status, string reason, double ttlSeconds = DefaultTransitionTtlSeconds)
        {
            lock (s_Lock)
            {
                s_Status = status;
                s_Reason = reason;
                s_ExpectedRecovery = true;
                s_TransitionExpiresAt = EditorApplication.timeSinceStartup + Math.Max(1.0, ttlSeconds);
                SaveLocked();
            }
        }

        public static void MarkEditorReloading(string reason = "compile_reload", double ttlSeconds = DefaultEditorReloadTtlSeconds)
        {
            lock (s_Lock)
            {
                s_DirectCommandHealth = "recovering";
                s_Status = "editor_reloading";
                s_Reason = reason;
                s_ExpectedRecovery = true;
                s_TransitionExpiresAt = EditorApplication.timeSinceStartup + Math.Max(1.0, ttlSeconds);
                SaveLocked();
            }
        }

        public static void MarkDisconnected(string reason = "disconnected")
        {
            lock (s_Lock)
            {
                s_Status = "disconnected";
                s_Reason = reason;
                s_ExpectedRecovery = false;
                s_TransitionExpiresAt = 0;
                s_DirectCommandHealth = "unknown";
                SaveLocked();
            }
        }

        public static void MarkCommandSuccess()
        {
            lock (s_Lock)
            {
                s_DirectCommandHealth = "ok";
                s_LastCommandSuccessUtc = DateTime.UtcNow.ToString("O");
                s_LastCommandFailureUtc = null;
                s_LastCommandFailureReason = null;
                s_Status = "ready";
                s_Reason = null;
                s_ExpectedRecovery = false;
                s_TransitionExpiresAt = 0;
                SaveLocked();
            }
        }

        public static void MarkCommandFailure(string reason, double ttlSeconds = 15.0)
        {
            lock (s_Lock)
            {
                s_DirectCommandHealth = "failed";
                s_LastCommandFailureUtc = DateTime.UtcNow.ToString("O");
                s_LastCommandFailureReason = string.IsNullOrWhiteSpace(reason) ? "direct_command_failed" : reason;
                s_Status = "transport_recovering";
                s_Reason = s_LastCommandFailureReason;
                s_ExpectedRecovery = true;
                s_TransitionExpiresAt = EditorApplication.timeSinceStartup + Math.Max(1.0, ttlSeconds);
                SaveLocked();
            }
        }

        public static void RefreshHeartbeat()
        {
            lock (s_Lock)
            {
                bool transitionActive = s_ExpectedRecovery && s_TransitionExpiresAt > EditorApplication.timeSinceStartup;
                if (!transitionActive)
                {
                    if (string.Equals(s_DirectCommandHealth, "failed", StringComparison.OrdinalIgnoreCase))
                    {
                        s_Status = "transport_degraded";
                        s_Reason = string.IsNullOrWhiteSpace(s_LastCommandFailureReason) ? "direct_command_failed" : s_LastCommandFailureReason;
                        s_ExpectedRecovery = false;
                        s_TransitionExpiresAt = 0;
                    }
                    else
                    {
                        s_Status = "ready";
                        s_Reason = null;
                        s_ExpectedRecovery = false;
                        s_TransitionExpiresAt = 0;
                    }
                }

                SaveLocked();
            }
        }

        static void SaveLocked()
        {
            if (string.IsNullOrWhiteSpace(s_ConnectionPath))
                return;

            string expectedRecoveryExpiresUtc = s_ExpectedRecovery && s_TransitionExpiresAt > 0
                ? DateTime.UtcNow.AddSeconds(Math.Max(0d, s_TransitionExpiresAt - EditorApplication.timeSinceStartup)).ToString("O")
                : null;

            ServerDiscovery.SaveStatusFile(
                s_ConnectionPath,
                s_Status,
                s_Reason,
                s_ExpectedRecovery,
                s_ToolDiscoveryMode,
                s_ToolCount,
                s_ToolsHash,
                s_ToolDiscoveryReason,
                s_ToolSnapshotUtc,
                s_DirectCommandHealth,
                s_LastCommandSuccessUtc,
                s_LastCommandFailureUtc,
                s_LastCommandFailureReason,
                expectedRecoveryExpiresUtc,
                s_BridgeSessionId,
                s_ManifestVersion,
                s_ProfileCatalogVersion,
                s_SupportsToolSyncLens,
                s_LastToolsChangedUtc);
        }
    }
}
