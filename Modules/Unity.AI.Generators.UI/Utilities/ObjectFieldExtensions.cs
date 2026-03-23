using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.UI.Utilities
{
    static class ObjectFieldExtensions
    {
        public static void ShowObjectPicker(this ObjectField objectField)
        {
            var selector = objectField.Q<VisualElement>(className: "unity-object-field__selector");
            var baseEvt = new Event
            {
                type = EventType.MouseDown,
                button = 0,
                clickCount = 1,
                mousePosition = selector.worldBound.center,
                modifiers = EventModifiers.None
            };
            using var mouseDownEvent = MouseDownEvent.GetPooled(baseEvt);
            mouseDownEvent.target = selector;
            selector.SendEvent(mouseDownEvent);
        }
    }
}
