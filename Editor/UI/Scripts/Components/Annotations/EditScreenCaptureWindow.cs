using System;
using System.Collections.Generic;
using Unity.AI.Assistant.Annotations;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Context;
using Unity.AI.Assistant.Utils;
using Unity.AI.Toolkit;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;
// ReSharper disable InconsistentNaming

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    /// <summary>
    /// Editor window for editing screen captures with annotation strokes.
    /// Displays a captured screenshot as background with strokes rendered on a separate layer.
    /// Supports overlays for the annotation toolbar.
    /// </summary>
    class EditScreenCaptureWindow : EditorWindow, ISupportsOverlays
    {
        // Singleton instance for overlay access
        public static EditScreenCaptureWindow Instance { get; private set; }

        // Event fired when a screenshot is sent/cleared (when user hits Send button)
        internal static event Action<VirtualAttachment> OnScreenshotSent;

        // Static cache for pen settings persistence across window recreation
        static Color s_CachedPenColor = new (1f, 48f / 255f, 0f); // Red-orange default
        static float s_CachedPenWidth = 8f;

        /// <summary>
        /// Called when a screenshot is sent/cleared via the Send button. Used to close the window when the screenshot is sent.
        /// </summary>
        internal static void NotifyScreenshotSent(VirtualAttachment sentAttachment)
        {
            OnScreenshotSent?.Invoke(sentAttachment);
        }

        const string k_WindowTitle = "Edit Screen Capture";

        // Layers
        Texture2D m_BackgroundCapture;
        RenderTexture m_StrokeLayer;
        StrokeRenderer m_StrokeRenderer;
        StrokeCollection m_Strokes;

        // UI Elements
        VisualElement m_CanvasContainer;
        VisualElement m_ViewportContainer; // Container for zoom/pan transform
        Image m_BackgroundImage;
        Image m_StrokeImage;


        // Drawing state
        bool m_IsDrawing;
        Vector2 m_CurrentMousePosition;
        bool m_IsMouseOverCanvas;

        // Zoom and pan state
        float m_ZoomLevel = 1f;
        Vector2 m_PanOffset = Vector2.zero;
        bool m_IsPanning;
        Vector2 m_LastPanMousePosition = Vector2.zero;
        bool m_IsSpaceKeyHeld;
        const float k_MinZoom = 0.1f;
        const float k_MaxZoom = 10f;
        const float k_ZoomSpeed = 1.1f;

        // Default pen settings
        static readonly Color k_DefaultPenColor = new (1f, 48f / 255f, 0f); // Red-orange
        const float k_DefaultPenWidth = 8f;

        // Current tool settings
        Color m_PenColor = k_DefaultPenColor;
        float m_PenWidth = k_DefaultPenWidth;

        /// <summary>Current pen color for drawing strokes.</summary>
        public Color PenColor
        {
            get => m_PenColor;
            set
            {
                m_PenColor = value;
                m_SerializedPenColor = value; // Save to serialized field for domain reload persistence
                s_CachedPenColor = value; // Cache for static access
                if (m_Strokes != null)
                    m_Strokes.CurrentColor = value;
            }
        }

        /// <summary>Current pen width for drawing strokes.</summary>
        public float PenWidth
        {
            get => m_PenWidth;
            set
            {
                m_PenWidth = value;
                m_SerializedPenWidth = value; // Save to serialized field for domain reload persistence
                s_CachedPenWidth = value; // Cache for static access
                if (m_Strokes != null)
                    m_Strokes.CurrentWidth = value;
            }
        }

        /// <summary>Get serialized pen color (for overlay to read during domain reload).</summary>
        public Color GetSerializedPenColor() => m_SerializedPenColor;

        /// <summary>Get serialized pen width (for overlay to read during domain reload).</summary>
        public float GetSerializedPenWidth() => m_SerializedPenWidth;

        /// <summary>Get the current stroke collection (for overlay to check undo/redo availability).</summary>
        public StrokeCollection GetStrokeCollection() => m_Strokes;

        /// <summary>Sync pen settings from overlay (overlay has most reliable serialization).</summary>
        internal void SyncPenSettingsFromOverlay(Color color, float width)
        {
            // Update the properties which will update both instance, serialized fields, and static cache
            PenColor = color;
            PenWidth = width;

            // Ensure strokes are using the correct color/width
            if (m_Strokes != null)
            {
                m_Strokes.CurrentColor = color;
                m_Strokes.CurrentWidth = width;
            }
        }

        // Screen capture metadata
        int m_CaptureWidth;
        int m_CaptureHeight;

        // Track the original virtual attachment being edited (if any)
        VirtualAttachment m_OriginalAttachment;

        // Track if we've attempted to highlight to avoid repeated attempts
        int m_HighlightAttempts;
        const int k_MaxHighlightAttempts = 10; // Try for ~10 frames

        // Persisted screenshot data for domain reload
        [SerializeField]
        byte[] m_SerializedScreenshotData;

        // Persisted original attachment metadata for domain reload
        [SerializeField]
        string m_SerializedAttachmentPayload;

        [SerializeField]
        string m_SerializedAttachmentType;

        [SerializeField]
        string m_SerializedAttachmentDisplayName;

        // Persisted stroke data for domain reload
        [SerializeField]
        SerializedStrokeData[] m_SerializedStrokes = Array.Empty<SerializedStrokeData>();

        // Persisted pen settings for domain reload
        [SerializeField]
        Color m_SerializedPenColor = new (1f, 48f / 255f, 0f); // Red-orange default

        [SerializeField]
        float m_SerializedPenWidth = 8f;

        // Persisted zoom and pan data for domain reload
        [SerializeField]
        float m_SerializedZoomLevel = 1f;

        [SerializeField]
        Vector2 m_SerializedPanOffset = Vector2.zero;

        /// <summary>
        /// Opens the Edit Screen Capture window with a fresh capture.
        /// </summary>
        public static void Open()
        {
            var window = GetWindow<EditScreenCaptureWindow>();
            window.titleContent = new GUIContent(k_WindowTitle);
            window.minSize = new Vector2(400, 300);
            window.Show();

            // Delay capture to next frame so window isn't captured
            EditorTask.delayCall += () =>
            {
                window.CaptureScreen();
            };
        }

        /// <summary>
        /// Opens the window with an existing screenshot.
        /// </summary>
        internal static void OpenWithScreenshot(byte[] pngData, VirtualAttachment originalAttachment = null)
        {
            var window = GetWindow<EditScreenCaptureWindow>();
            window.titleContent = new GUIContent(k_WindowTitle);
            window.minSize = new Vector2(400, 300);

            // Check if the same screenshot is already open to prevent losing annotations
            if (originalAttachment != null && window.m_OriginalAttachment != null &&
                window.m_OriginalAttachment.Payload == originalAttachment.Payload)
            {
                // Same screenshot is already open, just bring window to focus and highlight
                ContextElement.HighlightScreenshotByVirtualAttachment(originalAttachment);
                window.Focus();
                return;
            }

            window.m_OriginalAttachment = originalAttachment;

            // Serialize the attachment for persistence across domain reloads
            if (originalAttachment != null)
            {
                window.m_SerializedAttachmentPayload = originalAttachment.Payload;
                window.m_SerializedAttachmentType = originalAttachment.Type;
                window.m_SerializedAttachmentDisplayName = originalAttachment.DisplayName;

                // Highlight the context row for this screenshot
                ContextElement.HighlightScreenshotByVirtualAttachment(originalAttachment);
            }

            window.Show();
            window.LoadScreenshot(pngData);
        }

        /// <summary>
        /// Captures the full screen (entire desktop) to PNG bytes.
        /// Uses the native capture implementation that captures the full screen including taskbar.
        /// Used for annotation feature.
        /// </summary>
        /// <returns>PNG bytes of the captured screen, or null if capture failed</returns>
        public static byte[] CaptureFullScreenToBytes()
        {
            try
            {
                var rgba = NativeCapture.CaptureDesktopRGBA(out int width, out int height);
                if (rgba == null || rgba.Length == 0)
                {
                    InternalLog.LogError("[EditScreenCapture] Failed to capture full screen");
                    return null;
                }

                // Convert RGBA to PNG
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                Color[] colors = new Color[width * height];

                // Convert RGBA bytes to Color array
                for (int i = 0; i < rgba.Length; i += 4)
                {
                    int pixelIndex = i / 4;
                    colors[pixelIndex] = new Color(
                        rgba[i] / 255f,
                        rgba[i + 1] / 255f,
                        rgba[i + 2] / 255f,
                        rgba[i + 3] / 255f
                    );
                }

                texture.SetPixels(colors);
                texture.Apply(false);

                var pngBytes = texture.EncodeToPNG();
                DestroyImmediate(texture);

                return pngBytes;
            }
            catch (Exception e)
            {
                InternalLog.LogError($"[EditScreenCapture] Full screen capture failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Captures all focused Unity Editor windows to PNG bytes.
        /// Uses ScreenContextUtility to capture and stitch focused windows only.
        /// Used for screenshot feature - no OS background or taskbars.
        /// </summary>
        /// <returns>PNG bytes of the captured focused windows, or null if capture failed</returns>
        public static byte[] CaptureScreenToBytes()
        {
            try
            {
                // Use ScreenContextUtility which properly captures focused windows and stitches them
                var screenContext = ScreenContextUtility.CaptureScreenContext(includeScreenshots: true, saveToFile: false);
                if (screenContext.Screenshot == null || screenContext.Screenshot.Length == 0)
                {
                    InternalLog.LogError("[EditScreenCapture] Failed to capture focused windows");
                    return null;
                }

                return screenContext.Screenshot;
            }
            catch (Exception e)
            {
                InternalLog.LogError($"[EditScreenCapture] Screen capture failed: {e.Message}");
                return null;
            }
        }

        void OnEnable()
        {
            Instance = this;

            // Subscribe to screenshot sent event to close window when user hits Send
            OnScreenshotSent += OnScreenshotSent_Handler;

            // Subscribe to play mode state changes to reload cursors after domain reload
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            // IMPORTANT: Always restore pen settings from serialized fields BEFORE CreateUI()
            // If serialized values are defaults, try to read from overlay's pending values
            // (This handles the case where overlay CreatePanelContent runs before window OnEnable)
            if (m_SerializedPenColor == k_DefaultPenColor && EditScreenCaptureToolbarOverlay.pendingPenColor != k_DefaultPenColor)
            {
                m_SerializedPenColor = EditScreenCaptureToolbarOverlay.pendingPenColor;
                m_SerializedPenWidth = EditScreenCaptureToolbarOverlay.pendingPenWidth;
            }

            PenColor = m_SerializedPenColor;
            PenWidth = m_SerializedPenWidth;

            m_Strokes = new StrokeCollection
            {
                CurrentColor = m_PenColor,
                CurrentWidth = m_PenWidth
            };

            m_StrokeRenderer = new StrokeRenderer();
            m_StrokeRenderer.Initialize();

            // Create UI (overlay will read pen settings from window in CreatePanelContent)
            CreateUI();

            // Hook into update to refresh cursor rendering
            EditorApplication.update += OnEditorUpdate;

            // Ensure pen settings are synced from overlay after a frame
            // (overlay's CreatePanelContent might have run or might run after this)
            EditorTask.delayCall += () =>
            {
                if (Instance == this && m_Strokes != null)
                {
                    // Ensure m_Strokes has the correct color/width (in case overlay modified them)
                    if (m_Strokes.CurrentColor != m_PenColor || !Mathf.Approximately(m_Strokes.CurrentWidth, m_PenWidth))
                    {
                        m_Strokes.CurrentColor = m_PenColor;
                        m_Strokes.CurrentWidth = m_PenWidth;
                    }
                }
            };

            // Restore screenshot and strokes from serialized data after domain reload
            if (m_SerializedScreenshotData != null && m_SerializedScreenshotData.Length > 0)
            {
                EditorTask.delayCall += () =>
                {
                    // Load screenshot WITHOUT clearing strokes (we'll restore them separately)
                    LoadScreenshot(m_SerializedScreenshotData, clearStrokes: false);
                    RestoreSerializedStrokes();
                    RestoreSerializedAttachment();
                    // Force refresh UI images to ensure they're visible after domain reload
                    RefreshUI();
                    // Restore zoom/pan AFTER refreshing UI to ensure proper rendering
                    RestoreZoomAndPan();
                };
            }
            else if (!string.IsNullOrEmpty(m_SerializedAttachmentPayload))
            {
                // If we have attachment data but no screenshot data, just restore and highlight the attachment
                // This handles the case where AssistantWindow is reopened without a domain reload
                EditorTask.delayCall += () =>
                {
                    RestoreSerializedAttachment();
                };
            }
        }

        void OnGUI()
        {
            // Handle keyboard shortcuts before UIElements processes them
            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.control && Event.current.keyCode == KeyCode.Z)
                {
                    UndoStroke();
                    Event.current.Use();
                }
                else if (Event.current.control && Event.current.keyCode == KeyCode.Y)
                {
                    RedoStroke();
                    Event.current.Use();
                }
                else if (Event.current.keyCode == KeyCode.F)
                {
                    FitCaptureToView();
                    Event.current.Use();
                }
            }
        }

        void FitCaptureToView()
        {
            if (m_BackgroundCapture == null)
                return;

            // Reset zoom and pan to default - let ScaleToFit on Image elements handle the fitting
            m_ZoomLevel = 1f;
            m_PanOffset = Vector2.zero;

            UpdateViewportTransform();
            RenderStrokes();
            SerializeStrokes();
        }

        void OnDisable()
        {
            OnDisableCleanup();
        }

        /// <summary>
        /// Called by Unity when the user chooses to save changes before closing the window.
        /// </summary>
        public override void SaveChanges()
        {
            Done();
        }

        void OnDisableCleanup()
        {
            // Unsubscribe from screenshot sent event
            OnScreenshotSent -= OnScreenshotSent_Handler;

            // Unsubscribe from play mode state changes
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

            if (Instance == this)
                Instance = null;
            EditorApplication.update -= OnEditorUpdate;
            Cleanup();
        }

        void OnDestroy()
        {
            // Remove highlight from the context row when window is destroyed
            if (m_OriginalAttachment != null)
            {
                ContextElement.RemoveHighlightByVirtualAttachment(m_OriginalAttachment);
            }

            if (Instance == this)
                Instance = null;
            Cleanup();
        }

        void Cleanup()
        {
            m_StrokeRenderer?.Cleanup();
            m_StrokeRenderer = null;

            if (m_BackgroundCapture != null)
            {
                DestroyImmediate(m_BackgroundCapture);
                m_BackgroundCapture = null;
            }

            if (m_StrokeLayer != null)
            {
                m_StrokeLayer.Release();
                DestroyImmediate(m_StrokeLayer);
                m_StrokeLayer = null;
            }

            // Clean up cursors
            // Clear cursor style from UI element
            if (m_StrokeImage != null)
            {
                m_StrokeImage.style.cursor = StyleKeyword.Auto;
                m_StrokeImage.RemoveFromClassList("annotation-pan-cursor");
            }

            m_Strokes = null;
        }

        void OnScreenshotSent_Handler(VirtualAttachment sentAttachment)
        {
            // If the screenshot sent is the one currently being edited, close the window
            if (m_OriginalAttachment != null && sentAttachment.Payload == m_OriginalAttachment.Payload)
            {
                Close();
            }
        }

        void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            // Play mode changes handling
            // (USS-based pan cursor doesn't require reloading after domain reload)
        }

        void OnEditorUpdate()
        {
            // Update pan cursor class based on panning state
            UpdatePanCursorClass();

            // Continuously refresh cursor rendering when mouse is over canvas and not drawing or panning
            if (m_IsMouseOverCanvas && !m_IsDrawing && !m_IsPanning)
            {
                RenderStrokesWithCursor();
            }

            // Initialize m_OriginalAttachment from serialized data if not already set
            // The serialized fields are preserved by Unity across window reopens
            if (m_OriginalAttachment == null && !string.IsNullOrEmpty(m_SerializedAttachmentPayload))
            {
                m_OriginalAttachment = new VirtualAttachment(
                    m_SerializedAttachmentPayload,
                    m_SerializedAttachmentType,
                    m_SerializedAttachmentDisplayName,
                    metadata: null
                );
                m_HighlightAttempts = 0; // Reset to start highlighting attempts
            }

            // Try to highlight the attachment if we haven't succeeded yet
            if (m_HighlightAttempts < k_MaxHighlightAttempts && m_OriginalAttachment != null)
            {
                m_HighlightAttempts++;
                ContextElement.HighlightScreenshotByVirtualAttachment(m_OriginalAttachment);
            }
        }

        void CreateUI()
        {
            var root = rootVisualElement;
            root.Clear();

            // Load stylesheet
            var stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.unity.ai.assistant/Editor/UI/Styles/EditScreenCaptureWindow.uss");
            if (stylesheet != null)
            {
                root.styleSheets.Add(stylesheet);
            }

            // Create simple canvas UI - toolbar is provided by overlay
            root.style.flexDirection = FlexDirection.Column;
            root.style.flexGrow = 1;

            // Set background color following Unity design foundation color palette
            if (EditorGUIUtility.isProSkin)
            {
                // Dark theme - Window Background #383838
                root.style.backgroundColor = new Color(56f / 255f, 56f / 255f, 56f / 255f);
            }
            else
            {
                // Light theme - Window Background #C8C8C8
                root.style.backgroundColor = new Color(200f / 255f, 200f / 255f, 200f / 255f);
            }

            // Register keyboard handler on root to intercept Ctrl+Z and Ctrl+Y
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            // Canvas container
            m_CanvasContainer = new VisualElement();
            m_CanvasContainer.name = "canvas-container";
            m_CanvasContainer.style.flexGrow = 1;
            m_CanvasContainer.style.overflow = Overflow.Hidden;
            m_CanvasContainer.style.backgroundColor = Color.clear;
            root.Add(m_CanvasContainer);

            // Background image layer
            m_BackgroundImage = new Image();
            m_BackgroundImage.name = "background-image";
            m_BackgroundImage.style.position = Position.Absolute;
            m_BackgroundImage.style.left = 0;
            m_BackgroundImage.style.top = 0;
            m_BackgroundImage.style.right = 0;
            m_BackgroundImage.style.bottom = 0;
            m_BackgroundImage.scaleMode = ScaleMode.ScaleToFit;
            m_BackgroundImage.style.backgroundColor = Color.clear;
            m_CanvasContainer.Add(m_BackgroundImage);

            // Stroke image layer
            m_StrokeImage = new Image();
            m_StrokeImage.name = "stroke-image";
            m_StrokeImage.style.position = Position.Absolute;
            m_StrokeImage.style.left = 0;
            m_StrokeImage.style.top = 0;
            m_StrokeImage.style.right = 0;
            m_StrokeImage.style.bottom = 0;
            m_StrokeImage.scaleMode = ScaleMode.ScaleToFit;

            m_CanvasContainer.Add(m_StrokeImage);

            SetupCanvasEvents();
        }

        void SetupCanvasEvents()
        {
            if (m_CanvasContainer == null) return;

            // Create viewport container for zoom/pan
            m_ViewportContainer = new VisualElement();
            m_ViewportContainer.name = "viewport-container";
            m_ViewportContainer.style.position = Position.Absolute;
            m_ViewportContainer.style.left = 0;
            m_ViewportContainer.style.top = 0;
            m_ViewportContainer.style.right = 0;
            m_ViewportContainer.style.bottom = 0;

            // Move background and stroke images into viewport container
            if (m_BackgroundImage != null && m_BackgroundImage.parent == m_CanvasContainer)
            {
                m_CanvasContainer.Remove(m_BackgroundImage);
                m_ViewportContainer.Add(m_BackgroundImage);
            }

            if (m_StrokeImage != null && m_StrokeImage.parent == m_CanvasContainer)
            {
                m_CanvasContainer.Remove(m_StrokeImage);
                m_ViewportContainer.Add(m_StrokeImage);
            }

            m_CanvasContainer.Add(m_ViewportContainer);

            m_CanvasContainer.RegisterCallback<PointerDownEvent>(OnPointerDown);
            m_CanvasContainer.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            m_CanvasContainer.RegisterCallback<PointerUpEvent>(OnPointerUp);
            m_CanvasContainer.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
            m_CanvasContainer.RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            m_CanvasContainer.RegisterCallback<WheelEvent>(OnMouseWheel);
            m_CanvasContainer.RegisterCallback<KeyDownEvent>(OnCanvasKeyDown);
            m_CanvasContainer.RegisterCallback<KeyUpEvent>(OnCanvasKeyUp);
            m_CanvasContainer.focusable = true;
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (m_BackgroundCapture == null) return;

            m_CurrentMousePosition = evt.localPosition;

            // Middle mouse button for panning (Windows/external mice) or Space+left-click for Mac trackpad
            bool isMiddleClick = evt.button == (int)MouseButton.MiddleMouse;
            bool isSpaceLeftClick = evt.button == (int)MouseButton.LeftMouse && m_IsSpaceKeyHeld;

            if (isMiddleClick || isSpaceLeftClick)
            {
                m_IsPanning = true;
                m_LastPanMousePosition = m_CurrentMousePosition;
                m_CanvasContainer.CapturePointer(evt.pointerId);
                evt.StopPropagation();
                return;
            }

            if (evt.button != (int)MouseButton.LeftMouse) return;

            m_IsDrawing = true;
            m_CanvasContainer.CapturePointer(evt.pointerId);

            var canvasPos = ScreenToCanvasPosition(evt.localPosition);

            m_Strokes.CurrentColor = m_PenColor;
            m_Strokes.CurrentWidth = m_PenWidth;
            m_Strokes.BeginStroke(canvasPos);

            RenderStrokesWithCursor();
            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            m_CurrentMousePosition = evt.localPosition;

            // Handle panning
            if (m_IsPanning)
            {
                Vector2 delta = m_CurrentMousePosition - m_LastPanMousePosition;
                m_PanOffset += delta;
                m_LastPanMousePosition = m_CurrentMousePosition;
                UpdateViewportTransform();
                evt.StopPropagation();
                return;
            }

            if (!m_IsDrawing)
            {
                // Show cursor circle even when not drawing
                RenderStrokesWithCursor();
                return;
            }

            var canvasPos = ScreenToCanvasPosition(evt.localPosition);
            m_Strokes.ContinueStroke(canvasPos);

            RenderStrokesWithCursor();
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            // Handle pan release (middle mouse or Space+left-click)
            bool isMiddleClick = evt.button == (int)MouseButton.MiddleMouse;
            bool isLeftClick = evt.button == (int)MouseButton.LeftMouse;

            if (m_IsPanning && (isMiddleClick || isLeftClick))
            {
                m_IsPanning = false;
                m_CanvasContainer.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
                return;
            }

            if (!m_IsDrawing) return;

            m_IsDrawing = false;
            m_CanvasContainer.ReleasePointer(evt.pointerId);

            m_Strokes.EndStroke();
            hasUnsavedChanges = true;
            SerializeStrokes();
            RenderStrokes();

            // Update overlay button states after stroke is completed
            EditScreenCaptureToolbarOverlay.Instance?.UpdateUndoRedoButtonStates();

            evt.StopPropagation();
        }

        void UpdatePanCursorClass()
        {
            var root = rootVisualElement;
            if (root == null)
                return;

            // Add class if panning, remove if not (applies to root and all children)
            if (m_IsPanning)
            {
                if (!root.ClassListContains("annotation-pan-cursor"))
                {
                    root.AddToClassList("annotation-pan-cursor");
                }
            }
            else
            {
                if (root.ClassListContains("annotation-pan-cursor"))
                {
                    root.RemoveFromClassList("annotation-pan-cursor");
                }
            }
        }

        void OnPointerEnter(PointerEnterEvent evt)
        {
            m_IsMouseOverCanvas = true;
        }

        void OnPointerLeave(PointerLeaveEvent evt)
        {
            m_IsMouseOverCanvas = false;
            if (m_IsDrawing)
            {
                m_Strokes.EndStroke();
                m_IsDrawing = false;
                RenderStrokes();
            }
            else if (!m_IsPanning)
            {
                // Redraw without cursor when leaving (unless panning)
                RenderStrokes();
            }
            // Pan cursor will be maintained by UpdatePanCursorClass
        }

        void OnMouseWheel(WheelEvent evt)
        {
            if (m_BackgroundCapture == null || m_ViewportContainer == null) return;

            // Get mouse position in screen space (viewport space)
            var mouseScreenPos = m_CurrentMousePosition;

            // Calculate zoom delta (positive scroll = zoom in)
            var zoomDelta = evt.delta.y > 0 ? 1f / k_ZoomSpeed : k_ZoomSpeed;
            var newZoom = m_ZoomLevel * zoomDelta;
            newZoom = Mathf.Clamp(newZoom, k_MinZoom, k_MaxZoom);

            if (Mathf.Approximately(newZoom, m_ZoomLevel))
                return;

            // Convert screen position to world position before zoom
            var worldPosBeforeZoom = (mouseScreenPos - m_PanOffset) / m_ZoomLevel;

            m_ZoomLevel = newZoom;

            // Calculate new pan so that the world position is back at the same screen position
            var newPanOffset = mouseScreenPos - (worldPosBeforeZoom * m_ZoomLevel);
            m_PanOffset = newPanOffset;

            UpdateViewportTransform();
            evt.StopPropagation();
        }

        void UpdateViewportTransform()
        {
            if (m_ViewportContainer == null) return;

            // Apply zoom and pan transform
            m_ViewportContainer.style.transformOrigin = new TransformOrigin(0, 0);
            m_ViewportContainer.style.translate = new Translate(m_PanOffset.x, m_PanOffset.y);
            m_ViewportContainer.style.scale = new Scale(new Vector3(m_ZoomLevel, m_ZoomLevel, 1f));
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            // Check for Ctrl (Windows/Linux) or Cmd (Mac)
            bool isCmdOrCtrl = evt.ctrlKey || evt.commandKey;

            switch (isCmdOrCtrl)
            {
                // Handle Ctrl+Z / Cmd+Z for undo
                case true when evt.keyCode == KeyCode.Z:
                    UndoStroke();
                    evt.StopPropagation();
                    evt.StopImmediatePropagation();
                    return;

                // Handle Ctrl+Y / Cmd+Y for redo
                case true when evt.keyCode == KeyCode.Y:
                    RedoStroke();
                    evt.StopPropagation();
                    evt.StopImmediatePropagation();
                    return;
            }

            // Handle F key for fit to view
            if (evt.keyCode == KeyCode.F)
            {
                FitCaptureToView();
                evt.StopPropagation();
                evt.StopImmediatePropagation();
                return;
            }
        }

        void OnCanvasKeyDown(KeyDownEvent evt)
        {
            // Track Space key for Mac trackpad panning
            if (evt.keyCode == KeyCode.Space)
            {
                m_IsSpaceKeyHeld = true;
                evt.StopPropagation();
            }
        }

        void OnCanvasKeyUp(KeyUpEvent evt)
        {
            // Release Space key tracking
            if (evt.keyCode == KeyCode.Space)
            {
                m_IsSpaceKeyHeld = false;
                evt.StopPropagation();
            }
        }

        Vector2 ScreenToCanvasPosition(Vector2 localPosition)
        {
            if (m_BackgroundCapture == null || m_CanvasContainer == null)
                return localPosition;

            // Account for zoom and pan transforms
            // First, reverse the pan offset
            var adjustedPos = localPosition - m_PanOffset;

            // Then, reverse the zoom
            adjustedPos /= m_ZoomLevel;

            // Get canvas container size
            var containerRect = m_CanvasContainer.contentRect;
            if (containerRect.width <= 0 || containerRect.height <= 0)
                return adjustedPos;

            // Calculate aspect ratio and scaling
            var containerAspect = containerRect.width / containerRect.height;
            var imageAspect = (float)m_CaptureWidth / m_CaptureHeight;

            float displayWidth, displayHeight;
            float offsetX = 0, offsetY = 0;

            if (imageAspect > containerAspect)
            {
                // Image is wider - fit to width
                displayWidth = containerRect.width;
                displayHeight = containerRect.width / imageAspect;
                offsetY = (containerRect.height - displayHeight) / 2;
            }
            else
            {
                // Image is taller - fit to height
                displayHeight = containerRect.height;
                displayWidth = containerRect.height * imageAspect;
                offsetX = (containerRect.width - displayWidth) / 2;
            }

            // Convert from container space to image space
            float relX = (adjustedPos.x - offsetX) / displayWidth;
            float relY = (adjustedPos.y - offsetY) / displayHeight;

            // Clamp to valid range
            relX = Mathf.Clamp01(relX);
            relY = Mathf.Clamp01(relY);

            return new Vector2(relX * m_CaptureWidth, relY * m_CaptureHeight);
        }


        void RenderStrokes()
        {
            if (m_StrokeLayer == null || m_StrokeRenderer == null || m_Strokes == null)
                return;

            m_StrokeRenderer.RenderStrokes(m_StrokeLayer, m_Strokes);

            if (m_StrokeImage != null)
            {
                m_StrokeImage.image = m_StrokeLayer;
                m_StrokeImage.MarkDirtyRepaint();
            }
        }

        void RenderStrokesWithCursor()
        {
            if (m_StrokeLayer == null || m_StrokeRenderer == null || m_Strokes == null)
                return;

            if (!m_IsMouseOverCanvas)
                return;

            // Convert screen position to canvas position to get cursor position
            var cursorCanvasPos = ScreenToCanvasPosition(m_CurrentMousePosition);

            // PenWidth is the diameter, so radius is PenWidth / 2
            float cursorRadius = m_PenWidth;

            // Render strokes with cursor circle (no border)
            m_StrokeRenderer.RenderStrokes(m_StrokeLayer, m_Strokes, cursorCanvasPos, true, cursorRadius, null, false);

            if (m_StrokeImage != null)
            {
                m_StrokeImage.image = m_StrokeLayer;
                m_StrokeImage.MarkDirtyRepaint();
            }
        }

        // ====================================================================
        // Screen Capture
        // ====================================================================

        void CaptureScreen()
        {
            try
            {
                byte[] rgba = NativeCapture.CaptureDesktopRGBA(out int width, out int height);
                if (rgba == null || rgba.Length == 0)
                {
                    InternalLog.LogError("[EditScreenCapture] Failed to capture screen");
                    return;
                }

                LoadScreenshotFromRGBA(rgba, width, height);
            }
            catch (Exception e)
            {
                InternalLog.LogError($"[EditScreenCapture] Screen capture failed: {e.Message}");
            }
        }

        void LoadScreenshot(byte[] pngData, bool clearStrokes = true)
        {
            if (pngData == null || pngData.Length == 0) return;

            if (m_BackgroundCapture != null)
            {
                DestroyImmediate(m_BackgroundCapture);
            }

            m_BackgroundCapture = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear: false);
            m_BackgroundCapture.name = "Screenshot";
            m_BackgroundCapture.hideFlags = HideFlags.HideAndDontSave;
            m_BackgroundCapture.LoadImage(pngData, markNonReadable: false);
            m_BackgroundCapture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            m_CaptureWidth = m_BackgroundCapture.width;
            m_CaptureHeight = m_BackgroundCapture.height;

            // Serialize the screenshot data for domain reload persistence
            m_SerializedScreenshotData = pngData;

            SetupLayers(clearStrokes);
        }

        void LoadScreenshotFromRGBA(byte[] rgba, int width, int height)
        {
            if (m_BackgroundCapture != null)
            {
                DestroyImmediate(m_BackgroundCapture);
            }

            m_BackgroundCapture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            m_BackgroundCapture.LoadRawTextureData(rgba);
            m_BackgroundCapture.Apply();

            m_CaptureWidth = width;
            m_CaptureHeight = height;

            SetupLayers();
        }

        void SetupLayers(bool clearStrokes = true)
        {
            // Set background image
            if (m_BackgroundImage != null && m_BackgroundCapture != null)
            {
                m_BackgroundImage.image = m_BackgroundCapture;
                m_BackgroundImage.style.opacity = 1f;
                m_BackgroundImage.MarkDirtyRepaint();
            }

            // Create stroke layer
            if (m_StrokeLayer != null)
            {
                m_StrokeLayer.Release();
                DestroyImmediate(m_StrokeLayer);
            }

            m_StrokeLayer = new RenderTexture(m_CaptureWidth, m_CaptureHeight, 0, RenderTextureFormat.ARGB32)
            {
                name = "StrokeLayer",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            m_StrokeLayer.Create();

            // Clear stroke layer
            RenderTexture.active = m_StrokeLayer;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = null;

            // Set stroke image
            if (m_StrokeImage != null)
            {
                m_StrokeImage.image = m_StrokeLayer;
                m_StrokeImage.style.opacity = 1f;
                m_StrokeImage.MarkDirtyRepaint();
            }

            // Only clear strokes if requested (false during domain reload restoration)
            if (clearStrokes)
            {
                m_Strokes?.Clear();
            }
        }

        /// <summary>
        /// Refreshes the UI image displays without clearing strokes.
        /// Used after domain reload to ensure UI elements are properly bound to their data.
        /// </summary>
        void RefreshUI()
        {
            // Ensure background image is set
            if (m_BackgroundImage != null && m_BackgroundCapture != null)
            {
                m_BackgroundImage.image = m_BackgroundCapture;
                m_BackgroundImage.style.opacity = 1f;
                m_BackgroundImage.MarkDirtyRepaint();
            }

            // Ensure stroke image is set and re-render strokes
            if (m_StrokeImage != null && m_StrokeLayer != null)
            {
                m_StrokeImage.image = m_StrokeLayer;
                m_StrokeImage.style.opacity = 1f;
                m_StrokeImage.MarkDirtyRepaint();
                RenderStrokes();
            }
        }

        // ====================================================================
        // Action Handlers (public for overlay access)
        // ====================================================================

        /// <summary>Clears all strokes from the canvas.</summary>
        public void ClearStrokes()
        {
            m_Strokes?.Clear();

            // Only mark as unsaved if there are still strokes after clearing
            // (clearing an already empty canvas doesn't count as unsaved changes)
            hasUnsavedChanges = m_Strokes != null && m_Strokes.HasStrokes;
            SerializeStrokes();
            RenderStrokes();

            // Update overlay button states after clearing
            EditScreenCaptureToolbarOverlay.Instance?.UpdateUndoRedoButtonStates();
        }

        /// <summary>Undoes the last stroke.</summary>
        public void UndoStroke()
        {
            if (m_Strokes == null) return;
            m_Strokes.UndoLast();

            // Only mark as unsaved if there are strokes remaining
            // If undo brings us back to empty state, it's not a change
            hasUnsavedChanges = m_Strokes.HasStrokes;
            SerializeStrokes();
            RenderStrokes();

            // Update overlay button states after undo
            EditScreenCaptureToolbarOverlay.Instance?.UpdateUndoRedoButtonStates();
        }

        /// <summary>Redoes the last undone stroke.</summary>
        public void RedoStroke()
        {
            if (m_Strokes == null) return;
            m_Strokes.Redo();

            // Mark as unsaved if redo restored strokes
            hasUnsavedChanges = m_Strokes.HasStrokes;
            SerializeStrokes();
            RenderStrokes();

            // Update overlay button states after redo
            EditScreenCaptureToolbarOverlay.Instance?.UpdateUndoRedoButtonStates();
        }

        /// <summary>Exports the current screenshot with annotations to a user-selected location.</summary>
        public void ExportScreenshot()
        {
            try
            {
                // Compose the final image with annotations
                var finalImage = ComposeImage();
                if (finalImage == null)
                {
                    InternalLog.LogWarning("[EditScreenCapture] Could not compose image for export");
                    return;
                }

                // Generate filename with timestamp
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var defaultFileName = $"screenshot_{timestamp}.png";

                // Open save file dialog
                var filePath = EditorUtility.SaveFilePanel(
                    "Export Screenshot",
                    "",
                    defaultFileName,
                    "png");

                // If user cancelled the dialog, filePath will be empty
                if (string.IsNullOrEmpty(filePath))
                {
                    return;
                }

                // Write the PNG file to disk
                System.IO.File.WriteAllBytes(filePath, finalImage);
            }
            catch (Exception e)
            {
                InternalLog.LogError($"[EditScreenCapture] Failed to export screenshot: {e.Message}");
                EditorUtility.DisplayDialog("Export Failed", $"Failed to export screenshot:\n{e.Message}", "OK");
            }
        }

        /// <summary>Serializes the current stroke data for domain reload persistence.</summary>
        void SerializeStrokes()
        {
            if (m_Strokes == null)
                return;

            // Serialize main strokes
            var strokesList = new List<SerializedStrokeData>();
            foreach (var stroke in m_Strokes.Strokes)
            {
                strokesList.Add(new SerializedStrokeData(stroke));
            }

            m_SerializedStrokes = strokesList.ToArray();

            // Serialize zoom and pan
            m_SerializedZoomLevel = m_ZoomLevel;
            m_SerializedPanOffset = m_PanOffset;
        }

        /// <summary>Restores stroke data from serialization after domain reload.</summary>
        void RestoreSerializedStrokes()
        {
            if (m_Strokes == null)
                return;

            // Restore main strokes
            if (m_SerializedStrokes != null && m_SerializedStrokes.Length > 0)
            {
                m_Strokes.Clear();
                foreach (var serialized in m_SerializedStrokes)
                {
                    var stroke = serialized.ToStroke();
                    m_Strokes.AddStroke(stroke);
                }

                // Initialize undo stack after loading strokes so Ctrl+Z works after domain reload
                m_Strokes.FinalizeLoad();
                RenderStrokes();
            }
        }

        /// <summary>Restores zoom and pan state from serialization after domain reload.</summary>
        void RestoreZoomAndPan()
        {
            m_ZoomLevel = m_SerializedZoomLevel;
            m_PanOffset = m_SerializedPanOffset;
            UpdateViewportTransform();
        }

        /// <summary>
        /// Try to highlight the current attachment in the context list.
        /// Called when new ContextElements are created.
        /// </summary>
        public void TryHighlightCurrentAttachment()
        {
            // First ensure we have m_OriginalAttachment initialized from serialized data
            if (m_OriginalAttachment == null && !string.IsNullOrEmpty(m_SerializedAttachmentPayload))
            {
                m_OriginalAttachment = new VirtualAttachment(
                    m_SerializedAttachmentPayload,
                    m_SerializedAttachmentType,
                    m_SerializedAttachmentDisplayName,
                    metadata: null
                );
            }

            // Now try to highlight if we have an attachment
            if (m_OriginalAttachment != null)
            {
                ContextElement.HighlightScreenshotByVirtualAttachment(m_OriginalAttachment);
            }
        }

        void RestoreSerializedAttachment()
        {
            // Restore the original attachment from serialized data after domain reload
            if (!string.IsNullOrEmpty(m_SerializedAttachmentPayload))
            {
                m_OriginalAttachment = new VirtualAttachment(
                    m_SerializedAttachmentPayload,
                    m_SerializedAttachmentType,
                    m_SerializedAttachmentDisplayName,
                    metadata: null
                );
                m_HighlightAttempts = 0; // Reset highlight attempts when restoring attachment

                // Highlight the context row after domain reload with additional delay
                // to ensure ContextElements are fully created in the AssistantWindow
                EditorTask.delayCall += () =>
                {
                    EditorTask.delayCall += () =>
                    {
                        if (m_OriginalAttachment != null)
                        {
                            ContextElement.HighlightScreenshotByVirtualAttachment(m_OriginalAttachment);
                        }
                    };
                };
            }
        }

        /// <summary>Completes annotation, sends to assistant, and closes the window.</summary>
        public void Done()
        {
            // Compose final image and send to assistant
            var finalImage = ComposeImage();
            var annotationsImage = GetStrokeLayerBytes();

            if (finalImage != null)
            {
                // Send to assistant
                var assistantWindow = GetWindow<AssistantWindow>(false);
                if (assistantWindow != null)
                {
                    // If we have an original attachment, update it instead of creating a new one
                    if (m_OriginalAttachment != null)
                    {
                        assistantWindow.ReplaceScreenshot(m_OriginalAttachment, finalImage, annotationsImage);

                        // Remove highlight from the context row
                        ContextElement.RemoveHighlightByVirtualAttachment(m_OriginalAttachment);
                    }
                    else
                    {
                        assistantWindow.AttachAnnotatedScreenshot(finalImage, annotationsImage);
                    }
                }
            }

            // Mark changes as saved
            hasUnsavedChanges = false;
            Close();
        }

        byte[] GetStrokeLayerBytes()
        {
            if (m_StrokeLayer == null)
                return null;

            var previousRT = RenderTexture.active;
            var result = new Texture2D(m_CaptureWidth, m_CaptureHeight, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = m_StrokeLayer;
                result.ReadPixels(new Rect(0, 0, m_CaptureWidth, m_CaptureHeight), 0, 0);

                // Convert to a strict binary mask: Black background, White strokes at 100% opacity
                var pixels = result.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    if (pixels[i].a > 0)
                    {
                        pixels[i] = new Color32(255, 255, 255, 255);
                    }
                    else
                    {
                        pixels[i] = new Color32(0, 0, 0, 255);
                    }
                }
                result.SetPixels32(pixels);
                result.Apply();

                var pngData = result.EncodeToPNG();

                return pngData;
            }
            finally
            {
                RenderTexture.active = previousRT;
                DestroyImmediate(result);
            }
        }

        /// <summary>
        /// Focuses the main canvas element to ensure keyboard events are received by the window.
        /// Call this after overlay button clicks to fix keyboard shortcut handling.
        /// </summary>
        public void FocusCanvas()
        {
            if (m_CanvasContainer != null)
            {
                m_CanvasContainer.Focus();
            }
        }

        byte[] ComposeImage()
        {
            if (m_BackgroundCapture == null)
                return null;

            // Create a temporary RenderTexture for GPU-based blending
            var tempRT = RenderTexture.GetTemporary(m_CaptureWidth, m_CaptureHeight, 0, RenderTextureFormat.ARGB32);

            // Load the compositing shader
            var compositeShader = Shader.Find("Hidden/AI.Assistant/ImageCompositing");
            if (compositeShader == null)
            {
                InternalLog.LogError("[EditScreenCapture] Could not find ImageCompositing shader. Falling back to CPU blending.");
                RenderTexture.ReleaseTemporary(tempRT);
                return ComposeImageCPU();
            }

            var compositeMaterial = new Material(compositeShader);
            var result = new Texture2D(m_CaptureWidth, m_CaptureHeight, TextureFormat.RGBA32, false);
            var previousRT = RenderTexture.active;
            try
            {
                // Set up shader properties
                compositeMaterial.SetTexture("_MainTex", m_BackgroundCapture);
                compositeMaterial.SetTexture("_StrokeTex", m_StrokeLayer);

                // Blit the textures using the shader
                Graphics.Blit(m_BackgroundCapture, tempRT, compositeMaterial);

                // Read the result from RenderTexture
                RenderTexture.active = tempRT;
                result.ReadPixels(new Rect(0, 0, m_CaptureWidth, m_CaptureHeight), 0, 0);
                result.Apply();

                // Encode to PNG
                var pngData = result.EncodeToPNG();

                return pngData;
            }
            finally
            {
                RenderTexture.active = previousRT;
                RenderTexture.ReleaseTemporary(tempRT);
                DestroyImmediate(compositeMaterial);
                DestroyImmediate(result);
            }
        }

        /// <summary>
        /// Fallback CPU-based image compositing for when GPU blending is unavailable.
        /// This is much slower on large images but provides a working alternative.
        /// </summary>
        byte[] ComposeImageCPU()
        {
            if (m_BackgroundCapture == null)
                return null;

            // Create a temporary texture to compose the final image
            var result = new Texture2D(m_CaptureWidth, m_CaptureHeight, TextureFormat.RGBA32, false);

            // Copy background
            var bgPixels = m_BackgroundCapture.GetPixels32();

            // Read stroke layer
            var strokeTex = new Texture2D(m_CaptureWidth, m_CaptureHeight, TextureFormat.RGBA32, false);
            var previousRT = RenderTexture.active;
            try
            {
                RenderTexture.active = m_StrokeLayer;
                strokeTex.ReadPixels(new Rect(0, 0, m_CaptureWidth, m_CaptureHeight), 0, 0);
                strokeTex.Apply();

                var strokePixels = strokeTex.GetPixels32();

                // Blend strokes onto background
                var resultPixels = new Color32[bgPixels.Length];
                for (int i = 0; i < bgPixels.Length; i++)
                {
                    var bg = bgPixels[i];
                    var stroke = strokePixels[i];

                    // Alpha blend
                    float alpha = stroke.a / 255f;
                    resultPixels[i] = new Color32(
                        (byte)(stroke.r * alpha + bg.r * (1 - alpha)),
                        (byte)(stroke.g * alpha + bg.g * (1 - alpha)),
                        (byte)(stroke.b * alpha + bg.b * (1 - alpha)),
                        255
                    );
                }

                result.SetPixels32(resultPixels);
                result.Apply();

                var pngData = result.EncodeToPNG();

                return pngData;
            }
            finally
            {
                RenderTexture.active = previousRT;
                DestroyImmediate(strokeTex);
                DestroyImmediate(result);
            }
        }
    }

    /// <summary>
    /// Serializable wrapper for stroke data to persist across domain reloads.
    /// </summary>
    [Serializable]
    class SerializedStrokeData
    {
        public Vector2[] points = Array.Empty<Vector2>();
        public Color color = Color.red;
        public float width = 3f;
        public bool isComplete;

        public SerializedStrokeData() { }

        public SerializedStrokeData(AnnotationStroke annotationStroke)
        {
            points = annotationStroke.Points.ToArray();
            color = annotationStroke.Color;
            width = annotationStroke.Width;
            isComplete = annotationStroke.IsComplete;
        }

        public AnnotationStroke ToStroke()
        {
            var stroke = new AnnotationStroke(color, width);
            stroke.Points.AddRange(points);
            stroke.IsComplete = isComplete;
            return stroke;
        }
    }
}
