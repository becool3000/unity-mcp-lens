using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Preview
{
    /// <summary>
    /// Container for UI preview content with built-in resolution and aspect ratio management
    /// </summary>
    internal class UIPreviewContainer : VisualElement
    {
        const string k_USSClassName = "ui-preview-container";

        VisualElement m_ContentArea;
        Vector2Int? m_TargetResolution;
        float m_AspectRatio = 16f / 9f;

        public UIPreviewContainer()
        {
            AddToClassList(k_USSClassName);

            m_ContentArea = new VisualElement();
            m_ContentArea.style.position = Position.Absolute;
            m_ContentArea.style.left = 0;
            m_ContentArea.style.top = 0;
            m_ContentArea.style.right = 0;
            m_ContentArea.style.bottom = 0;
            style.overflow = Overflow.Visible;
            style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);

            Add(m_ContentArea);

            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        /// <summary>
        /// Gets or sets the target resolution for the preview
        /// </summary>
        public Vector2Int? TargetResolution
        {
            get => m_TargetResolution;
            set
            {
                m_TargetResolution = value;
                UpdateLayout();
            }
        }

        /// <summary>
        /// Gets or sets the aspect ratio when no specific resolution is set
        /// </summary>
        public float AspectRatio
        {
            get => m_AspectRatio;
            set
            {
                m_AspectRatio = value;
                if (!m_TargetResolution.HasValue)
                {
                    UpdateLayout();
                }
            }
        }

        /// <summary>
        /// Gets the content area where UXML content is added
        /// </summary>
        public VisualElement ContentArea => m_ContentArea;

        /// <summary>
        /// Adds a child element to the content area
        /// </summary>
        public new void Add(VisualElement child)
        {
            if (child == m_ContentArea)
            {
                base.Add(child);
            }
            else
            {
                m_ContentArea.Add(child);

                if (m_TargetResolution.HasValue)
                {
                    var res = m_TargetResolution.Value;
                    child.style.width = res.x;
                    child.style.height = res.y;
                }
            }
        }

        /// <summary>
        /// Removes a child element from the content area
        /// </summary>
        public new void Remove(VisualElement child)
        {
            m_ContentArea.Remove(child);
        }

        /// <summary>
        /// Clears all content from the preview
        /// </summary>
        public new void Clear()
        {
            m_ContentArea.Clear();
        }

        /// <summary>
        /// Sets up the preview to stretch and fill its parent container
        /// </summary>
        public void SetupForDisplay()
        {
            style.width = Length.Percent(100);
            style.height = Length.Percent(100);
            style.flexGrow = 1;
            style.alignSelf = Align.Stretch;
            style.position = Position.Relative;

            m_ContentArea.style.width = Length.Percent(100);
            m_ContentArea.style.height = Length.Percent(100);
        }


        void UpdateLayout()
        {
            if (m_TargetResolution.HasValue)
            {
                var res = m_TargetResolution.Value;
                style.width = res.x;
                style.height = res.y;
                m_ContentArea.style.width = res.x;
                m_ContentArea.style.height = res.y;
            }
            else
            {
                style.width = StyleKeyword.Auto;
                style.height = StyleKeyword.Auto;
                m_ContentArea.style.width = Length.Percent(100);
                m_ContentArea.style.height = Length.Percent(100);
            }
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (!m_TargetResolution.HasValue && evt.newRect.width > 0 && evt.newRect.height > 0 && m_ContentArea != null)
            {
                m_ContentArea.style.width = Length.Percent(100);
                m_ContentArea.style.height = Length.Percent(100);
            }
        }
    }
}
