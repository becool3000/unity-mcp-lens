using System;
using System.Collections.Generic;
using Unity.AI.Sound.Services.Stores.States;
using UnityEngine;

namespace Unity.AI.Sound.Services.Undo
{
    enum MarkerMode
    {
        None = 0,
        Marker,
        Point
    }

    class SoundEnvelopeUndoManager : ScriptableObject
    {
        [SerializeField]
        MarkerMode m_CurrentMarkerMode = MarkerMode.Marker;

        [SerializeField]
        int m_SelectedPointIndex = -1;

        [SerializeField]
        SoundEnvelopeSettings m_EnvelopeSettings = new ();

        [SerializeField]
        float m_TotalPanOffset = 0;

        [SerializeField]
        float m_CurrentZoomLevel = 1;

        public SoundEnvelopeSettings envelopeSettings
        {
            get => m_EnvelopeSettings;
            set
            {
                m_EnvelopeSettings = value;
                OnValidate();
            }
        }

        MarkerMode m_PreviousMarkerMode;
        bool m_ControlPointsChanged;
        int m_PreviousSelectedPointIndex;
        float m_PreviousStartMarkerPosition;
        float m_PreviousEndMarkerPosition;
        float m_PreviousPanOffset;
        float m_PreviousZoomLevel;

        public event Action OnControlPointDataChanged;
        public event Action OnMarkerPositionChanged;
        public event Action OnMarkerModeChanged;
        public event Action OnPanOrZoomChanged;

        internal MarkerMode CurrentMarkerMode
        {
            get => m_CurrentMarkerMode;
            set
            {
                m_CurrentMarkerMode = value;
                OnValidate();
            }
        }

        internal int SelectedPointIndex
        {
            get => m_SelectedPointIndex;
            set
            {
                m_SelectedPointIndex = value;
                OnValidate();
            }
        }

        internal List<Vector2> ControlPoints
        {
            get => envelopeSettings.controlPoints;
            set
            {
                m_EnvelopeSettings = envelopeSettings with { controlPoints = value };
                m_ControlPointsChanged = true;
                OnValidate();
            }
        }

        internal float StartMarkerPosition
        {
            get => envelopeSettings.startPosition;
            set
            {
                m_EnvelopeSettings = envelopeSettings with { startPosition = value };
                OnValidate();
            }
        }

        internal float EndMarkerPosition
        {
            get => envelopeSettings.endPosition;
            set
            {
                m_EnvelopeSettings = envelopeSettings with { endPosition = value };
                OnValidate();
            }
        }

        internal float TotalPanOffset
        {
            get => m_TotalPanOffset;
            set
            {
                m_TotalPanOffset = value;
                OnValidate();
            }
        }

        internal float CurrentZoomLevel
        {
            get => m_CurrentZoomLevel;
            set
            {
                m_CurrentZoomLevel = value;
                OnValidate();
            }
        }

        void OnValidate()
        {
            if (!Mathf.Approximately(m_PreviousStartMarkerPosition, StartMarkerPosition) || !Mathf.Approximately(m_PreviousEndMarkerPosition, EndMarkerPosition))
            {
                m_PreviousStartMarkerPosition = StartMarkerPosition;
                m_PreviousEndMarkerPosition = EndMarkerPosition;
                OnMarkerPositionChanged?.Invoke();
            }
            if (m_PreviousMarkerMode != m_CurrentMarkerMode)
            {
                m_PreviousMarkerMode = m_CurrentMarkerMode;
                OnMarkerModeChanged?.Invoke();
            }
            if (m_ControlPointsChanged || m_PreviousSelectedPointIndex != m_SelectedPointIndex)
            {
                m_PreviousSelectedPointIndex = m_SelectedPointIndex;
                m_ControlPointsChanged = false; // Reset the flag
                OnControlPointDataChanged?.Invoke();
            }
            if (!Mathf.Approximately(m_PreviousPanOffset, m_TotalPanOffset) || !Mathf.Approximately(m_PreviousZoomLevel, m_CurrentZoomLevel))
            {
                m_TotalPanOffset = Math.Clamp(m_TotalPanOffset, -(CurrentZoomLevel + 1) / 2, (CurrentZoomLevel + 1) / 2);

                m_PreviousPanOffset = m_TotalPanOffset;
                m_PreviousZoomLevel = m_CurrentZoomLevel;
                OnPanOrZoomChanged?.Invoke();
            }
        }

