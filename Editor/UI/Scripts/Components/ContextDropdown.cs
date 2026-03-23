using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Unity.AI.Assistant.Data;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    class ContextDropdown: ManagedTemplate
    {
        public ContextDropdown()
            : base(AssistantUIConstants.UIModulePath)
        {
        }

        internal class ContextDropdownListEntry
        {
            public AssistantContextEntry ContextEntry;
            [CanBeNull] public AssistantView Owner;
        }

        VisualElement m_ContextListViewContainer;
        ManagedScrollView<ContextDropdownListEntry, ContextElement> m_ManagedScrollView;

        internal void ClearData()
        {
            m_ManagedScrollView.ClearData();
        }

        internal void AddChoicesToDropdown(IList<AssistantContextEntry> entries, AssistantView owner = null)
        {
            foreach (var t in entries)
            {
                var data = new ContextDropdownListEntry
                {
                    ContextEntry = t,
                    Owner = owner
                };

                m_ManagedScrollView.AddData(data);
            }
        }

        internal IList<ContextDropdownListEntry> GetEntries()
        {
            return m_ManagedScrollView.Data.ToList();
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_ContextListViewContainer = view.Q<VisualElement>("contextDropDownViewContainer");

            var scrollView = new ScrollView();
            scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            m_ContextListViewContainer.Add(scrollView);

            m_ManagedScrollView = new ManagedScrollView<ContextDropdownListEntry, ContextElement>(scrollView);
            m_ManagedScrollView.Initialize(Context);
        }
    }
}
