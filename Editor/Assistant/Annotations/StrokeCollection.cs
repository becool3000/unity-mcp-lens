using System.Collections.Generic;
using UnityEngine;

namespace Unity.AI.Assistant.Annotations
{
    /// <summary>
    /// Collection of strokes for an annotation session.
    /// </summary>
    class StrokeCollection
    {
        readonly List<AnnotationStroke> m_Strokes = new ();
        readonly Stack<List<AnnotationStroke>> m_UndoStack = new ();
        readonly Stack<List<AnnotationStroke>> m_RedoStack = new ();
        AnnotationStroke m_CurrentAnnotationStroke;

        /// <summary>
        /// All completed strokes.
        /// </summary>
        public IReadOnlyList<AnnotationStroke> Strokes => m_Strokes;

        /// <summary>
        /// The stroke currently being drawn (may be null).
        /// </summary>
        public AnnotationStroke currentAnnotationStroke => m_CurrentAnnotationStroke;

        /// <summary>
        /// Whether there are any strokes (including the current one).
        /// </summary>
        public bool HasStrokes => m_Strokes.Count > 0 || (m_CurrentAnnotationStroke != null && m_CurrentAnnotationStroke.Points.Count > 0);

        /// <summary>
        /// Whether undo is available (there are actions to undo).
        /// </summary>
        public bool CanUndo => m_UndoStack.Count > 0;

        /// <summary>
        /// Whether redo is available (there are actions to redo).
        /// </summary>
        public bool CanRedo => m_RedoStack.Count > 0;

        /// <summary>
        /// The current pen color.
        /// </summary>
        public Color CurrentColor { get; set; } = Color.red;

        /// <summary>
        /// The current pen width.
        /// </summary>
        public float CurrentWidth { get; set; } = 3f;

        /// <summary>
        /// Begins a new stroke at the specified position.
        /// </summary>
        public void BeginStroke(Vector2 position)
        {
            // Save current state to undo stack when a new stroke is started
            m_UndoStack.Push(new List<AnnotationStroke>(m_Strokes));
            // Clear redo stack when a new stroke is started
            m_RedoStack.Clear();

            m_CurrentAnnotationStroke = new AnnotationStroke(CurrentColor, CurrentWidth);
            m_CurrentAnnotationStroke.AddPoint(position);
        }

        /// <summary>
        /// Continues the current stroke to the specified position.
        /// </summary>
        public void ContinueStroke(Vector2 position)
        {
            m_CurrentAnnotationStroke?.AddPoint(position);
        }

        /// <summary>
        /// Ends the current stroke.
        /// </summary>
        public void EndStroke()
        {
            if (m_CurrentAnnotationStroke != null && m_CurrentAnnotationStroke.IsValid)
            {
                m_CurrentAnnotationStroke.IsComplete = true;
                m_Strokes.Add(m_CurrentAnnotationStroke);
            }
            m_CurrentAnnotationStroke = null;
        }

        /// <summary>
        /// Clears all strokes and saves them to undo stack as a single action.
        /// Only adds to undo stack if there are strokes to clear (prevents spam clicking from creating multiple undo entries).
        /// </summary>
        public void Clear()
        {
            // Only add to undo stack if there are actually strokes to clear
            if (m_Strokes.Count > 0 || (m_CurrentAnnotationStroke != null && m_CurrentAnnotationStroke.Points.Count > 0))
            {
                // Save current state to undo stack before clearing
                m_UndoStack.Push(new List<AnnotationStroke>(m_Strokes));
                // Clear redo stack when clear is performed
                m_RedoStack.Clear();

                m_Strokes.Clear();
                m_CurrentAnnotationStroke = null;
            }
        }

        /// <summary>
        /// Undoes the last action (stroke or clear).
        /// </summary>
        public void UndoLast()
        {
            if (m_UndoStack.Count > 0)
            {
                // Save current state to redo stack
                m_RedoStack.Push(new List<AnnotationStroke>(m_Strokes));
                // Restore previous state
                m_Strokes.Clear();
                m_Strokes.AddRange(m_UndoStack.Pop());
            }
        }

        /// <summary>
        /// Redoes the last undone action.
        /// </summary>
        public void Redo()
        {
            if (m_RedoStack.Count > 0)
            {
                // Save current state to undo stack
                m_UndoStack.Push(new List<AnnotationStroke>(m_Strokes));
                // Restore next state
                m_Strokes.Clear();
                m_Strokes.AddRange(m_RedoStack.Pop());
            }
        }

        /// <summary>
        /// Adds a stroke directly to the collection (used for domain reload restoration).
        /// </summary>
        public void AddStroke(AnnotationStroke annotationStroke)
        {
            if (annotationStroke != null && annotationStroke.IsValid)
            {
                m_Strokes.Add(annotationStroke);
            }
        }

        /// <summary>
        /// Initializes the undo stack after strokes have been restored from serialization.
        /// Reconstructs undo history so each stroke can be undone individually after domain reload.
        /// Call this after all strokes have been added via AddStroke().
        /// </summary>
        public void FinalizeLoad()
        {
            // Clear undo/redo stacks (domain reload clears them anyway)
            m_UndoStack.Clear();
            m_RedoStack.Clear();

            // Reconstruct undo history by creating entries for each stroke
            // This allows users to undo each stroke individually after domain reload
            if (m_Strokes.Count > 0)
            {
                // Create undo entries in forward order so the stack has the correct order
                // For strokes [1, 2, 3], we want the stack (top to bottom) to be:
                // [1, 2] <- top (undo the last stroke)
                // [1]
                // []     <- bottom
                for (int i = 0; i < m_Strokes.Count; i++)
                {
                    // Create a list containing strokes 0 through i-1 (the state before stroke "i" was added)
                    var previousState = new List<AnnotationStroke>();
                    for (int j = 0; j < i; j++)
                    {
                        previousState.Add(m_Strokes[j]);
                    }
                    m_UndoStack.Push(previousState);
                }
            }
        }
    }
}
