using System;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    class AssistantDropdownEntry : ManagedTemplate
    {
        const string k_CheckedClass = "assistant-dropdown-entry-checked";
        const string k_IconHiddenClass = "assistant-dropdown-entry-icon-hidden";

        VisualElement m_Root;
        Image m_Icon;
        Label m_Label;

        string m_Id;
        string m_IconClass;

        public event Action<string> Clicked;

        public AssistantDropdownEntry() : base(AssistantUIConstants.UIModulePath) { }

        public string EntryId => m_Id;

        public void SetData(string id, string displayText, string iconClass)
        {
            m_Id = id;
            m_Label.text = displayText;

            if (!string.IsNullOrEmpty(m_IconClass))
            {
                m_Icon.RemoveFromClassList(m_IconClass);
            }

            if (!string.IsNullOrEmpty(iconClass))
            {
                m_IconClass = AssistantUIConstants.IconStylePrefix + iconClass;
                m_Icon.AddToClassList(m_IconClass);
                m_Icon.RemoveFromClassList(k_IconHiddenClass);
            }
            else
            {
                m_Icon.AddToClassList(k_IconHiddenClass);
                m_IconClass = null;
            }
        }

        public void SetChecked(bool isChecked)
        {
            m_Root.EnableInClassList(k_CheckedClass, isChecked);
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_Root = view.Q<VisualElement>("assistantDropdownEntryRoot");
            m_Icon = view.Q<Image>("entryIcon");
            m_Label = view.Q<Label>("entryLabel");

            m_Root.RegisterCallback<ClickEvent>(OnClicked);
        }

        void OnClicked(ClickEvent evt)
        {
            Clicked?.Invoke(m_Id);
        }
    }
}
