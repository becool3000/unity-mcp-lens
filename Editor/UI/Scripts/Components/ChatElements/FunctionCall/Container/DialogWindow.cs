using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class DialogWindow : EditorWindow
    {
        DialogView m_DialogView;
        VisualElement m_Content;
        const float k_MinWidth = 500;
        const float k_MinHeight = 50;

        public void SetContent(VisualElement content)
        {
            if (m_DialogView == null)
            {
                rootVisualElement.Clear();

                m_DialogView = new DialogView();
                m_DialogView.Initialize(null);
                rootVisualElement.Add(m_DialogView);

                // Set from C# since DialogView styles are not loaded on the "DialogView" node, but its child which
                // is internally considered the view root (received in InitializeView).
                m_DialogView.style.width = new StyleLength(new Length(100, LengthUnit.Percent));
                m_DialogView.style.height = new StyleLength(new Length(100, LengthUnit.Percent));
            }

            m_Content = content;
            m_DialogView.SetContent(content);

            // Register for geometry changes to adjust window size when content is laid out
            content.RegisterCallback<GeometryChangedEvent>(OnContentGeometryChanged);
        }

        void OnDestroy()
        {
            m_Content?.UnregisterCallback<GeometryChangedEvent>(OnContentGeometryChanged);
        }

        void OnContentGeometryChanged(GeometryChangedEvent evt)
        {
            // Only adjust if we have valid dimensions
            if (evt.newRect.height <= 0)
                return;

            // Calculate desired height based on content
            var contentHeight = evt.newRect.height;

            // Add padding from DialogView. Padding is not accounted in height, thus adding it manually.
            var container = m_Content.parent;
            var paddingTop = container.resolvedStyle.paddingTop;
            var paddingBottom = container.resolvedStyle.paddingBottom;
            var desiredHeight = contentHeight + paddingTop + paddingBottom;

            // Apply minimum constraints
            desiredHeight = UnityEngine.Mathf.Max(desiredHeight, k_MinHeight);

            // Set window size - keep width at minimum, adjust height
            minSize = new UnityEngine.Vector2(k_MinWidth, desiredHeight);
            maxSize = new UnityEngine.Vector2(k_MinWidth, desiredHeight);
        }
    }
}
