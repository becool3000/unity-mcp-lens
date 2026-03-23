using System;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    abstract class ManagedListEntry : ManagedTemplate
    {
        protected ManagedListEntry(string basePath = null)
            : base(basePath ?? AssistantUIConstants.UIModulePath)
        {
        }

        public bool DidComeIntoView { get; protected set; }

        public virtual void SetData(int index, object data, bool isSelected = false)
        {
        }

        public virtual bool CameIntoView()
        {
            DidComeIntoView = true;
            return false;
        }
    }
}
