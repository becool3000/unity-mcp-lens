using System.Collections.Generic;
using Unity.AI.Assistant.Editor.Acp;
using Unity.AI.Assistant.UI.Editor.Scripts.Data.MessageBlocks;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class ChatElementBlockPlan : ChatElementBlockBase<AcpPlanBlockModel>
    {
        readonly List<Label> m_EntryLabels = new();
        VisualElement m_EntriesContainer;

        protected override void InitializeView(TemplateContainer view)
        {
            base.InitializeView(view);
            m_EntriesContainer = view.Q("planEntries");
        }

        protected override void OnBlockModelChanged()
        {
            RefreshContent();
        }

        void RefreshContent()
        {
            var entries = BlockModel.Entries;

            // Create or reuse labels for each entry
            for (int i = 0; i < entries.Count; i++)
            {
                Label label;
                if (i >= m_EntryLabels.Count)
                {
                    label = new Label();
                    label.selection.isSelectable = true;
                    label.AddToClassList("mui-chat-plan-entry");
                    m_EntryLabels.Add(label);
                    m_EntriesContainer.Add(label);
                }
                else
                {
                    label = m_EntryLabels[i];
                }

                label.text = FormatEntry(entries[i]);
            }

            // Hide excess labels
            for (int i = entries.Count; i < m_EntryLabels.Count; i++)
            {
                m_EntryLabels[i].style.display = DisplayStyle.None;
            }

            // Show used labels
            for (int i = 0; i < entries.Count; i++)
            {
                m_EntryLabels[i].style.display = DisplayStyle.Flex;
            }
        }

        static string FormatEntry(AcpPlanEntry entry)
        {
            return $"{entry.StatusIcon} {entry.Content}{entry.PriorityDisplay}";
        }
    }
}
