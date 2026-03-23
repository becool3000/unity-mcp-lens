using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Editor.Settings;
using UnityEditor;

namespace Unity.Relay.Editor
{
    /// <summary>
    /// Handles persistence requests from the Relay server via the relay bus.
    /// The Relay calls PersistenceLoad/PersistenceSave methods to read/write EditorPrefs.
    /// </summary>
    class RelayPersistenceService : IDisposable
    {
        const string k_KeyPrefix = "Unity.AI.Gateway.Relay.";

        static RelayPersistenceService s_Instance;

        bool m_Disposed;

        [InitializeOnLoadMethod]
        static void InitializeOnLoad() => _ = Instance;

        public static RelayPersistenceService Instance => s_Instance ??= new RelayPersistenceService();

        RelayPersistenceService()
        {
            // Bus is long-lived — register handlers once (Handle replaces per channel, so this is idempotent)
            var bus = RelayService.Instance.Bus;
            bus.Handle(
                RelayChannels.PersistenceLoad,
                HandleLoadAsync);
            bus.Handle(
                RelayChannels.PersistenceSave,
                HandleSaveAsync);
        }

        static Task<PersistenceLoadResponse> HandleLoadAsync(PersistenceLoadRequest request)
        {
            if (string.IsNullOrEmpty(request?.Key))
                return Task.FromResult(new PersistenceLoadResponse(false, Error: "Key is required"));

            var fullKey = k_KeyPrefix + request.Key;
            var exists = EditorPrefs.HasKey(fullKey);
            var value = exists ? JToken.Parse(EditorPrefs.GetString(fullKey)) : null;

            return Task.FromResult(new PersistenceLoadResponse(true, value, exists));
        }

        static Task<PersistenceSaveResponse> HandleSaveAsync(PersistenceSaveRequest request)
        {
            if (string.IsNullOrEmpty(request?.Key))
                return Task.FromResult(new PersistenceSaveResponse(false, "Key is required"));

            var fullKey = k_KeyPrefix + request.Key;

            if (request.Value == null || request.Value.Type == JTokenType.Null)
            {
                EditorPrefs.DeleteKey(fullKey);
            }
            else
            {
                EditorPrefs.SetString(fullKey, request.Value.ToString(Newtonsoft.Json.Formatting.None));
            }

            return Task.FromResult(new PersistenceSaveResponse(true));
        }

        public static void ClearPreferences()
        {
            var key = k_KeyPrefix + "envVars.preferences";
            if (EditorPrefs.HasKey(key))
            {
                EditorPrefs.DeleteKey(key);
                GatewayPreferenceService.Instance.Preferences.Refresh();
            }
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;

            m_Disposed = true;
        }
    }
}
