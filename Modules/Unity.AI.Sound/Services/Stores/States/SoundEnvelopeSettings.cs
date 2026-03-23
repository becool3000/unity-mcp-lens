using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Unity.AI.Sound.Services.Stores.States
{
    [Serializable]
    sealed record SoundEnvelopeSettings
    {
        public float startPosition = 0;
        public float endPosition = 1;
        public List<Vector2> controlPoints = new();

        public bool Equals(SoundEnvelopeSettings other)
        {
            if (other is null)
                return false;

            if (startPosition != other.startPosition || endPosition != other.endPosition)
                return false;

            if (controlPoints == null && other.controlPoints == null)
                return true;

            if (controlPoints == null || other.controlPoints == null)
                return false;

            return controlPoints.SequenceEqual(other.controlPoints);
        }

        public override int GetHashCode()
        {
            var hashCode = startPosition.GetHashCode();
            hashCode = (hashCode * 397) ^ endPosition.GetHashCode();

            if (controlPoints != null)
            {
                foreach (var point in controlPoints)
                {
                    hashCode = (hashCode * 397) ^ point.GetHashCode();
                }
            }

            return hashCode;
        }
    }

    [Serializable]
    record SoundEnvelopeMarkerSettings
    {
        public SoundEnvelopeSettings envelopeSettings = new();
        public float zoomScale = 1.0f;
        public float playbackPosition;
        public bool showCursor;
        public bool showMarker = true;
        public bool showControlPoints = true;
        public bool showControlLines = true;
        public int selectedPointIndex = -1;
        public float panOffset;
        public float padding = 1;
        public float width = 128;
        public float height = 64;
        public float screenScaleFactor = 1.0f;
    }
}
