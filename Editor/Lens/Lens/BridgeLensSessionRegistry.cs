using System;
using System.Collections.Generic;
using System.Linq;

namespace Becool.UnityMcpLens.Editor.Lens
{
    sealed class BridgeLensClientCapabilities
    {
        public bool SupportsToolSyncLens { get; set; }
        public bool SupportsToolDeltas { get; set; }
        public bool SupportsToolProfiles { get; set; }
        public bool SupportsLazySchemas { get; set; }

        public static BridgeLensClientCapabilities Default => new()
        {
            SupportsToolSyncLens = false,
            SupportsToolDeltas = false,
            SupportsToolProfiles = false,
            SupportsLazySchemas = false
        };
    }

    sealed class BridgeLensConnectionState
    {
        public string ConnectionId { get; set; }
        public string ClientName { get; set; }
        public string ClientVersion { get; set; }
        public string ClientTitle { get; set; }
        public BridgeLensClientCapabilities Capabilities { get; set; } = BridgeLensClientCapabilities.Default;
        public string[] ActiveToolPacks { get; set; } = ToolPackCatalog.DefaultActivePacks;
        public string LastKnownBridgeSessionId { get; set; }
        public long LastKnownManifestVersion { get; set; }
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }

    static class BridgeLensSessionRegistry
    {
        static readonly object s_Lock = new();
        static readonly Dictionary<string, BridgeLensConnectionState> s_States = new(StringComparer.Ordinal);

        public static BridgeLensConnectionState RegisterOrUpdateConnection(
            string connectionId,
            string clientName,
            string clientVersion,
            string clientTitle,
            BridgeLensClientCapabilities capabilities)
        {
            if (string.IsNullOrWhiteSpace(connectionId))
                throw new ArgumentException("Connection ID cannot be empty.", nameof(connectionId));

            lock (s_Lock)
            {
                if (!s_States.TryGetValue(connectionId, out var state))
                {
                    state = new BridgeLensConnectionState
                    {
                        ConnectionId = connectionId,
                        ActiveToolPacks = ToolPackCatalog.DefaultActivePacks
                    };
                    s_States[connectionId] = state;
                }

                state.ClientName = clientName;
                state.ClientVersion = clientVersion;
                state.ClientTitle = clientTitle;
                state.Capabilities = capabilities ?? BridgeLensClientCapabilities.Default;
                state.UpdatedUtc = DateTime.UtcNow;
                return Clone(state);
            }
        }

        public static bool TryGetConnectionState(string connectionId, out BridgeLensConnectionState state)
        {
            lock (s_Lock)
            {
                if (connectionId != null && s_States.TryGetValue(connectionId, out var existing))
                {
                    state = Clone(existing);
                    return true;
                }
            }

            state = null;
            return false;
        }

        public static BridgeLensConnectionState EnsureConnectionState(string connectionId)
        {
            if (string.IsNullOrWhiteSpace(connectionId))
            {
                return new BridgeLensConnectionState
                {
                    ConnectionId = connectionId,
                    ActiveToolPacks = ToolPackCatalog.DefaultActivePacks
                };
            }

            lock (s_Lock)
            {
                if (!s_States.TryGetValue(connectionId, out var state))
                {
                    state = new BridgeLensConnectionState
                    {
                        ConnectionId = connectionId,
                        ActiveToolPacks = ToolPackCatalog.DefaultActivePacks
                    };
                    s_States[connectionId] = state;
                }

                state.UpdatedUtc = DateTime.UtcNow;
                return Clone(state);
            }
        }

        public static bool TrySetActiveToolPacks(string connectionId, IEnumerable<string> requestedPacks, out string[] normalizedPacks, out string error)
        {
            normalizedPacks = Array.Empty<string>();
            error = null;

            if (string.IsNullOrWhiteSpace(connectionId))
            {
                error = "A Lens bridge connection is required before tool packs can be changed.";
                return false;
            }

            if (!ToolPackCatalog.TryNormalizeSelection(requestedPacks, out normalizedPacks, out error))
                return false;

            lock (s_Lock)
            {
                if (!s_States.TryGetValue(connectionId, out var state))
                {
                    state = new BridgeLensConnectionState
                    {
                        ConnectionId = connectionId
                    };
                    s_States[connectionId] = state;
                }

                state.ActiveToolPacks = normalizedPacks;
                state.UpdatedUtc = DateTime.UtcNow;
            }

            return true;
        }

        public static string[] GetActiveToolPacks(string connectionId)
        {
            lock (s_Lock)
            {
                if (connectionId != null && s_States.TryGetValue(connectionId, out var state) && state.ActiveToolPacks?.Length > 0)
                    return state.ActiveToolPacks.ToArray();
            }

            return ToolPackCatalog.DefaultActivePacks;
        }

        public static void UpdateAcknowledgedManifest(string connectionId, string bridgeSessionId, long manifestVersion)
        {
            if (string.IsNullOrWhiteSpace(connectionId))
                return;

            lock (s_Lock)
            {
                if (!s_States.TryGetValue(connectionId, out var state))
                    return;

                state.LastKnownBridgeSessionId = bridgeSessionId;
                state.LastKnownManifestVersion = manifestVersion;
                state.UpdatedUtc = DateTime.UtcNow;
            }
        }

        public static string[] GetToolSyncConnectionIds()
        {
            lock (s_Lock)
            {
                return s_States.Values
                    .Where(state => state.Capabilities?.SupportsToolSyncLens == true)
                    .Select(state => state.ConnectionId)
                    .ToArray();
            }
        }

        public static BridgeLensConnectionState[] GetConnectionStatesSnapshot()
        {
            lock (s_Lock)
            {
                return s_States.Values
                    .Select(Clone)
                    .OrderBy(state => state.ConnectionId, StringComparer.Ordinal)
                    .ToArray();
            }
        }

        public static void ReleaseConnection(string connectionId)
        {
            if (string.IsNullOrWhiteSpace(connectionId))
                return;

            lock (s_Lock)
            {
                s_States.Remove(connectionId);
            }

            ToolDetailRefStore.ReleaseConnection(connectionId);
        }

        static BridgeLensConnectionState Clone(BridgeLensConnectionState state)
        {
            if (state == null)
                return null;

            return new BridgeLensConnectionState
            {
                ConnectionId = state.ConnectionId,
                ClientName = state.ClientName,
                ClientVersion = state.ClientVersion,
                ClientTitle = state.ClientTitle,
                Capabilities = state.Capabilities == null
                    ? BridgeLensClientCapabilities.Default
                    : new BridgeLensClientCapabilities
                    {
                        SupportsToolSyncLens = state.Capabilities.SupportsToolSyncLens,
                        SupportsToolDeltas = state.Capabilities.SupportsToolDeltas,
                        SupportsToolProfiles = state.Capabilities.SupportsToolProfiles,
                        SupportsLazySchemas = state.Capabilities.SupportsLazySchemas
                    },
                ActiveToolPacks = state.ActiveToolPacks?.ToArray() ?? ToolPackCatalog.DefaultActivePacks,
                LastKnownBridgeSessionId = state.LastKnownBridgeSessionId,
                LastKnownManifestVersion = state.LastKnownManifestVersion,
                UpdatedUtc = state.UpdatedUtc
            };
        }
    }
}
