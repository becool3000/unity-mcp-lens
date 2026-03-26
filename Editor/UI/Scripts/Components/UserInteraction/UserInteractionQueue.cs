using System.Collections.Generic;
using Unity.AI.Assistant.Editor.Utils.Event;
using Unity.AI.Assistant.UI.Editor.Scripts.Data;
using Unity.AI.Assistant.UI.Editor.Scripts.Events;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.UserInteraction
{
    class UserInteractionQueue
    {
        readonly List<UserInteractionEntry> m_Entries = new();
        int m_TotalEnqueued;
        int m_TotalProcessed;

        public int Total => m_TotalEnqueued;
        public int CurrentIndex => m_TotalProcessed + 1;
        public UserInteractionEntry Current => m_Entries.Count > 0 ? m_Entries[0] : null;
        public bool HasPending => m_Entries.Count > 0;

        public void Enqueue(UserInteractionEntry entry)
        {
            var id = UserInteractionId.Next();
            entry.Id = id;
            m_Entries.Add(entry);
            m_TotalEnqueued++;
            AssistantEvents.Send(new EventInteractionQueueChanged());
        }

        public void Complete(UserInteractionEntry entry)
        {
            if (!m_Entries.Remove(entry))
            {
                return;
            }

            m_TotalProcessed++;
            AssistantEvents.Send(new EventInteractionQueueChanged());
        }

        public void CancelAll()
        {
            if (m_Entries.Count == 0)
            {
                return;
            }

            var entries = new List<UserInteractionEntry>(m_Entries);
            m_Entries.Clear();
            m_TotalEnqueued = 0;
            m_TotalProcessed = 0;

            foreach (var entry in entries)
            {
                entry.OnCancel?.Invoke();
            }

            AssistantEvents.Send(new EventInteractionQueueChanged());
        }
    }
}
