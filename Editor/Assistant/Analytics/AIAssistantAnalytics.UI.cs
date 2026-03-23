using System;
using Unity.AI.Assistant.Bridge.Editor;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using UnityEditor;
using UnityEngine.Analytics;

namespace Unity.AI.Assistant.Editor.Analytics
{
    internal enum UITriggerBackendEventSubType
    {
        FavoriteConversation,
        DeleteConversation,
        RenameConversation,
        LoadConversation,
        CancelRequest,
        CreateNewConversation
    }

    internal enum ContextSubType
    {
        PingAttachedContextObjectFromFlyout,
        ClearAllAttachedContext,
        RemoveSingleAttachedContext,
        DragDropAttachedContext,
        ChooseContextFromFlyout
    }

    internal enum UITriggerLocalEventSubType
    {
        OpenReferenceUrl,
        SaveCode,
        CopyCode,
        CopyResponse,
        ExpandCommandLogic,
        PermissionResponse
    }

    internal static partial class AIAssistantAnalytics
    {
        #region Remote UI Events

        internal const string k_UITriggerBackendEvent = "AIAssistantUITriggerBackendEvent";

        [Serializable]
        internal class UITriggerBackendEventData : IAnalytic.IData
        {
            public UITriggerBackendEventData(UITriggerBackendEventSubType subType) => SubType = subType.ToString();

            public string SubType;
            public string ConversationId;
            public string MessageId;
            public string ResponseMessage;
            public string ConversationTitle;
            public string IsFavorite;
        }

        [AnalyticInfo(eventName: k_UITriggerBackendEvent, vendorKey: k_VendorKey)]
        class UITriggerBackendEvent : IAnalytic
        {
            private readonly UITriggerBackendEventData m_Data;

            public UITriggerBackendEvent(UITriggerBackendEventData data)
            {
                m_Data = data;
            }

            public bool TryGatherData(out IAnalytic.IData data, out Exception error)
            {
                error = null;
                data = m_Data;
                return true;
            }
        }

        static void ReportUITriggerBackendEvent(UITriggerBackendEventData data)
        {
            EditorAnalytics.SendAnalytic(new UITriggerBackendEvent(data));
        }

        internal static void ReportUITriggerBackendCreateNewConversationEvent()
        {
            ReportUITriggerBackendEvent(new UITriggerBackendEventData(UITriggerBackendEventSubType.CreateNewConversation));
        }

        internal static void ReportUITriggerBackendCancelRequestEvent(AssistantConversationId conversationId)
        {
            ReportUITriggerBackendEvent(new UITriggerBackendEventData(UITriggerBackendEventSubType.CancelRequest)
            {
                ConversationId = conversationId.Value,
            });
        }

        internal static void ReportUITriggerBackendLoadConversationEvent(AssistantConversationId conversationId, string title)
        {
            ReportUITriggerBackendEvent(new UITriggerBackendEventData(UITriggerBackendEventSubType.LoadConversation)
            {
                ConversationId = conversationId.Value,
                ConversationTitle = title,
            });
        }

        internal static void ReportUITriggerBackendFavoriteConversationEvent(AssistantConversationId conversationId, string title, bool isFavorited)
        {
            ReportUITriggerBackendEvent(new UITriggerBackendEventData(UITriggerBackendEventSubType.FavoriteConversation)
            {
                ConversationId = conversationId.Value,
                ConversationTitle = title,
                IsFavorite = isFavorited.ToString(),
            });
        }

        internal static void ReportUITriggerBackendDeleteConversationEvent(AssistantConversationId conversationId, string title)
        {
            ReportUITriggerBackendEvent(new UITriggerBackendEventData(UITriggerBackendEventSubType.DeleteConversation)
            {
                ConversationId = conversationId.Value,
                ConversationTitle = title,
            });
        }

        internal static void ReportUITriggerBackendRenameConversationEvent(AssistantConversationId conversationId, string newTitle)
        {
            ReportUITriggerBackendEvent(new UITriggerBackendEventData(UITriggerBackendEventSubType.RenameConversation)
            {
                ConversationId = conversationId.Value,
                ConversationTitle = newTitle,
            });
        }

        #endregion

        #region Context Events

        internal const string k_ContextEvent = "AIAssistantContextEvent";

        [Serializable]
        internal class ContextEventData : IAnalytic.IData
        {
            public ContextEventData(ContextSubType subType) => SubType = subType.ToString();

            public string SubType;
            public string ContextContent;
            public string ContextType;
            public string IsSuccessful;
        }

        // Context Group
        [AnalyticInfo(eventName: k_ContextEvent, vendorKey: k_VendorKey)]
        class ContextEvent : IAnalytic
        {
            private readonly ContextEventData m_Data;

            public ContextEvent(ContextEventData data)
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

        static void ReportContextEvent(ContextEventData data)
        {
            EditorAnalytics.SendAnalytic(new ContextEvent(data));
        }

        internal static void ReportContextRemoveSingleAttachedContextEvent(AssistantContextEntry contextEntry)
        {
            ReportContextEvent(new ContextEventData(ContextSubType.RemoveSingleAttachedContext)
            {
                ContextContent = contextEntry.DisplayValue,
                ContextType = contextEntry.EntryType.ToString(),
            });
        }

