using System;
using System.Collections.Generic;
using Unity.AI.Assistant.UI.Editor.Scripts;
using UnityEditor;
using Object = UnityEngine.Object;

namespace Unity.Assistant.UI.Components
{
    static class ContextMenuUtility
    {
        const string k_ContextMenuAddGameObject = "GameObject/Add to Assistant";
        const string k_ContextMenuAddAsset = "Assets/Add to Assistant";

        public static event Action<IEnumerable<Object>> ObjectsAttached;

        static void AttachObjects(IEnumerable<Object> objects)
        {
            EditorWindow.GetWindow<AssistantWindow>();

            ObjectsAttached?.Invoke(objects);
        }

        static bool ValidateObjects(Object[] objects)
        {
            foreach (var obj in objects)
            {
                if (obj is not DefaultAsset)
                {
                    return true;
                }
            }

            return false;
        }

        [MenuItem(k_ContextMenuAddGameObject, false, -1000)]
        static void AddContextGameObject()
        {
            AttachObjects(Selection.objects);
        }

        [MenuItem(k_ContextMenuAddAsset, false, 0)]
        static void AddContextAsset()
        {
            AttachObjects(Selection.objects);
        }

        [MenuItem(k_ContextMenuAddGameObject, true)]
        static bool ValidateAddContextGameObject()
        {
            return ValidateObjects(Selection.objects);
        }

        [MenuItem(k_ContextMenuAddAsset, true)]
        static bool ValidateAddContextAsset()
        {
            return ValidateObjects(Selection.objects);
        }
    }
}
