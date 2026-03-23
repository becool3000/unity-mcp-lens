using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor
{
    static class AssistantEnvironment
    {
        internal const string k_DefaultApiUrl = "https://api-beta-v2.prd.azure.muse.unity.com";
        internal const string k_DefaultWebSocketApiUrl = "wss://api-beta-v2.prd.azure.muse.unity.com/v1/assistant/ws";

       public static string ApiUrl = k_DefaultApiUrl;
       public static string WebSocketApiUrl = k_DefaultWebSocketApiUrl;
       public static bool DebugModeEnabled;

        internal static void SetApi(string apiUrl)
        {
            ApiUrl = apiUrl;
        }

        internal static void SetWebSocketApi(string apiUrl)
        {
            WebSocketApiUrl = apiUrl;
        }

        internal static void Reset()
        {
            ApiUrl = k_DefaultApiUrl;
            WebSocketApiUrl = k_DefaultWebSocketApiUrl;
            DebugModeEnabled = false;
        }
    }
}
