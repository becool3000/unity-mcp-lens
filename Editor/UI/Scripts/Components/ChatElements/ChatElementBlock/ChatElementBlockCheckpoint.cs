using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.UI.Editor.Scripts.Data;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class ChatElementBlockCheckpoint : ChatElementBase
    {
        ChatElementCheckpoint m_Checkpoint;

        AssistantMessageId m_MessageId;

        protected override void InitializeView(TemplateContainer view)
        {
            m_Checkpoint = view.Q<ChatElementCheckpoint>(); 
            m_Checkpoint.ShowCheckpointLabel = true;
            m_Checkpoint.Initialize(Context);

            m_Checkpoint.SetCheckpointData(m_MessageId);
        }

        public override void SetData(MessageModel message)
        {
            m_MessageId = message.Id;

            var isValid = false;
            if (m_Checkpoint != null)
            {
                m_Checkpoint.SetCheckpointData(m_MessageId);
                m_Checkpoint.OnValidated -= CheckpointValidated;
                m_Checkpoint.OnValidated += CheckpointValidated;
                
                isValid = m_Checkpoint.CheckpointValid;
            }
            
            this.SetDisplay(isValid);
        }

        void CheckpointValidated()
        {
            this.SetDisplay(true);
        }
    }
}
