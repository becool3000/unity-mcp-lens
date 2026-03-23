using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    enum DoodleModifierState
    {
        None,
        Brush,
        Erase,
        BucketFill
    }

    [UxmlElement]
    partial class DoodlePad : VisualElement, IDisposable, INotifyValueChanged<byte[]>
    {
        public const string baseStyleName = "doodle-pad";
        public const string doodleCanvasStyleName = baseStyleName + "-canvas";
        public const string cursorStyleName = baseStyleName + "-cursor";

        public Action onDoodleStart;
        public Action onDoodleUpdate;
        public Action onDoodleEnd;

        public Action<DoodleModifierState> onModifierStateChanged;

        readonly UnityEngine.UIElements.Image m_Image;

        DoodleModifierState m_StartingState = DoodleModifierState.None;
        DoodleModifierState m_ModifierState;

        [UxmlAttribute]
        public DoodleModifierState modifierState
        {
            get => m_ModifierState;
            set
            {
                RemoveFromClassList(m_ModifierState.ToString().ToLower());
                m_ModifierState = value;
                AddToClassList(m_ModifierState.ToString().ToLower());
                UpdateDoodleCursorStyle();

                onModifierStateChanged?.Invoke(m_ModifierState);
            }
        }

        float m_BrushRadius;
        public float brushRadius => m_BrushRadius;

        int m_DoodleWidth = 512;
        int m_DoodleHeight = 512;

        Vector2 m_CurrentDoodlePosition;
        Vector2 m_LastDoodlePosition;

        readonly VisualElement m_BackgroundImageElement;
        readonly DoodleCursorOverlay m_DoodleCursorOverlay;
        bool m_IsPainting;

        readonly Painter m_Painter;

        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/DoodlePad/DoodlePad.uxml";

        public bool isClear
        {
            get => m_Painter.isClear;
            set => m_Painter.SetClear(value);
        }

        public DoodlePad()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            AddToClassList(baseStyleName);

            pickingMode = PickingMode.Ignore;

            m_Painter = new Painter(new Vector2Int(m_DoodleWidth, m_DoodleHeight));

            m_BackgroundImageElement = this.Q<VisualElement>("doodle-pad-background-image");
            m_BackgroundImageElement.style.position = Position.Absolute;

            m_Image = this.Q<UnityEngine.UIElements.Image>();
            m_Image.image = m_Painter.texture;

            m_Image.RegisterCallback<PointerDownEvent>(OnDoodleStart);
            m_Image.RegisterCallback<PointerUpEvent>(OnDoodleStop);
            m_Image.RegisterCallback<PointerLeaveEvent>(OnMousePointerLeave);
            m_Image.RegisterCallback<PointerCancelEvent>(OnMousePointerCancel);
            m_Image.RegisterCallback<PointerMoveEvent>(OnDoodleMove);

            m_DoodleCursorOverlay = this.Q<DoodleCursorOverlay>();
        }

        public Texture backgroundImage
        {
            get => m_BackgroundImageElement.resolvedStyle.backgroundImage.texture;
            set => m_BackgroundImageElement.style.backgroundImage = value as Texture2D ?? Background.FromRenderTexture(value as RenderTexture);
        }

        public float backGroundImageOpacity
        {
            get => m_BackgroundImageElement.resolvedStyle.opacity;
            set => m_BackgroundImageElement.style.opacity = value;
        }

        public void SetNone() => modifierState = DoodleModifierState.None;
        public void SetBrush() => modifierState = DoodleModifierState.Brush;
        public void SetEraser() => modifierState = DoodleModifierState.Erase;
        public void SetBucketFill() => modifierState = DoodleModifierState.BucketFill;

        public void SetBrushSize(float newBrushRadius)
        {
            m_BrushRadius = newBrushRadius;
            m_Painter.brushRadius = m_BrushRadius;
            UpdateDoodleCursorStyle();
        }

        public Color brushColor => m_Painter.paintColor;

        public void SetBrushColor(Color color)
        {
            m_Painter.paintColor = color;
        }

        public void SetDoodle(byte[] doodle)
        {
            SetValueWithoutNotify(doodle);
            SendValueChangedEvent(doodle);
        }

        void InitializeWithData(byte[] doodle)
        {
            m_Painter.InitializeWithData(doodle);

            m_DoodleWidth = m_Painter.size.x;
            m_DoodleHeight = m_Painter.size.y;

            m_Image.image = m_Painter.texture;
            m_Image.MarkDirtyRepaint();
        }

        void OnMousePointerLeave(PointerLeaveEvent evt)
        {
            m_DoodleCursorOverlay.style.display = DisplayStyle.None;
        }

        void OnMousePointerCancel(PointerCancelEvent evt)
        {
            m_DoodleCursorOverlay.style.display = DisplayStyle.None;
        }

        void OnDoodleStart(PointerDownEvent evt)
        {
            if (m_ModifierState == DoodleModifierState.None)
                return;

            if (evt.altKey)
                return;

            m_StartingState = modifierState;
            m_WasActionKeyPressed = evt.actionKey;
            if (m_WasActionKeyPressed)
            {
                SwitchBrush();
            }

            m_LastDoodlePosition = m_CurrentDoodlePosition = evt.localPosition;

            var currentPosition = GetPosition(m_CurrentDoodlePosition);

            switch (m_ModifierState)
            {
                case DoodleModifierState.Brush:
                case DoodleModifierState.Erase:
                    if (evt.button == (int)MouseButton.LeftMouse)
                    {
                        m_IsPainting = true;
                        m_Image.CaptureMouse();
                        if (m_ModifierState == DoodleModifierState.Brush)
                            m_Painter.Paint(currentPosition, currentPosition);
                        else if (m_ModifierState == DoodleModifierState.Erase)
                            m_Painter.Erase(currentPosition, currentPosition);
                        onDoodleStart?.Invoke();
                    }
                    else if (evt.button == (int)MouseButton.RightMouse)
                    {
                        m_Painter.DoodleFill(currentPosition);
                        onDoodleUpdate?.Invoke();
                        SendValueChangedEvent();
                    }

                    break;
                case DoodleModifierState.BucketFill:
                    m_Painter.DoodleFill(currentPosition);
                    onDoodleUpdate?.Invoke();
                    SendValueChangedEvent();
                    break;
            }

            m_Image.MarkDirtyRepaint();

            evt.StopPropagation();
        }

        void OnDoodleStop(PointerUpEvent evt)
        {
            if (m_ModifierState == DoodleModifierState.None || m_StartingState == DoodleModifierState.None)
                return;

            if (evt.button != (int)MouseButton.LeftMouse)
                return;

            m_IsPainting = false;
            modifierState = m_StartingState;
            m_StartingState = DoodleModifierState.None;

            m_Painter.UpdateTextureData();
            schedule.Execute(SendValueChangedEvent);

            m_Image.ReleaseMouse();
            onDoodleEnd?.Invoke();

            evt.StopPropagation();
        }

        void SendValueChangedEvent()
        {
            using var evt = ChangeEvent<byte[]>.GetPooled(null, m_Painter.GetTextureData().EncodeToPNG());
            evt.target = this;
            SendEvent(evt);
        }

        void SendValueChangedEvent(IReadOnlyList<byte> doodle)
        {
            using var evt = ChangeEvent<byte[]>.GetPooled(null, doodle?.ToArray());
            evt.target = this;
            SendEvent(evt);
        }

        bool m_WasActionKeyPressed;

        void OnDoodleMove(PointerMoveEvent evt)
        {
            if (m_ModifierState == DoodleModifierState.None)
                return;

            m_CurrentDoodlePosition = evt.localPosition;
            UpdateDoodleCursorStyle();
            if (!m_IsPainting)
                return;

            // If control key on windows or command key on mac is pressed, switch between brush and erase
            if (m_WasActionKeyPressed != evt.actionKey)
            {
                SwitchBrush();
            }

            m_WasActionKeyPressed = evt.actionKey;

            var currentPosition = (Vector3)GetPosition(m_CurrentDoodlePosition);
            var previousPosition = (Vector3)GetPosition(m_LastDoodlePosition);

            if (m_ModifierState == DoodleModifierState.Brush)
                m_Painter.Paint(previousPosition, currentPosition);
            else if (m_ModifierState == DoodleModifierState.Erase)
                m_Painter.Erase(previousPosition, currentPosition);

            m_LastDoodlePosition = m_CurrentDoodlePosition;

            onDoodleUpdate?.Invoke();

            evt.StopPropagation();
        }

        void SwitchBrush()
        {
            if (modifierState == DoodleModifierState.Brush)
            {
                modifierState = DoodleModifierState.Erase;
            }
            else if (modifierState == DoodleModifierState.Erase)
            {
                modifierState = DoodleModifierState.Brush;
            }
        }

        float GetBrushSize(float brushSize)
        {
            var size = m_Image.contentRect.size;
            var parentAspectRatio = size.x / size.y;
            var imageAspectRatio = (float)m_DoodleWidth / m_DoodleHeight;
            if (imageAspectRatio > parentAspectRatio)
            {
                // width match
                brushSize *= size.x / m_DoodleWidth;
            }
            else
            {
                // height match
                brushSize *= size.y / m_DoodleHeight;
            }

            return brushSize;
        }

        Vector2 GetPosition(Vector3 evtLocalPosition)
        {
            var size = m_Image.contentRect.size;
            var pos = evtLocalPosition;
            pos.y = size.y - pos.y;

            var parentAspectRatio = size.x / size.y;
            var imageAspectRatio = (float)m_DoodleWidth / m_DoodleHeight;
            var realImageWidth = size.x;
            var realImageHeight = size.y;
            if (imageAspectRatio > parentAspectRatio)
            {
                // width match
                realImageHeight = size.x / imageAspectRatio;
                pos.y -= (size.y - realImageHeight) * 0.5f;
            }
            else
            {
                // height match
                realImageWidth = size.y * imageAspectRatio;
                pos.x -= (size.x - realImageWidth) * 0.5f;
            }

            pos.x *= m_DoodleWidth / realImageWidth;
            pos.y *= m_DoodleHeight / realImageHeight;

            return pos;
        }

        void UpdateDoodleCursorStyle()
        {
            var isVisible = m_ModifierState is not DoodleModifierState.None and not DoodleModifierState.BucketFill;
            var currentPos = (Vector3)GetPosition(m_CurrentDoodlePosition);
            var withinDoodlingArea = currentPos.x >= 0 && currentPos.x < m_DoodleWidth && currentPos.y >= 0 && currentPos.y < m_DoodleHeight;
            if (withinDoodlingArea)
            {
                var doodleImageRadius = GetBrushSize(m_BrushRadius);

                m_DoodleCursorOverlay.style.position = Position.Absolute;
                m_DoodleCursorOverlay.style.top = m_CurrentDoodlePosition.y - doodleImageRadius + m_Image.resolvedStyle.paddingTop;
                m_DoodleCursorOverlay.style.left = m_CurrentDoodlePosition.x - doodleImageRadius;
                m_DoodleCursorOverlay.style.width = m_DoodleCursorOverlay.style.height = doodleImageRadius * 2;
                m_DoodleCursorOverlay.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
            }
            else
                m_DoodleCursorOverlay.style.display = DisplayStyle.None;
            m_DoodleCursorOverlay.MarkDirtyRepaint();
        }

        public void SetValueWithoutNotify(byte[] newValue)
        {
            InitializeWithData(newValue);
        }

        public byte[] value
        {
            get => m_Painter.GetTextureData().EncodeToPNG();
            set => SetDoodle(value);
        }

        public void Fill()
        {
            m_Painter.DoodleFill();
            SendValueChangedEvent();
        }

        public void SetDoodleSize(Vector2Int newSize)
        {
            m_DoodleWidth = newSize.x;
            m_DoodleHeight = newSize.y;

            m_Painter.Resize(newSize);

            m_Image.image = m_Painter.texture;
            m_Image.MarkDirtyRepaint();
        }

        public Vector2Int GetDoodleSize() => new Vector2Int(m_DoodleWidth, m_DoodleHeight);

        public void Dispose()
        {
            m_Painter?.Dispose();
        }
    }
}
