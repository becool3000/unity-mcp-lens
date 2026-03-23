using System;
using UnityEditor;
using UnityEngine.Analytics;

namespace Unity.AI.Assistant.Editor.Analytics
{
    internal static partial class AIAssistantAnalytics
    {
        const string k_VendorKey = "unity.ai.assistant";
        const string k_SendMessageEvent = "AIAssistantSendUserMessageEvent";

        #region SendMessageEvent

        [Serializable]
        internal class SendUserMessageEventData : IAnalytic.IData
        {
            public string userPrompt;
            public string commandMode;
            public string conversationId;
        }

        [AnalyticInfo(eventName: k_SendMessageEvent, vendorKey: k_VendorKey)]
        class SendUserMessageEvent : IAnalytic
        {
            readonly SendUserMessageEventData m_Data;

            public SendUserMessageEvent(SendUserMessageEventData data)
            {
                m_Data = data;
            }

            public bool TryGatherData(out IAnalytic.IData data, out Exception error)
            {
                data = m_Data;
                error = null;
                return true;
            }
        }

        internal static void ReportSendUserMessageEvent(string userPrompt, string conversationId)
        {
            var data = new SendUserMessageEventData
            {
                userPrompt = userPrompt,
                commandMode = string.Empty,
                conversationId = conversationId
            };

            EditorAnalytics.SendAnalytic(new SendUserMessageEvent(data));
        }

        #endregion
    }
}