        void OnEnable()
        {
            // Initialize previous state
            m_PreviousSelectedPointIndex = m_SelectedPointIndex;
            m_PreviousStartMarkerPosition = StartMarkerPosition;
            m_PreviousEndMarkerPosition = EndMarkerPosition;
            m_PreviousMarkerMode = m_CurrentMarkerMode;
            m_PreviousPanOffset = m_TotalPanOffset;
            m_PreviousZoomLevel = m_CurrentZoomLevel;
        }

        public bool IsValidSelectedPointIndex() => m_SelectedPointIndex >= 0 && m_SelectedPointIndex < ControlPoints.Count;

        public void InitializeFromAudioData(SoundEnvelopeSettings audioData)
        {
            if (audioData == null)
                return;

            ControlPoints = audioData.controlPoints;
            StartMarkerPosition = audioData.startPosition;
            EndMarkerPosition = audioData.endPosition;
            OnValidate();
        }

        public void ClearControlPoints()
        {
            ControlPoints = new List<Vector2>();
            m_SelectedPointIndex = -1;
            OnValidate();
        }

        public void RemoveControlPointAtIndex()
        {
            var previousPosition = ControlPoints[m_SelectedPointIndex];
            var controlPoints = ControlPoints;
            controlPoints.RemoveAt(m_SelectedPointIndex);
            ControlPoints = controlPoints;

            if (m_SelectedPointIndex == 0 && ControlPoints.Count > 0)
            {
                // keep the selection
            }
            else if (m_SelectedPointIndex == 0 || m_SelectedPointIndex >= ControlPoints.Count)
            {
                --m_SelectedPointIndex;
            }
            else if (ControlPoints[m_SelectedPointIndex].x - previousPosition.x > previousPosition.x - ControlPoints[m_SelectedPointIndex - 1].x)
            {
                --m_SelectedPointIndex;
            }

            m_ControlPointsChanged = true;

            OnValidate();
        }

        public int FindControlPointIndex(float newPositionX, float newPositionY, float toleranceX = 0.02f, float toleranceY = 0.1f)
        {
            for (var i = 0; i < ControlPoints.Count; i++)
            {
                if (Mathf.Abs(ControlPoints[i].x - newPositionX) < toleranceX && Mathf.Abs(ControlPoints[i].y - newPositionY) < toleranceY)
                {
                    return i;
                }
            }
            return -1;
        }

        public int InsertControlPoint(float newPositionX, float newPositionY, bool updateSelectdIndex = true)
        {
            var newPoint = new Vector2(newPositionX, newPositionY);
            var insertionIndex = ControlPoints.BinarySearch(newPoint, Comparer<Vector2>.Create((a, b) => a.x.CompareTo(b.x)));
            if (insertionIndex < 0)
                insertionIndex = ~insertionIndex;

            var controlPoints = ControlPoints;
            controlPoints.Insert(insertionIndex, newPoint);
            ControlPoints = controlPoints;
            if (updateSelectdIndex)
                m_SelectedPointIndex = insertionIndex;
            OnValidate();
            return insertionIndex;
        }

        public float ClampControlPointPositionX(float newPositionX, int pointIndex = -1)
        {
            if (pointIndex < 0)
                pointIndex = m_SelectedPointIndex;

            // clamp to previous and next points (if any)
            if (pointIndex > 0)
                newPositionX = Mathf.Max(newPositionX, ControlPoints[pointIndex - 1].x);
            if (pointIndex < ControlPoints.Count - 1)
                newPositionX = Mathf.Min(newPositionX, ControlPoints[pointIndex + 1].x);

            return newPositionX;
        }

        public void SetControlPointPosition(float positionX, float positionY, int pointIndex = -1)
        {
            if (pointIndex < 0)
                pointIndex = m_SelectedPointIndex;
            ControlPoints[pointIndex] = new Vector2(positionX, positionY);
            m_ControlPointsChanged = true;
            OnValidate();
        }
    }
}
