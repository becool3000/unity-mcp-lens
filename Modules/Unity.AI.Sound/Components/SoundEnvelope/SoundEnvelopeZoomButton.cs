using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Sound.Components
{
    [UxmlElement]
    partial class SoundEnvelopeZoomButton : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Sound/Components/SoundEnvelope/SoundEnvelopeZoomButton.uxml";

        bool m_MenuHasDisabledItemsOnly;
        readonly ToolbarMenu m_ZoomMenu;

        public const float zoomFactor = 1.2f;
        float m_ZoomValue = 1;
        public Action<float> onZoomChanged;

        string ZoomButtonLabelFormat => $"{1 / zoomValue:0%}";

        public float zoomValue => m_ZoomValue;

        public SoundEnvelopeZoomButton()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            m_ZoomMenu = this.Q<ToolbarMenu>(classes: "zoom-button");

            m_ZoomMenu.menu.AppendAction("Zoom in", (a) => ZoomOption(0));
            m_ZoomMenu.menu.AppendAction("Zoom out", (a) => ZoomOption(1));
            m_ZoomMenu.menu.AppendAction("Zoom to 50%", (a) => ZoomOption(2));
            m_ZoomMenu.menu.AppendAction("Zoom to 100%", (a) => ZoomOption(3));
            m_ZoomMenu.menu.AppendAction("Zoom to 200%", (a) => ZoomOption(4));
            m_ZoomMenu.menu.AppendAction("Zoom to 400%", (a) => ZoomOption(5));
            return;

            void ZoomOption(int index)
            {
                switch (index)
                {
                    case 0:
                        ZoomIncrement(-zoomFactor);
                        break;
                    case 1:
                        ZoomIncrement(zoomFactor);
                        break;
                    case 2:
                        SetZoom(2);
                        break;
                    case 3:
                        SetZoom(1);
                        break;
                    case 4:
                        SetZoom(0.5f);
                        break;
                    case 5:
                        SetZoom(0.25f);
                        break;
                }

                m_ZoomMenu.text = ZoomButtonLabelFormat;
            }
        }

        public void ZoomIncrement(float factor) => SetZoom(factor > 0 ? zoomValue * factor : zoomValue / -factor);

        public void SetZoom(float zoomValue)
        {
            m_ZoomValue = Mathf.Clamp(zoomValue, 1 / 128f, 8f);
            m_ZoomMenu.text = ZoomButtonLabelFormat;
            onZoomChanged?.Invoke(this.zoomValue);
        }
    }
}
