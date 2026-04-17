using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.AI.MCP.Editor.Lens
{
    sealed class StoredDetailPayload
    {
        public string refId { get; set; }
        public string contentType { get; set; }
        public object payload { get; set; }
        public object meta { get; set; }
        public string createdUtc { get; set; }
    }

    static class ToolDetailRefStore
    {
        const int MaxEntriesPerConnection = 64;
        const int MaxGlobalEntries = 128;
        static readonly TimeSpan GlobalEntryTtl = TimeSpan.FromMinutes(15);

        sealed class ConnectionStore
        {
            public readonly Dictionary<string, StoredDetailPayload> Items = new(StringComparer.Ordinal);
            public readonly Queue<string> Order = new();
        }

        static readonly object s_Lock = new();
        static readonly Dictionary<string, ConnectionStore> s_Stores = new(StringComparer.Ordinal);
        static readonly Dictionary<string, StoredDetailPayload> s_GlobalItems = new(StringComparer.Ordinal);
        static readonly Queue<string> s_GlobalOrder = new();

        public static string Store(string connectionId, object payload, string contentType = "application/json", object meta = null)
        {
            if (string.IsNullOrWhiteSpace(connectionId))
                throw new ArgumentException("A connection ID is required to store a detail ref.", nameof(connectionId));

            var now = DateTime.UtcNow;
            var refId = $"detail_{Guid.NewGuid():N}";
            var detailPayload = new StoredDetailPayload
            {
                refId = refId,
                contentType = contentType,
                payload = payload,
                meta = meta,
                createdUtc = now.ToString("O")
            };

            lock (s_Lock)
            {
                PruneGlobalLocked(now);

                if (!s_Stores.TryGetValue(connectionId, out var store))
                {
                    store = new ConnectionStore();
                    s_Stores[connectionId] = store;
                }

                store.Items[refId] = detailPayload;
                store.Order.Enqueue(refId);
                s_GlobalItems[refId] = detailPayload;
                s_GlobalOrder.Enqueue(refId);

                while (store.Order.Count > MaxEntriesPerConnection)
                {
                    var staleRefId = store.Order.Dequeue();
                    store.Items.Remove(staleRefId);
                }

                while (s_GlobalOrder.Count > MaxGlobalEntries)
                {
                    var staleRefId = s_GlobalOrder.Dequeue();
                    s_GlobalItems.Remove(staleRefId);
                }
            }

            return refId;
        }

        public static bool TryRead(string connectionId, string refId, out StoredDetailPayload payload)
        {
            lock (s_Lock)
            {
                PruneGlobalLocked(DateTime.UtcNow);

                if (!string.IsNullOrWhiteSpace(connectionId) &&
                    !string.IsNullOrWhiteSpace(refId) &&
                    s_Stores.TryGetValue(connectionId, out var store) &&
                    store.Items.TryGetValue(refId, out payload))
                {
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(refId) &&
                    s_GlobalItems.TryGetValue(refId, out payload))
                {
                    return true;
                }
            }

            payload = null;
            return false;
        }

        public static string[] GetStoredRefIds(string connectionId)
        {
            lock (s_Lock)
            {
                PruneGlobalLocked(DateTime.UtcNow);

                if (!string.IsNullOrWhiteSpace(connectionId) && s_Stores.TryGetValue(connectionId, out var store))
                {
                    return store.Items.Keys
                        .Concat(s_GlobalItems.Keys)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(key => key, StringComparer.Ordinal)
                        .ToArray();
                }

                return s_GlobalItems.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
            }
        }

        public static void ReleaseConnection(string connectionId)
        {
            if (string.IsNullOrWhiteSpace(connectionId))
                return;

            lock (s_Lock)
            {
                s_Stores.Remove(connectionId);
            }
        }

        static void PruneGlobalLocked(DateTime nowUtc)
        {
            while (s_GlobalOrder.Count > 0)
            {
                var refId = s_GlobalOrder.Peek();
                if (!s_GlobalItems.TryGetValue(refId, out var payload))
                {
                    s_GlobalOrder.Dequeue();
                    continue;
                }

                if (!DateTime.TryParse(payload.createdUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var createdUtc) ||
                    nowUtc - createdUtc > GlobalEntryTtl)
                {
                    s_GlobalOrder.Dequeue();
                    s_GlobalItems.Remove(refId);
                    continue;
                }

                break;
            }
        }
    }
}
