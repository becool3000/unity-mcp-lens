using Unity.AI.Assistant.Editor.Utils.Event;
using Unity.AI.Assistant.UI.Editor.Scripts.Data;
using Unity.AI.Assistant.UI.Editor.Scripts.Events;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class ChatElementBlockRevertedMessagesLink : ChatElementBase
    {
        long m_TimeStamp = -1;

        protected override void InitializeView(TemplateContainer view)
        {
            view.SetupButton("checkpointFilterSection", OnFilterClicked);
        }

        public override void SetData(MessageModel message)
        {
            // Unused
        }

        public void SetTimestamp(long timestamp)
        {
            m_TimeStamp = timestamp;
        }

        void OnFilterClicked(PointerUpEvent evt)
        {
            AssistantEvents.Send(new EventRevertedTimeStampFilterRequested(m_TimeStamp));
        }
    }
}
