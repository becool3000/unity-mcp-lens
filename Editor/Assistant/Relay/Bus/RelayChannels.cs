using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Editor.Settings;

namespace Unity.Relay
{
    /// <summary>
    /// All relay bus channel definitions. Channel names are auto-derived from field names
    /// using PascalCase → dot.case convention (e.g., PersistenceLoad → "persistence.load").
    /// Use an explicit name in the constructor to override the convention.
    /// </summary>
    static class RelayChannels
    {
        // ── Methods ──

        /// <summary>Ping the remote side and get a pong response.</summary>
        public static readonly RelayMethod<PingRequest, PongResponse> Ping = new("ping");

        /// <summary>Load a value from EditorPrefs (Relay → Unity).</summary>
        public static readonly RelayMethod<PersistenceLoadRequest, PersistenceLoadResponse> PersistenceLoad = new();

        /// <summary>Save a value to EditorPrefs (Relay → Unity).</summary>
        public static readonly RelayMethod<PersistenceSaveRequest, PersistenceSaveResponse> PersistenceSave = new();

        /// <summary>Reveal a credential value by reading directly from keytar (Unity → Relay).</summary>
        public static readonly RelayMethod<CredentialRevealRequest, CredentialRevealResponse> CredentialReveal = new();

        /// <summary>Get gateway preferences (Unity → Relay). Shared: concurrent callers reuse in-flight request.</summary>
        public static readonly RelayMethod<PreferencesGetRequest, PreferencesData> PreferencesGet = new(MethodBehavior.Shared);

        /// <summary>Save gateway preferences (Unity → Relay).</summary>
        public static readonly RelayMethod<PreferencesData, SetPreferencesResult> PreferencesSet = new();

        /// <summary>Reset gateway preferences to system defaults and re-seed from shell env (Unity → Relay).</summary>
        public static readonly RelayMethod<PreferencesResetRequest, PreferencesData> PreferencesReset = new();

        /// <summary>Post analytics data via the relay (Unity → Relay).</summary>
        public static readonly RelayMethod<AnalyticsPostRequest, AnalyticsPostResponse> AnalyticsPost = new();

        // ── Events ──

        /// <summary>Async preferences update notification (Relay → Unity). Sent after version fetch completes.</summary>
        public static readonly RelayEvent<PreferencesData> PreferencesUpdated = new();

        /// <summary>MCP session token registration (Relay → Unity).</summary>
        public static readonly RelayEvent<McpSessionRegistration> McpSessionRegister = new();

        /// <summary>MCP session token unregistration (Relay → Unity).</summary>
        public static readonly RelayEvent<McpSessionUnregistration> McpSessionUnregister = new();

        // Channel definitions are added here as messages are migrated to the bus.

        static RelayChannels()
        {
            ChannelNaming.AutoName(typeof(RelayChannels));
        }
    }

    // ── Ping ──
    record PingRequest;
    record PongResponse(string Timestamp);

    // ── Persistence ──
    record PersistenceLoadRequest(string Key);
    record PersistenceLoadResponse(bool Success, JToken Value = null, bool Exists = false, string Error = null);
    record PersistenceSaveRequest(string Key, JToken Value);
    record PersistenceSaveResponse(bool Success, string Error = null);

    // ── Credentials ──
    record CredentialRevealRequest(string AgentType, string Name);
    record CredentialRevealResponse(bool Success, string Value = null, string Error = null);

    // ── Preferences ──
    record PreferencesGetRequest;
    record PreferencesResetRequest;
    record SetPreferencesResult(bool CredentialsUpdated);

    // ── MCP Session ──
    record McpSessionRegistration(string SessionId, string Token, string Provider = null)
    {
        public bool IsValid => !string.IsNullOrEmpty(SessionId) && !string.IsNullOrEmpty(Token);
    }

    record McpSessionUnregistration(string SessionId);

    // ── Analytics ──
    record AnalyticsPostRequest(string Url, Dictionary<string, string> Headers, string Body);
    record AnalyticsPostResponse(bool Success, int StatusCode = 0, string Error = null);
}
