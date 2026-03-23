using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    class DoodlePadManipulator : Manipulator
    {
        const int k_BrushSizeStep = 2;

        Vector2Int m_Size;

        public event Action onDoodleUpdate;

        public DoodleModifierState currentState => doodlePad?.modifierState ?? DoodleModifierState.None;

        public DoodlePad doodlePad => (DoodlePad)target;

        public DoodlePadManipulator(Vector2Int size, float opacity = 1.0f)
        {
            m_Size = size;
        }

        public bool isClear
        {
            get => doodlePad.isClear;
            set => doodlePad.isClear = value;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            doodlePad.SetBrushSize(10);
            doodlePad.SetDoodleSize(m_Size);
            switch (currentState)
            {
                case DoodleModifierState.Brush:
                    doodlePad.SetBrush();
                    break;
                case DoodleModifierState.Erase:
                    doodlePad.SetEraser();
                    break;
                case DoodleModifierState.BucketFill:
                    doodlePad.SetBucketFill();
                    break;
            }

            doodlePad.onModifierStateChanged += state => onModifierStateChanged?.Invoke(state);
            doodlePad.onDoodleStart += onDoodleUpdate;
            doodlePad.onDoodleUpdate += onDoodleUpdate;
            doodlePad.onDoodleEnd += onDoodleUpdate;
        }

        protected override void UnregisterCallbacksFromTarget()
        {

        }

        public void SetBrushSize(float size)
        {
            doodlePad.SetBrushSize(size);
        }

        public void SetBrushColor(Color color)
        {
            doodlePad.SetBrushColor(color);
        }

        public float GetBrushSize() => doodlePad.brushRadius;

        public void IncreaseBrushSize()
        {
            if (doodlePad == null)
                return;

            var size = (int)doodlePad.brushRadius + k_BrushSizeStep;
            if (size > 100)
                size = 100;
            doodlePad.SetBrushSize(size);
        }

        public void DecreaseBrushSize()
        {
            if (doodlePad == null)
                return;

            var size = (int)doodlePad.brushRadius - k_BrushSizeStep;
            if (size < 0)
                size = 1;
            doodlePad.SetBrushSize(size);
        }

        public void SetValueWithoutNotify(byte[] newValue)
        {
            doodlePad?.SetValueWithoutNotify(newValue);
        }

        public event Action<DoodleModifierState> onModifierStateChanged;

        public void ToggleBrush()
        {
            if (doodlePad?.modifierState != DoodleModifierState.Brush)
                doodlePad?.SetBrush();
            else
                doodlePad?.SetNone();
        }

        public void SetBrush()
        {
            if (doodlePad?.modifierState != DoodleModifierState.Brush)
                doodlePad?.SetBrush();
        }

        public void ToggleEraser()
        {
            if (doodlePad?.modifierState != DoodleModifierState.Erase)
                doodlePad?.SetEraser();
            else
                doodlePad?.SetNone();
        }

        public void SetEraser()
        {
            if (doodlePad?.modifierState != DoodleModifierState.Erase)
                doodlePad?.SetEraser();
        }

        public void SetNone()
        {
            doodlePad?.SetNone();
        }

        public void ClearDoodle()
        {
            if (!doodlePad.isClear)
                doodlePad?.SetDoodle(null);
        }

        public void Resize(Vector2Int newSize)
        {
            doodlePad?.SetDoodleSize(newSize);
        }

        public Vector2Int GetSize()
        {
            return m_Size;
        }
    }
}
