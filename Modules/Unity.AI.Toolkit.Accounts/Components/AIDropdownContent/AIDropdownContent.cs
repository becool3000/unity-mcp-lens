using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts.Components
{
    class AIDropdownContent : PopupWindowContent
    {
        AIDropdownRoot m_Dropdown;

        internal event Action OnCreated;
        internal AIDropdownRoot dropdown
        {
            get => m_Dropdown;
            private set
            {
                m_Dropdown = value;
                OnCreated?.Invoke();
            }
        }

        public override VisualElement CreateGUI() => dropdown ??= new();
    }
}
