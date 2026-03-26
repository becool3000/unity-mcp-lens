using System.IO;
using JetBrains.Annotations;
using Unity.AI.Assistant.UI.Editor.Scripts.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    [UsedImplicitly]
    class ChatElementWrapper : ManagedListEntry
    {
        VisualElement m_Root;
        ChatElementBase m_ChatElement;

        MessageModel m_Message;

        protected override void InitializeView(TemplateContainer view)
        {
            m_Root = view.Q<VisualElement>("wrapperRoot");
        }

        public override void SetData(int index, object data, bool isSelected = false)
        {
            base.SetData(index, data);

            m_Message = (MessageModel)data;

            // Ensure items that were visible, get refreshed when data changes:
            if (DidComeIntoView)
            {
                DidComeIntoView = false;
                CameIntoView();
            }
        }

        public override bool CameIntoView()
        {
            if (DidComeIntoView)
                return false;

            DidComeIntoView = true;
            SetupChatElement(ref m_ChatElement, m_Message);

            // We have a minimum height to limit the amount of elements getting created.
            // Set the minHeight to 0 after the first frame, to allow the element to shrink if needed:
            schedule.Execute(() =>
            {
                style.minHeight = 0;
            });

            return true;
        }

        void SetupChatElement(ref ChatElementBase element, MessageModel message)
        {
            if (element == null)
            {
                // TODO-checkpoint: workaround / proof-of-concept to add other elements than prompt/response
                if (message.IsRevertedTimeStampLink)
                {
                    // Create the separator link (includes wrapper with margins from UXML)
                    var link = new ChatElementBlockRevertedMessagesLink();
                    link.SetTimestamp(message.RevertedTimeStamp);
                    element = link;
                }
                else if (message.IsInitialCheckpoint)
                {
                    var initialCheckpoint = new ChatElementBlockCheckpoint();
                    initialCheckpoint.SetData(message);
                    element = initialCheckpoint;
                }
                else
                {
                    element = message.Role switch
                    {
                        MessageModelRole.User => new ChatElementUser(),
                        MessageModelRole.Error or MessageModelRole.Assistant => new ChatElementResponse(),
                        _ => throw new InvalidDataException("Unsupported Role: " + message.Role)
                    };
                }

                element.Initialize(Context);
            }
            element.SetData(message);

            m_Root.Add(element);
        }
    }
}
