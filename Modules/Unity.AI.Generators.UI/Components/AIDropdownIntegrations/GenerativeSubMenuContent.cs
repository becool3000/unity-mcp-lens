using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.UI.AIDropdownIntegrations
{
    class GenerativeSubMenuContent : PopupWindowContent
    {
        static GenerativeSubMenuContent s_GenerativeSubMenuContent;
        public static PopupWindowContent Content() => s_GenerativeSubMenuContent ??= new GenerativeSubMenuContent();

        readonly GenerativeSubMenu m_Content = new();
        public override VisualElement CreateGUI() => m_Content;
    }
}
