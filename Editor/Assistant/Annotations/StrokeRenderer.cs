using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.AI.Assistant.Annotations
{
    /// <summary>
    /// Renders strokes to a RenderTexture using GPU mesh rendering.
    /// </summary>
    class StrokeRenderer
    {
        Material m_StrokeMaterial;
        Mesh m_StrokeMesh;
        readonly List<Vector3> m_Vertices = new ();
        readonly List<Color> m_Colors = new ();
        readonly List<int> m_Triangles = new ();

        // Number of segments for round caps/joins
        const int k_CircleSegments = 12;
        const float k_BorderWidth = 2f;
        const float k_CursorRingWidth = 3f;

        static readonly Color k_CursorWhiteColor = new (1f, 1f, 1f, 0.8f);
        static readonly Color k_DebugColor = new (0f, 1f, 0f, 0.6f);
        static readonly Color k_TransparentColor = new (0, 0, 0, 0);
        static readonly int k_SrcBlend = Shader.PropertyToID("_SrcBlend");
        static readonly int k_DstBlend = Shader.PropertyToID("_DstBlend");
        static readonly int k_Cull = Shader.PropertyToID("_Cull");
        static readonly int k_ZWrite = Shader.PropertyToID("_ZWrite");

        /// <summary>
        /// Initializes the stroke renderer.
        /// </summary>
        public void Initialize()
        {
            // Create a simple unlit shader for strokes
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                // Fallback to UI shader
                shader = Shader.Find("UI/Default");
            }

            m_StrokeMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            // Set up for transparent rendering
            m_StrokeMaterial.SetInt(k_SrcBlend, (int)BlendMode.SrcAlpha);
            m_StrokeMaterial.SetInt(k_DstBlend, (int)BlendMode.OneMinusSrcAlpha);
            m_StrokeMaterial.SetInt(k_Cull, (int)CullMode.Off);
            m_StrokeMaterial.SetInt(k_ZWrite, 0);

            m_StrokeMesh = new Mesh
            {
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = IndexFormat.UInt32  // Support meshes with more than 65k vertices
            };
        }

        /// <summary>
        /// Cleans up resources.
        /// </summary>
        public void Cleanup()
        {
            if (m_StrokeMaterial != null)
            {
                Object.DestroyImmediate(m_StrokeMaterial);
                m_StrokeMaterial = null;
            }

            if (m_StrokeMesh != null)
            {
                Object.DestroyImmediate(m_StrokeMesh);
                m_StrokeMesh = null;
            }
        }

        /// <summary>
        /// Renders all strokes to the specified RenderTexture without border.
        /// </summary>
        public void RenderStrokes(RenderTexture target, StrokeCollection strokes)
        {
            RenderStrokes(target, strokes, Vector2.zero, false, 1f, null, false);
        }

        /// <summary>
        /// Renders all strokes to the specified RenderTexture with all options.
        /// </summary>
        public void RenderStrokes(RenderTexture target, StrokeCollection strokes, Vector2 cursorPosition, bool showCursor, float cursorRadius, List<Rect> exclusionRects, bool drawBorder)
        {
            if (target == null || strokes == null)
                return;

            if (m_StrokeMaterial == null)
                Initialize();

            // Set up command buffer - we always need to clear even if no strokes
            var cmd = new CommandBuffer { name = "Annotations Stroke Render" };

            cmd.SetRenderTarget(target);
            cmd.ClearRenderTarget(true, true, Color.clear);

            // Build mesh from all strokes, cursor, and debug exclusion rects
            BuildMesh(strokes, target.height, cursorPosition, showCursor, cursorRadius, exclusionRects);

            if (m_Vertices.Count > 0)
            {
                // Set up orthographic projection for screen space
                var projMatrix = Matrix4x4.Ortho(0, target.width, 0, target.height, -1, 1);
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, projMatrix);

                cmd.DrawMesh(m_StrokeMesh, Matrix4x4.identity, m_StrokeMaterial);
            }

            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();
        }

        /// <summary>
        /// Builds a mesh from all strokes in the collection with all options.
        /// </summary>
        void BuildMesh(StrokeCollection strokes, int height, Vector2 cursorPosition, bool showCursor, float cursorRadius, List<Rect> exclusionRects)
        {
            m_Vertices.Clear();
            m_Colors.Clear();
            m_Triangles.Clear();

            // Add completed strokes
            foreach (var stroke in strokes.Strokes)
            {
                if (stroke.IsValid)
                {
                    AddStrokeToMesh(stroke, height);
                }
            }

            // Add current stroke being drawn
            if (strokes.currentAnnotationStroke != null && strokes.currentAnnotationStroke.IsValid)
            {
                AddStrokeToMesh(strokes.currentAnnotationStroke, height);
            }

            // Add cursor circle if visible
            if (showCursor)
            {
                Vector2 flippedCursorPos = cursorPosition;
                flippedCursorPos.y = height - cursorPosition.y;

                // Draw cursor as an outline circle with dark color for visibility on all backgrounds
                Color cursorColor = new Color(0f, 0f, 0f, 0.5f);
                AddCursorCircle(flippedCursorPos, cursorRadius, cursorColor);
            }

            // Add debug exclusion rectangles if provided
            if (exclusionRects != null && exclusionRects.Count > 0)
            {
                foreach (var rect in exclusionRects)
                {
                    if (rect.width > 0 && rect.height > 0)
                    {
                        AddDebugRectangle(rect, height);
                    }
                }
            }

            // Update mesh
            m_StrokeMesh.Clear();
            if (m_Vertices.Count > 0)
            {
                m_StrokeMesh.SetVertices(m_Vertices);
                m_StrokeMesh.SetColors(m_Colors);
                m_StrokeMesh.SetTriangles(m_Triangles, 0);
            }
        }

        /// <summary>
        /// Adds a single stroke to the mesh with round joins and caps.
        /// </summary>
        void AddStrokeToMesh(AnnotationStroke annotationStroke, int screenHeight)
        {
            var points = annotationStroke.Points;
            if (points.Count < 1)
                return;

            float halfWidth = annotationStroke.Width * 0.5f;

            // First, add circles at each point for round joins/caps
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 current = points[i];
                current.y = screenHeight - current.y;
                AddCircle(current, halfWidth, annotationStroke.Color);
            }

            // Then add quad segments between consecutive points (if there are multiple points)
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 p0 = points[i];
                Vector2 p1 = points[i + 1];

                // Flip Y
                p0.y = screenHeight - p0.y;
                p1.y = screenHeight - p1.y;

                AddLineSegment(p0, p1, halfWidth, annotationStroke.Color);
            }
        }

        /// <summary>
        /// Adds a circle (fan of triangles) at the specified position.
        /// </summary>
        void AddCircle(Vector2 center, float radius, Color color)
        {
            int centerIndex = m_Vertices.Count;

            // Add center vertex
            m_Vertices.Add(new Vector3(center.x, center.y, 0));
            m_Colors.Add(color);

            // Add perimeter vertices
            for (int i = 0; i <= k_CircleSegments; i++)
            {
                var angle = (i / (float)k_CircleSegments) * Mathf.PI * 2f;
                var x = center.x + Mathf.Cos(angle) * radius;
                var y = center.y + Mathf.Sin(angle) * radius;

                m_Vertices.Add(new Vector3(x, y, 0));
                m_Colors.Add(color);
            }

            // Create triangles (fan)
            for (int i = 0; i < k_CircleSegments; i++)
            {
                m_Triangles.Add(centerIndex);
                m_Triangles.Add(centerIndex + 1 + i);
                m_Triangles.Add(centerIndex + 1 + i + 1);
            }
        }

        /// <summary>
        /// Adds a quad segment between two points.
        /// </summary>
        void AddLineSegment(Vector2 p0, Vector2 p1, float halfWidth, Color color)
        {
            Vector2 direction = (p1 - p0).normalized;
            Vector2 perpendicular = new Vector2(-direction.y, direction.x);

            int startVertex = m_Vertices.Count;

            // Four corners of the quad
            m_Vertices.Add(new Vector3(p0.x - perpendicular.x * halfWidth, p0.y - perpendicular.y * halfWidth, 0));
            m_Vertices.Add(new Vector3(p0.x + perpendicular.x * halfWidth, p0.y + perpendicular.y * halfWidth, 0));
            m_Vertices.Add(new Vector3(p1.x - perpendicular.x * halfWidth, p1.y - perpendicular.y * halfWidth, 0));
            m_Vertices.Add(new Vector3(p1.x + perpendicular.x * halfWidth, p1.y + perpendicular.y * halfWidth, 0));

            m_Colors.Add(color);
            m_Colors.Add(color);
            m_Colors.Add(color);
            m_Colors.Add(color);

            // Two triangles for the quad
            m_Triangles.Add(startVertex);
            m_Triangles.Add(startVertex + 1);
            m_Triangles.Add(startVertex + 2);

            m_Triangles.Add(startVertex + 1);
            m_Triangles.Add(startVertex + 3);
            m_Triangles.Add(startVertex + 2);
        }

        /// <summary>
        /// Adds a cursor circle outline with dark-white-dark ring (transparent center).
        /// </summary>
        void AddCursorCircle(Vector2 center, float radius, Color darkColor)
        {
            // Use half the radius to match stroke rendering (which uses halfWidth = width * 0.5f)
            var halfRadius = radius * 0.5f;

            // Define colors and dimensions
            var halfRingWidth = k_CursorRingWidth * 0.5f; // Half for each side of white

            // Three radii for the ring: dark outer -> white middle -> transparent center
            var outerRadius = halfRadius + halfRingWidth; // Outer dark edge
            var middleRadius = halfRadius; // Middle white ring
            var innerRadius = halfRadius - halfRingWidth; // Inner edge (transparent starts here)

            // Build outer circle vertices (dark color)
            int startVertexOuter = m_Vertices.Count;
            for (int i = 0; i <= k_CircleSegments; i++)
            {
                var angle = (i / (float)k_CircleSegments) * Mathf.PI * 2f;
                var x = center.x + Mathf.Cos(angle) * outerRadius;
                var y = center.y + Mathf.Sin(angle) * outerRadius;

                m_Vertices.Add(new Vector3(x, y, 0));
                m_Colors.Add(darkColor);
            }

            // Build middle circle vertices (white color)
            int startVertexMiddle = m_Vertices.Count;
            for (int i = 0; i <= k_CircleSegments; i++)
            {
                var angle = (i / (float)k_CircleSegments) * Mathf.PI * 2f;
                var x = center.x + Mathf.Cos(angle) * middleRadius;
                var y = center.y + Mathf.Sin(angle) * middleRadius;

                m_Vertices.Add(new Vector3(x, y, 0));
                m_Colors.Add(k_CursorWhiteColor);
            }

            // Build inner circle vertices (transparent/invisible)
            var startVertexInner = m_Vertices.Count;
            for (int i = 0; i <= k_CircleSegments; i++)
            {
                var angle = (i / (float)k_CircleSegments) * Mathf.PI * 2f;
                var x = center.x + Mathf.Cos(angle) * innerRadius;
                var y = center.y + Mathf.Sin(angle) * innerRadius;

                m_Vertices.Add(new Vector3(x, y, 0));
                m_Colors.Add(k_TransparentColor);
            }

            // Create triangles for outer dark ring (outer to middle)
            for (int i = 0; i < k_CircleSegments; i++)
            {
                var outerA = startVertexOuter + i;
                var outerB = startVertexOuter + i + 1;
                var middleA = startVertexMiddle + i;
                var middleB = startVertexMiddle + i + 1;

                // Triangle 1
                m_Triangles.Add(outerA);
                m_Triangles.Add(middleA);
                m_Triangles.Add(outerB);

                // Triangle 2
                m_Triangles.Add(middleA);
                m_Triangles.Add(middleB);
                m_Triangles.Add(outerB);
            }

            // Create triangles for inner white ring (middle to inner)
            for (int i = 0; i < k_CircleSegments; i++)
            {
                var middleA = startVertexMiddle + i;
                var middleB = startVertexMiddle + i + 1;
                var innerA = startVertexInner + i;
                var innerB = startVertexInner + i + 1;

                // Triangle 1
                m_Triangles.Add(middleA);
                m_Triangles.Add(innerA);
                m_Triangles.Add(middleB);

                // Triangle 2
                m_Triangles.Add(innerA);
                m_Triangles.Add(innerB);
                m_Triangles.Add(middleB);
            }
        }

        /// <summary>
        /// Adds a debug rectangle outline for exclusion rects.
        /// Draws a green rectangle with semi-transparency.
        /// </summary>
        void AddDebugRectangle(Rect rect, int screenHeight)
        {
            var debugBorderWidth = k_BorderWidth * 0.5f;

            // Flip Y coordinates to match screen space (same as stroke rendering)
            var flippedTopY = screenHeight - rect.y;
            var flippedBottomY = screenHeight - rect.y - rect.height;

            // Draw four sides of the rectangle
            // Top edge (in flipped space, this is where Y is larger)
            AddLineSegment(
                new Vector2(rect.x, flippedTopY),
                new Vector2(rect.x + rect.width, flippedTopY),
                debugBorderWidth,
                k_DebugColor
            );

            // Bottom edge (in flipped space, this is where Y is smaller)
            AddLineSegment(
                new Vector2(rect.x, flippedBottomY),
                new Vector2(rect.x + rect.width, flippedBottomY),
                debugBorderWidth,
                k_DebugColor
            );

            // Left edge
            AddLineSegment(
                new Vector2(rect.x, flippedBottomY),
                new Vector2(rect.x, flippedTopY),
                debugBorderWidth,
                k_DebugColor
            );

            // Right edge
            AddLineSegment(
                new Vector2(rect.x + rect.width, flippedBottomY),
                new Vector2(rect.x + rect.width, flippedTopY),
                debugBorderWidth,
                k_DebugColor
            );
        }
    }
}
