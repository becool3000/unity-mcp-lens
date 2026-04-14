using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.AI.MCP.Editor.VNext
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

        sealed class ConnectionStore
        {
            public readonly Dictionary<string, StoredDetailPayload> Items = new(StringComparer.Ordinal);
            public readonly Queue<string> Order = new();
        }

        static readonly object s_Lock = new();
        static readonly Dictionary<string, ConnectionStore> s_Stores = new(StringComparer.Ordinal);

        public static string Store(string connectionId, object payload, string contentType = "application/json", object meta = null)
        {
            if (string.IsNullOrWhiteSpace(connectionId))
                throw new ArgumentException("A connection ID is required to store a detail ref.", nameof(connectionId));

            var refId = $"detail_{Guid.NewGuid():N}";
            var detailPayload = new StoredDetailPayload
            {
                refId = refId,
                contentType = contentType,
                payload = payload,
                meta = meta,
                createdUtc = DateTime.UtcNow.ToString("O")
            };

            lock (s_Lock)
            {
                if (!s_Stores.TryGetValue(connectionId, out var store))
                {
                    store = new ConnectionStore();
                    s_Stores[connectionId] = store;
                }

                store.Items[refId] = detailPayload;
                store.Order.Enqueue(refId);

                while (store.Order.Count > MaxEntriesPerConnection)
                {
                    var staleRefId = store.Order.Dequeue();
                    store.Items.Remove(staleRefId);
                }
            }

            return refId;
        }

        public static bool TryRead(string connectionId, string refId, out StoredDetailPayload payload)
        {
            lock (s_Lock)
            {
                if (!string.IsNullOrWhiteSpace(connectionId) &&
                    !string.IsNullOrWhiteSpace(refId) &&
                    s_Stores.TryGetValue(connectionId, out var store) &&
                    store.Items.TryGetValue(refId, out payload))
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
                if (!string.IsNullOrWhiteSpace(connectionId) && s_Stores.TryGetValue(connectionId, out var store))
                    return store.Items.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
            }

            return Array.Empty<string>();
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
    }
}
