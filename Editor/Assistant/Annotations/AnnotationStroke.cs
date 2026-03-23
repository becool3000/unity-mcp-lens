using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.AI.Assistant.Annotations
{
    /// <summary>
    /// Represents a single stroke (pen stroke, line, etc.) in the annotation system.
    /// </summary>
    [Serializable]
    class AnnotationStroke
    {
        /// <summary>
        /// The points that make up this stroke, in screen coordinates.
        /// </summary>
        public List<Vector2> Points { get; set; } = new ();

        /// <summary>
        /// The color of this stroke.
        /// </summary>
        public Color Color { get; set; } = Color.red;

        /// <summary>
        /// The width of this stroke in pixels.
        /// </summary>
        public float Width { get; set; } = 3f;

        /// <summary>
        /// Whether this stroke is complete (user lifted pen).
        /// </summary>
        public bool IsComplete { get; set; }

        /// <summary>
        /// Creates a new stroke with the specified color and width.
        /// </summary>
        public AnnotationStroke(Color color, float width)
        {
            Color = color;
            Width = width;
        }

        /// <summary>
        /// Creates a new stroke with default settings.
        /// </summary>
        public AnnotationStroke() { }

        /// <summary>
        /// Adds a point to the stroke.
        /// </summary>
        public void AddPoint(Vector2 point)
        {
            // Optionally filter out duplicate points
            if (Points.Count > 0 && Vector2.Distance(Points[^1], point) < 0.5f)
                return;

            Points.Add(point);
        }

        /// <summary>
        /// Clears all points from the stroke.
        /// </summary>
        public void Clear()
        {
            Points.Clear();
            IsComplete = false;
        }

        /// <summary>
        /// Returns true if the stroke has enough points to be rendered.
        /// Single points are valid (rendered as dots).
        /// </summary>
        public bool IsValid => Points.Count >= 1;

        /// <summary>
        /// Gets the bounding rectangle of this stroke.
        /// </summary>
        public Rect GetBounds()
        {
            if (Points.Count == 0)
                return Rect.zero;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var point in Points)
            {
                minX = Mathf.Min(minX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxX = Mathf.Max(maxX, point.x);
                maxY = Mathf.Max(maxY, point.y);
            }

            // Expand by stroke width
            var halfWidth = Width * 0.5f;
            minX -= halfWidth;
            minY -= halfWidth;
            maxX += halfWidth;
            maxY += halfWidth;

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }
    }
}
