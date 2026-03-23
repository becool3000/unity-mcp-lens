using System;
using UnityEditor;

namespace Unity.AI.MCP.Editor.Helpers
{
    readonly struct BridgeStatusSnapshot
    {
        public readonly string Status;
        public readonly string Reason;
        public readonly bool ExpectedRecovery;
        public readonly string ToolDiscoveryMode;
        public readonly int ToolCount;
        public readonly string ToolsHash;
        public readonly string ToolDiscoveryReason;
        public readonly string ToolSnapshotUtc;

        public BridgeStatusSnapshot(
            string status,
            string reason,
            bool expectedRecovery,
            string toolDiscoveryMode,
            int toolCount,
            string toolsHash,
            string toolDiscoveryReason,
            string toolSnapshotUtc)
        {
            Status = status;
            Reason = reason;
            ExpectedRecovery = expectedRecovery;
            ToolDiscoveryMode = toolDiscoveryMode;
            ToolCount = toolCount;
            ToolsHash = toolsHash;
            ToolDiscoveryReason = toolDiscoveryReason;
            ToolSnapshotUtc = toolSnapshotUtc;
        }
    }

    static class BridgeStatusTracker
    {
        const double DefaultTransitionTtlSeconds = 10.0;
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
                return new BridgeStatusSnapshot(
                    s_Status,
                    s_Reason,
                    s_ExpectedRecovery,
                    s_ToolDiscoveryMode,
                    s_ToolCount,
                    s_ToolsHash,
                    s_ToolDiscoveryReason,
                    s_ToolSnapshotUtc);
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

        public static void MarkReady()
        {
            lock (s_Lock)
            {
                s_Status = "ready";
                s_Reason = null;
                s_ExpectedRecovery = false;
                s_TransitionExpiresAt = 0;
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

        public static void MarkEditorReloading(string reason = "compile_reload", double ttlSeconds = 60.0) =>
            MarkTransition("editor_reloading", reason, ttlSeconds);

        public static void MarkDisconnected(string reason = "disconnected")
        {
            lock (s_Lock)
            {
                s_Status = "disconnected";
                s_Reason = reason;
                s_ExpectedRecovery = false;
                s_TransitionExpiresAt = 0;
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
                    s_Status = "ready";
                    s_Reason = null;
                    s_ExpectedRecovery = false;
                    s_TransitionExpiresAt = 0;
                }

                SaveLocked();
            }
        }

        static void SaveLocked()
        {
            if (string.IsNullOrWhiteSpace(s_ConnectionPath))
                return;

            ServerDiscovery.SaveStatusFile(
                s_ConnectionPath,
                s_Status,
                s_Reason,
                s_ExpectedRecovery,
                s_ToolDiscoveryMode,
                s_ToolCount,
                s_ToolsHash,
                s_ToolDiscoveryReason,
                s_ToolSnapshotUtc);
        }
    }
}
