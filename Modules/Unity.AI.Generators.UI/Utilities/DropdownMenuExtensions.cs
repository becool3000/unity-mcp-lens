using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.UI.Utilities
{
    static class DropdownMenuExtensions
    {
        public static void Show(this DropdownMenu sourceDropdownMenu, Rect position)
        {
            if (sourceDropdownMenu == null)
            {
                Debug.LogError("Cannot show a null DropdownMenu.");
                return;
            }

            sourceDropdownMenu.PrepareForDisplay(null);

            var genericMenu = new GenericMenu();
            foreach (var item in sourceDropdownMenu.MenuItems())
            {
                switch (item)
                {
                    case DropdownMenuAction action:
                    {
                        var isOn = action.userData != null && (bool)action.userData;
                        switch (action.status)
                        {
                            case DropdownMenuAction.Status.Normal:
                                genericMenu.AddItem(new GUIContent(action.name), isOn, () => action.Execute());
                                break;
                            case DropdownMenuAction.Status.Disabled:
                                genericMenu.AddDisabledItem(new GUIContent(action.name), isOn);
                                break;
                        }
                        break;
                    }
                    case DropdownMenuSeparator:
                        genericMenu.AddSeparator("");
                        break;
                }
            }
            genericMenu.DropDown(position);
        }
    }
}
