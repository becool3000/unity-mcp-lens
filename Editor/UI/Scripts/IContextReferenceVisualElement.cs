using UnityEngine;

namespace Unity.AI.Assistant.UI.Editor.Scripts
{
    interface IContextReferenceVisualElement
    {
        void RefreshVisualElement(Object activeTargetObject, Component activeTargetComponent);
    }
}
