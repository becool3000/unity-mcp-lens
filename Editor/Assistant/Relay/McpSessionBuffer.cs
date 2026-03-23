using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Unity.Relay;
using UnityEditor;

namespace Unity.Relay.Editor
{
    /// <summary>
    /// Buffers MCP session registrations from RelayService to handle late subscribers.
    /// This solves the race condition where Bridge subscribes after registrations are sent.
    ///
    /// The problem: After domain reload, RelayService.Init() runs and reconnects to Relay.
    /// Relay sends RELAY_MCP_SESSION_REGISTER immediately on reconnect. But Bridge.Init()
    /// may run later (order not guaranteed), so it misses the registration event.
    ///
    /// The solution: This buffer subscribes to MCP session events via its own [InitializeOnLoadMethod]
    /// and stores all registrations. Late subscribers can call GetAll() to catch missed registrations.
    /// </summary>
    static class McpSessionBuffer
    {
        static readonly ConcurrentDictionary<string, McpSessionRegistration> s_Sessions = new();
        static bool s_Subscribed;

        [InitializeOnLoadMethod]
        static void Initialize()
        {
            if (s_Subscribed) return;

            RelayService.Instance.OnMcpSessionRegister += OnRegister;
            RelayService.Instance.OnMcpSessionUnregister += OnUnregister;
            s_Subscribed = true;
        }

        static void OnRegister(McpSessionRegistration registration)
        {
            s_Sessions[registration.SessionId] = registration;
        }

        static void OnUnregister(string sessionId)
        {
            s_Sessions.TryRemove(sessionId, out _);
        }

        /// <summary>
        /// Get all currently registered MCP sessions.
        /// Used by late subscribers to catch registrations they missed.
        /// </summary>
        public static IReadOnlyList<McpSessionRegistration> GetAll()
        {
            return s_Sessions.Values.ToList();
        }
    }
}

