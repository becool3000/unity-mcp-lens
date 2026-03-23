using Unity.AI.Assistant.UI.Editor.Scripts.Components.WhatsNew;
using Unity.AI.Toolkit.Accounts.Services;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts
{
    class WhatsNewWindow : EditorWindow
    {
        const string k_WindowName = "See what's new";

        static Vector2 k_MinSize = new(400, 600);

        internal WhatsNewView m_View;

        [SerializeField]
        string m_LastContentType;
        [SerializeField]
        int m_LastContentPage;

        public string ContentType
        {
            get => m_LastContentType;
            set => m_LastContentType = value;
        }
        public int ContentPageIndex
        {
            get => m_LastContentPage;
            set => m_LastContentPage = value;
        }

        public static void ShowWindow()
        {
            var editor = GetWindow<WhatsNewWindow>();
            editor.titleContent = new GUIContent(k_WindowName);
            editor.Show();
            editor.minSize = k_MinSize;
        }

        void CreateGUI()
        {
            m_View = new WhatsNewView();
            m_View.SetWindow(this);
            m_View.Initialize(null);
            m_View.CloseRequested += Close;
            m_View.style.flexGrow = 1;
            m_View.style.minWidth = 400;
            rootVisualElement.Add(m_View);

            m_View.RepaintWindow += Repaint;

            m_View.InitializeThemeAndStyle();
        }

        [InitializeOnLoadMethod]
        static void Init() => DropdownExtension.RegisterMainMenuExtension(container => container.Add(new AssistantToolbarMenuItem()), 0);

        class AssistantToolbarMenuItem : VisualElement
        {
            public AssistantToolbarMenuItem()
            {
                AddToClassList("label-button");
                AddToClassList("text-menu-item");
                AddToClassList("dropdown-item-with-margin");

                var label = new Label("See What's New");
                label.AddManipulator(new Clickable(ShowWindow));
                Add(label);
            }
        }
    }
}