        internal static void ReportContextDragDropAttachedContextEvent(UnityEngine.Object obj)
        {
            ReportContextEvent(new ContextEventData(ContextSubType.DragDropAttachedContext)
            {
                ContextContent = obj.name,
                ContextType = obj.GetType().Name,
                IsSuccessful = "false",
            });
        }

        internal static void ReportContextDragDropAttachedContextEvent(AssistantContextEntry contextEntry)
        {
            ReportContextEvent(new ContextEventData(ContextSubType.DragDropAttachedContext)
            {
                ContextContent = contextEntry.DisplayValue,
                ContextType = contextEntry.ValueType,
                IsSuccessful = "true",
            });
        }

        internal static void ReportContextClearAllAttachedContextEvent()
        {
            ReportContextEvent(new ContextEventData(ContextSubType.ClearAllAttachedContext));
        }

        internal static void ReportContextChooseContextFromFlyoutEvent(LogData logData)
        {
            ReportContextEvent(new ContextEventData(ContextSubType.ChooseContextFromFlyout)
            {
                ContextContent = logData.Message,
                ContextType = "LogData",
            });
        }

        internal static void ReportContextChooseContextFromFlyoutEvent(UnityEngine.Object obj)
        {
            ReportContextEvent(new ContextEventData(ContextSubType.ChooseContextFromFlyout)
            {
                ContextContent = obj.name,
                ContextType = obj.GetType().ToString(),
            });
        }

        internal static void ReportContextPingAttachedContextObjectFromFlyoutEvent(UnityEngine.Object obj)
        {
            ReportContextEvent(new ContextEventData(ContextSubType.PingAttachedContextObjectFromFlyout)
            {
                ContextContent = obj.name,
                ContextType = obj.GetType().ToString(),
            });
        }

        #endregion

        #region Local UI Events

        internal const string k_UITriggerLocalEvent = "AIAssistantUITriggerLocalEvent";

        [Serializable]
        internal class UITriggerLocalEventData : IAnalytic.IData
        {
            public UITriggerLocalEventData(UITriggerLocalEventSubType subType) => SubType = subType.ToString();

            public string SubType;
            public string UsedInspirationalPrompt;
            public string ChosenMode;
            public string ReferenceUrl;
            public string ConversationId;
            public string MessageId;
            public string ResponseMessage;
            public string PreviewParameter;
            public string FunctionId;
            public string UserAnswer;
            public string PermissionType;
        }

        [AnalyticInfo(eventName: k_UITriggerLocalEvent, vendorKey: k_VendorKey)]
        class UITriggerLocalEvent : IAnalytic
        {
            private readonly UITriggerLocalEventData m_Data;

            public UITriggerLocalEvent(UITriggerLocalEventData data)
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

        static void ReportUITriggerLocalEvent(UITriggerLocalEventData data)
        {
            EditorAnalytics.SendAnalytic(new UITriggerLocalEvent(data));
        }

        internal static void ReportUITriggerLocalCopyResponseEvent(AssistantMessageId messageId, string responseMessage)
        {
            ReportUITriggerLocalEvent(new UITriggerLocalEventData(UITriggerLocalEventSubType.CopyResponse)
            {
                ConversationId = messageId.ConversationId.Value,
                MessageId = messageId.FragmentId,
                ResponseMessage = responseMessage,
            });
        }

        internal static void ReportUITriggerLocalCopyCodeEvent(AssistantConversationId? conversationId, string responseMessage)
        {
            ReportUITriggerLocalEvent(new UITriggerLocalEventData(UITriggerLocalEventSubType.CopyCode)
            {
                ConversationId = conversationId?.Value ?? string.Empty,
                ResponseMessage = responseMessage,
            });
        }

        internal static void ReportUITriggerLocalSaveCodeEvent(AssistantConversationId? conversationId, string responseMessage)
        {
            ReportUITriggerLocalEvent(new UITriggerLocalEventData(UITriggerLocalEventSubType.SaveCode)
            {
                ConversationId = conversationId?.Value ?? string.Empty,
                ResponseMessage = responseMessage,
            });
        }

        internal static void ReportUITriggerLocalExpandCommandLogicEvent()
        {
            ReportUITriggerLocalEvent(new UITriggerLocalEventData(UITriggerLocalEventSubType.ExpandCommandLogic));
        }

        internal static void ReportUITriggerLocalPermissionResponseEvent(AssistantConversationId conversationId, ToolExecutionContext.CallInfo callInfo, ToolPermissions.UserAnswer answer, ToolPermissions.PermissionType permissionType)
        {
            ReportUITriggerLocalEvent(new UITriggerLocalEventData(UITriggerLocalEventSubType.PermissionResponse)
            {
                ConversationId = conversationId.Value,
                FunctionId = callInfo.FunctionId ?? string.Empty,
                UserAnswer = answer.ToString(),
                PermissionType = permissionType.ToString(),
            });
        }

        internal static void ReportUITriggerLocalOpenReferenceUrlEvent(string referenceUrl)
        {
            ReportUITriggerLocalEvent(new UITriggerLocalEventData(UITriggerLocalEventSubType.OpenReferenceUrl)
            {
                ReferenceUrl = referenceUrl,
            });
        }

        #endregion
    }
}
