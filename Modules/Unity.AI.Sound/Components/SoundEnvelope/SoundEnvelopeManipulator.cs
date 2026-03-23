using System;
using Unity.AI.Sound.Services.Undo;
using Unity.AI.Generators.UI.Utilities;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Sound.Components
{
    class SoundEnvelopeManipulator : Manipulator
    {
        SoundEnvelope soundEnvelope => target as SoundEnvelope;

        float MarkerThreshold => 0.02f * soundEnvelope.undoManager.CurrentZoomLevel;

        const bool k_PanningModeSupported = true;

        Vector2 m_LastMousePosition;
        bool m_IsDraggingControlPoint;
        bool m_IsDraggingStartMarker;
        bool m_IsDraggingEndMarker;
        bool m_IsMovingMarker;
        bool m_IsPanning;

        Button m_PlayButton;
        SoundEnvelopeZoomButton m_ZoomButton;
        Label m_HoverValue;

        protected override void RegisterCallbacksOnTarget()
        {
            m_HoverValue = target.Q<Label>(classes: "hover-value");
            m_PlayButton = target.Q<Button>(classes: "play-button");
            m_ZoomButton = target.Q<SoundEnvelopeZoomButton>();

            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            target.RegisterCallback<PointerUpEvent>(OnPointerUpEvent);
            target.RegisterCallback<PointerLeaveEvent>(OnPointerLeaveEvent);
            target.RegisterCallback<KeyDownEvent>(OnKeyDown);
            target.RegisterCallback<WheelEvent>(OnWheelEvent);
            target.focusable = true;
            target.Focus();
            
            m_HoverValue.EnableInClassList("hide", !m_IsDraggingControlPoint);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUpEvent);
            target.UnregisterCallback<PointerLeaveEvent>(OnPointerLeaveEvent);
            target.UnregisterCallback<KeyDownEvent>(OnKeyDown);
            target.UnregisterCallback<WheelEvent>(OnWheelEvent);
        }

        void OnWheelEvent(WheelEvent evt)
        {
            Undo.RegisterCompleteObjectUndo(soundEnvelope.undoManager, "Zoom Audio waveform");
            m_ZoomButton.ZoomIncrement(Mathf.Sign(evt.delta.y) * SoundEnvelopeZoomButton.zoomFactor);
        }

        void OnPointerLeaveEvent(PointerLeaveEvent _)
        {
            m_IsDraggingControlPoint = false;
            m_HoverValue.EnableInClassList("hide", !m_IsDraggingControlPoint);
            
            TurnOffMarkerDragging();
            TurnOffPanning();
        }

        void OnPointerUpEvent(PointerUpEvent evt)
        {
            m_IsDraggingControlPoint = false;
            m_HoverValue.EnableInClassList("hide", !m_IsDraggingControlPoint);

            TurnOffMarkerDragging();
            TurnOffPanning();
            evt.StopPropagation();
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Space:
                {
                    m_PlayButton.Click();
                    break;
                }
                case KeyCode.Delete when soundEnvelope.undoManager.IsValidSelectedPointIndex():
                    Undo.RegisterCompleteObjectUndo(soundEnvelope.undoManager, "Delete Control Point");
                    soundEnvelope.undoManager.RemoveControlPointAtIndex();
                    break;
                case KeyCode.F:
                    if (k_PanningModeSupported)
                    {
                        Undo.RegisterCompleteObjectUndo(soundEnvelope.undoManager, "Center Audio waveform");
                        soundEnvelope.undoManager.TotalPanOffset = 0;
                    }
                    break;
            }
        }

        float CalculateNewPositionY(Vector2 localPosition) => 1 - Mathf.Clamp01(localPosition.y / soundEnvelope.waveformImage.resolvedStyle.height);

        void OnPointerDown(PointerDownEvent evt)
        {
            var localPosition = ((VisualElement)evt.currentTarget).ChangeCoordinatesTo(soundEnvelope.waveformImage, evt.localPosition);
            if (!soundEnvelope.waveformImage.ContainsPoint(localPosition))
                return;
            var scaledWidth = soundEnvelope.waveformImage.resolvedStyle.width;
            var normalizedPosition = (localPosition.x / scaledWidth - 0.5f) * soundEnvelope.undoManager.CurrentZoomLevel + 0.5f + soundEnvelope.undoManager.TotalPanOffset;

            var pointerEvent = (IPointerEvent)evt;
            if (soundEnvelope.undoManager.CurrentMarkerMode == MarkerMode.Point && pointerEvent.button != (int)MouseButton.MiddleMouse)
            {
                var newPositionX = Mathf.Clamp01(normalizedPosition);
                var newPositionY = CalculateNewPositionY(localPosition);
                var index = soundEnvelope.undoManager.FindControlPointIndex(newPositionX, newPositionY);
                if (index >= 0)
                {
                    Undo.RegisterCompleteObjectUndo(soundEnvelope.undoManager, "Select Control Point");
                    soundEnvelope.undoManager.SelectedPointIndex = index;
                }
                else
                {
                    Undo.RegisterCompleteObjectUndo(soundEnvelope.undoManager, "Add Control Point");
                    soundEnvelope.undoManager.InsertControlPoint(newPositionX, newPositionY);
                }

                m_IsDraggingControlPoint = true;
                m_HoverValue.EnableInClassList("hide", !m_IsDraggingControlPoint);
                
                evt.StopPropagation();
                return;
            }

            var deltaStartMarker = Mathf.Abs(normalizedPosition - soundEnvelope.undoManager.StartMarkerPosition);
            var deltaEndMarker = Mathf.Abs(normalizedPosition - soundEnvelope.undoManager.EndMarkerPosition);
            switch (soundEnvelope.undoManager.CurrentMarkerMode)
            {
                case MarkerMode.Marker when deltaStartMarker < MarkerThreshold && deltaStartMarker < deltaEndMarker:
                    Undo.RegisterCompleteObjectUndo(soundEnvelope.undoManager, "Move Start Marker");
                    m_IsDraggingStartMarker = true;
                    break;
                case MarkerMode.Marker when deltaEndMarker < MarkerThreshold && deltaEndMarker < deltaStartMarker:
                    Undo.RegisterCompleteObjectUndo(soundEnvelope.undoManager, "Move End Marker");
                    m_IsDraggingEndMarker = true;
                    break;
                default:
                {
                    switch (pointerEvent.button)
                    {
                        case (int)MouseButton.LeftMouse or (int)MouseButton.MiddleMouse when pointerEvent.clickCount == 1:
                            if (k_PanningModeSupported)
                            {
                                Undo.RegisterCompleteObjectUndo(soundEnvelope.undoManager, "Pan Audio waveform");
                                m_IsPanning = true;
                                soundEnvelope.waveformImage.AddToClassList("cursor--grabbing");
                            }
                            break;
                        case (int)MouseButton.LeftMouse or (int)MouseButton.MiddleMouse when pointerEvent.clickCount == 2:
                            if (soundEnvelope.undoManager.CurrentMarkerMode == MarkerMode.Marker && normalizedPosition < soundEnvelope.undoManager.EndMarkerPosition)
                            {
                                Undo.RegisterCompleteObjectUndo(soundEnvelope.undoManager, "Move Start Marker");
                                soundEnvelope.undoManager.StartMarkerPosition = normalizedPosition;
                            }
                            break;
                        case (int)MouseButton.RightMouse when pointerEvent.clickCount == 2:
                            if (soundEnvelope.undoManager.CurrentMarkerMode == MarkerMode.Marker && normalizedPosition > soundEnvelope.undoManager.StartMarkerPosition)
                            {
                                Undo.RegisterCompleteObjectUndo(soundEnvelope.undoManager, "Move End Marker");
                                soundEnvelope.undoManager.EndMarkerPosition = normalizedPosition;
                            }
                            break;
                    }
                    break;
                }
            }

            m_LastMousePosition = pointerEvent.position;
            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            var localPosition = ((VisualElement)evt.currentTarget).ChangeCoordinatesTo(soundEnvelope.waveformImage, evt.localPosition);
            var scaledWidth = soundEnvelope.waveformImage.resolvedStyle.width;
            var normalizedPosition = (localPosition.x / scaledWidth - 0.5f) * soundEnvelope.undoManager.CurrentZoomLevel + 0.5f + soundEnvelope.undoManager.TotalPanOffset;

            if (soundEnvelope.undoManager.CurrentMarkerMode == MarkerMode.Marker)
            {
                var isMovingMarker = Mathf.Abs(normalizedPosition - soundEnvelope.undoManager.StartMarkerPosition) < MarkerThreshold || Mathf.Abs(normalizedPosition - soundEnvelope.undoManager.EndMarkerPosition) < MarkerThreshold;
                if (m_IsMovingMarker != isMovingMarker)
                {
                    m_IsMovingMarker = isMovingMarker;
                    soundEnvelope.waveformImage.EnableInClassList("cursor-resize", isMovingMarker);
                }
            }

            if (!m_IsDraggingControlPoint && !m_IsDraggingStartMarker && !m_IsDraggingEndMarker)
            {
                if (m_IsPanning && k_PanningModeSupported)
                {
                    Vector2 currentMousePosition = evt.position;
                    var delta = currentMousePosition - m_LastMousePosition;
                    m_LastMousePosition = currentMousePosition;

                    // Adjust pan delta considering the zoom level to ensure consistent movement
                    var panDelta = delta.x / scaledWidth * soundEnvelope.undoManager.CurrentZoomLevel;
                    soundEnvelope.undoManager.TotalPanOffset -= panDelta;
                    evt.StopPropagation();
                }

                return;
            }

            var newPosition = Mathf.Clamp01(normalizedPosition);
            switch (soundEnvelope.undoManager.CurrentMarkerMode)
            {
                case MarkerMode.Point:
                    var newPositionX = newPosition;
                    if (soundEnvelope.undoManager.IsValidSelectedPointIndex())
                    {
                        var newPositionY = CalculateNewPositionY(localPosition);
                        newPositionX = soundEnvelope.undoManager.ClampControlPointPositionX(newPositionX);
                        soundEnvelope.undoManager.SetControlPointPosition(newPositionX, newPositionY);

                        m_HoverValue.text = $"({FormatTime(soundEnvelope.totalTime * newPositionX)}, {newPositionY:0.00})";
                        m_HoverValue.style.left = evt.position.x;
                        m_HoverValue.style.top = evt.position.y;
                    }
                    break;
                case MarkerMode.Marker:
                    if (m_IsDraggingStartMarker && newPosition < soundEnvelope.undoManager.EndMarkerPosition)
                        soundEnvelope.undoManager.StartMarkerPosition = newPosition;
                    if (m_IsDraggingEndMarker && newPosition > soundEnvelope.undoManager.StartMarkerPosition)
                        soundEnvelope.undoManager.EndMarkerPosition = newPosition;
                    break;
            }

            evt.StopPropagation();
        }

        void TurnOffMarkerDragging()
        {
            soundEnvelope.waveformImage.RemoveFromClassList("cursor-resize");
            m_IsMovingMarker = false;
            if (!m_IsDraggingStartMarker && !m_IsDraggingEndMarker)
                return;

            m_IsDraggingStartMarker = false;
            m_IsDraggingEndMarker = false;
        }

        void TurnOffPanning()
        {
            if (!m_IsPanning)
                return;

            m_IsPanning = false;
            soundEnvelope.waveformImage.RemoveFromClassList("cursor--grabbing");
        }
        
        static string FormatTime(float totalSeconds) => TimeSpan.FromSeconds(totalSeconds).ToString(@"mm\:ss\.fff");
    }
}
