using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class DoodleCursorOverlay : VisualElement
    {
        const float k_LineWidth = 1.0f;
        const float k_SegmentLength = 10.0f;
        readonly Color k_LineColor = Color.white;

        public DoodleCursorOverlay()
        {
            k_LineColor = EditorGUIUtility.isProSkin ? Color.white : Color.black;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += GenerateVisualContent;
        }

        void GenerateVisualContent(MeshGenerationContext context)
        {
            var width = contentRect.width;
            var height = contentRect.height;
            var painter = context.painter2D;
            painter.lineWidth = k_LineWidth;
            painter.lineCap = LineCap.Butt;

            painter.strokeColor = k_LineColor;

            var radius = Mathf.Max(width, height) * 0.5f;
            var circumference = 2 * Mathf.PI * radius;
            var segmentCount = (int)(circumference / k_SegmentLength);
            var segmentAngle = 360f / segmentCount;
            var dashedPercentage = 0.7f;
            var currentAngle = 0f;
            for (var i = 0; i < segmentCount; i++)
            {
                painter.BeginPath();
                painter.Arc(new Vector2(width * 0.5f, height * 0.5f), width * 0.5f, currentAngle, currentAngle + segmentAngle * dashedPercentage);
                painter.Stroke();
                currentAngle += segmentAngle;
            }
        }
    }
}
