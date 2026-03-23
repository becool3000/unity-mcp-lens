using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    /// <summary>
    /// Floating toolbar overlay for annotation tools displayed in the EditScreenCaptureWindow.
    /// Provides buttons for colors, line thickness, and actions (clear, undo, redo, cancel, done).
    /// </summary>
    [Overlay(typeof(EditScreenCaptureWindow), k_OverlayId, "Annotate", defaultDisplay = true, defaultDockZone = DockZone.Floating)]
    class EditScreenCaptureToolbarOverlay : Overlay
    {
        const string k_OverlayId = "edit-screen-capture-annotation-toolbar";
        const string k_StylesPath = "Packages/com.unity.ai.assistant/Editor/UI/Styles";
        const string k_IconsPath = "Packages/com.unity.ai.assistant/Editor/UI/Assets/icons";

        // Static instance for window to access overlay
        internal static EditScreenCaptureToolbarOverlay Instance { get; private set; }

        // Default pen color (red-orange)
        static readonly Color k_DefaultPenColor = new (1f, 48f / 255f, 0f);
        const float k_DefaultPenWidth = 8f;

        // Thickness values
        const float k_SmallSize = 3f;
        const float k_MediumSize = 8f;
        const float k_HeavySize = 20f;

        // Pending pen settings that window can read before overlay fully initializes
        // This handles the case where overlay CreatePanelContent runs before window OnEnable
        internal static Color pendingPenColor = k_DefaultPenColor;
        internal static float pendingPenWidth = k_DefaultPenWidth;

        // Selected settings (instance fields)
        static Button s_SelectedColorButton;
        Color m_SelectedColor = k_DefaultPenColor;
        float m_SelectedThickness = k_DefaultPenWidth;

        // Undo/Redo/Export button references
        Button m_UndoButton;
        Button m_RedoButton;
        Button m_ExportButton;

        // Thickness button references
        Button m_ThicknessSmallBtn;
        Button m_ThicknessMediumBtn;
        Button m_ThicknessHeavyBtn;

        // Opacity slider and input references
        SliderInt m_OpacitySlider;
        TextField m_OpacityInput;

        // Main panel container reference for refreshing after play mode
        VisualElement m_PanelContent;

        // Serialized settings for domain reload persistence
        [SerializeField]
        Color m_SerializedSelectedColor = k_DefaultPenColor;

        [SerializeField]
        float m_SerializedSelectedThickness = k_DefaultPenWidth;

        [SerializeField]
        int m_SerializedOpacityValue = 100;

        public EditScreenCaptureToolbarOverlay()
        {
            displayName = "Annotate";
            // Subscribe to play mode state changes to recreate overlay when exiting play mode
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public override VisualElement CreatePanelContent()
        {
            // Set static instance for window to access
            Instance = this;

            // Always read from window's serialized fields as the source of truth
            // This ensures overlay and window stay in sync after close/reopen
            if (EditScreenCaptureWindow.Instance != null)
            {
                m_SelectedColor = EditScreenCaptureWindow.Instance.GetSerializedPenColor();
                m_SelectedThickness = EditScreenCaptureWindow.Instance.GetSerializedPenWidth();
                m_SerializedSelectedColor = m_SelectedColor;
                m_SerializedSelectedThickness = m_SelectedThickness;
            }
            else
            {
                // Fallback if window not available - use overlay's serialized values
                m_SelectedColor = m_SerializedSelectedColor;
                m_SelectedThickness = m_SerializedSelectedThickness;
            }

            // Restore opacity from the serialized color's alpha channel
            // If the serialized color has an alpha value, use it; otherwise default to 100
            m_SerializedOpacityValue = m_SelectedColor.a > 0f ? Mathf.RoundToInt(m_SelectedColor.a * 100f) : 100;

            // Save to pending static fields so window can read them in OnEnable
            // This handles the case where CreatePanelContent runs before window OnEnable
            pendingPenColor = m_SelectedColor;
            pendingPenWidth = m_SelectedThickness;

            // Sync with window if available
            if (EditScreenCaptureWindow.Instance != null)
            {
                EditScreenCaptureWindow.Instance.SyncPenSettingsFromOverlay(m_SelectedColor, m_SelectedThickness);
            }

            // Load UXML from file
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{k_StylesPath}/AnnotationToolbarOverlay.uxml");
            if (uxml == null)
            {
                InternalLog.LogError("[Annotation] Failed to load UXML");
                return CreateFallbackContent();
            }

            // Load USS stylesheet
            var stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                $"{k_StylesPath}/AnnotationToolbarOverlay.uss");

            // Create main container from UXML
            var mainContainer = uxml.Instantiate();
            if (stylesheet != null)
            {
                mainContainer.styleSheets.Add(stylesheet);
            }

            // Configure Row 1: Hide tool buttons (only draw tool in capture window)
            var toggleGroup = mainContainer.Q<ToggleButtonGroup>("row-1-draw");
            if (toggleGroup != null)
            {
                toggleGroup.AddToClassList("hidden");
            }

            // Populate Row 2: Color buttons (primary colors)
            var row2 = mainContainer.Q("row-2-colors");
            if (row2 != null)
            {
                var row2Colors = new Color[]
                {
                    new (1f, 48f / 255f, 0f),         // Red (default)
                    new (1f, 153f / 255f, 27f / 255f), // Orange
                    new (1f, 198f / 255f, 0f),         // Yellow
                    new (22f / 255f, 217f / 255f, 105f / 255f), // Green
                    new (0f, 176f / 255f, 1f),         // Cyan
                    new (144f / 255f, 75f / 255f, 1f)  // Purple
                };

                foreach (var color in row2Colors)
                {
                    // Compare RGB values only (ignore alpha) to determine if color is selected
                    var isSelected = ColorEqualsRGB(color, m_SelectedColor);
                    row2.Add(CreateColorButton(color, isSelected));
                }
            }

            // Populate Row 3: More color buttons (grays)
            var row3 = mainContainer.Q("row-3-colors");
            if (row3 != null)
            {
                var row3Colors = new Color[]
                {
                    new (1f, 1f, 1f),                           // White
                    new (30f / 255f, 30f / 255f, 30f / 255f),   // Dark gray
                    new (77f / 255f, 77f / 255f, 77f / 255f),   // Gray
                    new (128f / 255f, 128f / 255f, 128f / 255f), // Medium gray
                    new (180f / 255f, 180f / 255f, 180f / 255f), // Light gray
                    new (245f / 255f, 245f / 255f, 245f / 255f)  // Very light
                };

                foreach (var color in row3Colors)
                {
                    // Compare RGB values only (ignore alpha) to determine if color is selected
                    var isSelected = ColorEqualsRGB(color, m_SelectedColor);
                    row3.Add(CreateColorButton(color, isSelected));
                }
            }

            // Populate Row 0: Undo, Redo, Export (left), Clear (right)
            var row0 = mainContainer.Q("row-0-undo-redo");
            if (row0 != null)
            {
                // Left side: Undo, Redo, and Export
                var leftButtons = new VisualElement();
                leftButtons.AddToClassList("annotation-buttons-row");

                // Platform-specific shortcut text
                var ctrlKey = RuntimePlatform.OSXEditor == Application.platform ? "Cmd" : "Ctrl";
                var undoTooltip = $"Undo last stroke ({ctrlKey}+Z)";
                var redoTooltip = $"Redo last stroke ({ctrlKey}+Y)";

                m_UndoButton = CreateIconButton($"{k_IconsPath}/Undo@4x.png", () => OnUndoClicked(this), undoTooltip);
                m_RedoButton = CreateIconButton($"{k_IconsPath}/Redo@4x.png", () => OnRedoClicked(this), redoTooltip);
                m_ExportButton = CreateIconButton($"{k_IconsPath}/Export.png", () => OnExportClicked(this), "Export screenshot to file");
                leftButtons.Add(m_UndoButton);
                leftButtons.Add(m_RedoButton);
                leftButtons.Add(m_ExportButton);
                row0.Add(leftButtons);

                // Right side: Clear button
                var clearBtn = new Button();
                clearBtn.text = "Clear";
                clearBtn.AddToClassList("annotation-clear-button");
                ApplyThemeColors(clearBtn);
                clearBtn.clicked += () => OnClearClicked(this);
                clearBtn.tooltip = "Clear all annotations";
                row0.Add(clearBtn);
            }
            else
            {
                InternalLog.LogWarning("[Annotation] row-0-undo-redo not found in mainContainer");
            }

            // Initialize Row 4: Opacity slider
            ConfigureOpacitySlider(mainContainer);

            // Initialize Row 5: Line thickness buttons
            ConfigureThicknessButtons(mainContainer);

            // Populate Row 6: Action buttons (Cancel, Done)
            var row6 = mainContainer.Q("row-6-actions");
            if (row6 != null)
            {
                row6.Add(CreateActionButton("Cancel", () => OnCancelClicked(), "Cancel and close without saving"));
                row6.Add(CreateActionButton("Save & Close", () => OnDoneClicked(), "Save annotations and attach to assistant"));
            }

            // Initialize undo/redo button states
            UpdateUndoRedoButtonStates();

            // Store reference to panel content for refreshing after play mode
            m_PanelContent = mainContainer;

            return mainContainer;
        }

        VisualElement CreateFallbackContent()
        {
            var container = new VisualElement();
            container.AddToClassList("annotation-fallback-container");

            container.Add(CreateActionButton("Clear", () => OnClearClicked(this)));
            container.Add(CreateActionButton("Undo", () => OnUndoClicked(this)));
            container.Add(CreateActionButton("Cancel", () => OnCancelClicked()));
            container.Add(CreateActionButton("Done", () => OnDoneClicked()));

            return container;
        }

        void ConfigureOpacitySlider(VisualElement root)
        {
            m_OpacitySlider = root.Q<SliderInt>("opacity-slider");
            m_OpacityInput = root.Q<TextField>("opacity-input");

            if (m_OpacitySlider == null)
                return;

            // Restore opacity value from serialized data (defaults to 100 = 100%)
            m_OpacitySlider.value = m_SerializedOpacityValue;

            // Update input field and tooltip
            UpdateOpacityDisplay();

            // Register slider change handler
            m_OpacitySlider.RegisterValueChangedCallback(evt =>
            {
                OnOpacityChanged(evt.newValue);
            });

            // Register input field handler - use isDelayed to wait for Enter/focus loss
            if (m_OpacityInput != null)
            {
                m_OpacityInput.isDelayed = true;
                m_OpacityInput.RegisterValueChangedCallback(evt =>
                {
                    OnOpacityInputChanged(evt.newValue);
                });
            }
        }

        void OnOpacityChanged(int opacityPercent)
        {
            // Use SetValueWithoutNotify to prevent duplicate event triggers when called from OnOpacityInputChanged
            // The slider's built-in clamping still applies
            m_OpacitySlider.SetValueWithoutNotify(opacityPercent);
            ApplyOpacity(m_OpacitySlider.value);
        }

        void OnOpacityInputChanged(string newValueString)
        {
            // Remove optional '%' sign to improve UX
            string cleanValue = newValueString.Replace("%", "").Trim();

            // Parse percentage input
            if (int.TryParse(cleanValue, out int percentValue))
            {
                // Explicitly clamp to ensure the value stays within the valid range
                if (m_OpacitySlider != null)
                {
                    percentValue = Mathf.Clamp(percentValue, m_OpacitySlider.lowValue, m_OpacitySlider.highValue);
                }
                OnOpacityChanged(percentValue);
            }
            else
            {
                // Invalid input - revert to current valid slider value
                UpdateOpacityDisplay();
            }
        }

        void ApplyOpacity(int opacityPercent)
        {
            m_SerializedOpacityValue = opacityPercent;
            UpdateOpacityDisplay();

            // Convert percentage to alpha (20-100 -> 0.2-1.0)
            float alpha = opacityPercent / 100f;

            // Update pen color with new opacity (preserve RGB)
            m_SelectedColor.a = alpha;
            m_SerializedSelectedColor = m_SelectedColor;
            pendingPenColor = m_SelectedColor;

            // Sync with window
            if (EditScreenCaptureWindow.Instance != null)
            {
                EditScreenCaptureWindow.Instance.PenColor = m_SelectedColor;
            }
        }

        void UpdateOpacityDisplay()
        {
            if (m_OpacitySlider == null)
                return;

            m_OpacitySlider.tooltip = $"Opacity: {m_OpacitySlider.value}%";

            if (m_OpacityInput != null)
            {
                m_OpacityInput.SetValueWithoutNotify(m_OpacitySlider.value.ToString());
            }
        }

        void ConfigureThicknessButtons(VisualElement root)
        {
            var toggleGroup = root.Q<ToggleButtonGroup>("thickness-toggle-group");
            var smallBtn = root.Q<Button>("thickness-small");
            var mediumBtn = root.Q<Button>("thickness-medium");
            var heavyBtn = root.Q<Button>("thickness-heavy");

            if (toggleGroup == null || smallBtn == null || mediumBtn == null || heavyBtn == null)
                return;

            // Store button references for refreshing after play mode
            m_ThicknessSmallBtn = smallBtn;
            m_ThicknessMediumBtn = mediumBtn;
            m_ThicknessHeavyBtn = heavyBtn;

            // Set icons
            SetButtonIcon(smallBtn, $"{k_IconsPath}/LineThickness-Small.png");
            SetButtonIcon(mediumBtn, $"{k_IconsPath}/LineThickness-Medium.png");
            SetButtonIcon(heavyBtn, $"{k_IconsPath}/LineThickness-Heavy.png");

            // Register click handlers
            smallBtn.clicked += () => SelectThickness(k_SmallSize);
            mediumBtn.clicked += () => SelectThickness(k_MediumSize);
            heavyBtn.clicked += () => SelectThickness(k_HeavySize);

            // Select the cached thickness or default to medium
            if (Mathf.Approximately(m_SelectedThickness, k_SmallSize))
            {
                toggleGroup.SetValueWithoutNotify(new ToggleButtonGroupState(0b001UL, 3));
            }
            else if (Mathf.Approximately(m_SelectedThickness, k_HeavySize))
            {
                toggleGroup.SetValueWithoutNotify(new ToggleButtonGroupState(0b100UL, 3));
            }
            else
            {
                // Default to medium
                toggleGroup.SetValueWithoutNotify(new ToggleButtonGroupState(0b010UL, 3));
                m_SelectedThickness = k_MediumSize;
                m_SerializedSelectedThickness = k_MediumSize; // Save for domain reload persistence
            }

            // Update undo/redo button states based on current stroke collection state
            UpdateUndoRedoButtonStates();
        }

        internal void UpdateUndoRedoButtonStates()
        {
            if (EditScreenCaptureWindow.Instance == null || EditScreenCaptureWindow.Instance.GetStrokeCollection() == null)
                return;

            var strokes = EditScreenCaptureWindow.Instance.GetStrokeCollection();

            if (m_UndoButton != null)
            {
                m_UndoButton.SetEnabled(strokes.CanUndo);
            }

            if (m_RedoButton != null)
            {
                m_RedoButton.SetEnabled(strokes.CanRedo);
            }
        }

        static void SetButtonIcon(Button button, string iconPath)
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
            if (icon != null)
            {
                // Tint the icon based on theme using Unity standard colors
                // Dark theme: #EEEEEE (rgb(238, 238, 238)), Light theme: #090909 (rgb(9, 9, 9))
                Color iconTintColor = EditorGUIUtility.isProSkin ? new Color(238f / 255f, 238f / 255f, 238f / 255f) : new Color(9f / 255f, 9f / 255f, 9f / 255f);
                icon = TintIcon(icon, iconTintColor);
                button.style.backgroundImage = new StyleBackground(icon);
            }
        }

        /// <summary>
        /// Compares two colors with tolerance for floating point precision.
        /// </summary>
        static bool ColorEquals(Color a, Color b, float tolerance = 0.01f)
        {
            return Mathf.Abs(a.r - b.r) < tolerance &&
                   Mathf.Abs(a.g - b.g) < tolerance &&
                   Mathf.Abs(a.b - b.b) < tolerance &&
                   Mathf.Abs(a.a - b.a) < tolerance;
        }

        /// <summary>
        /// Compares only RGB values of two colors, ignoring alpha.
        /// </summary>
        static bool ColorEqualsRGB(Color a, Color b, float tolerance = 0.01f)
        {
            return Mathf.Abs(a.r - b.r) < tolerance &&
                   Mathf.Abs(a.g - b.g) < tolerance &&
                   Mathf.Abs(a.b - b.b) < tolerance;
        }

        void SelectThickness(float thickness)
        {
            m_SelectedThickness = thickness;
            m_SerializedSelectedThickness = thickness; // Save for domain reload persistence
            pendingPenWidth = thickness; // Update pending for window to read

            // Update the window (which will serialize it)
            if (EditScreenCaptureWindow.Instance != null)
            {
                EditScreenCaptureWindow.Instance.PenWidth = thickness;
            }
        }

        Button CreateColorButton(Color color, bool selected = false)
        {
            var button = new Button();
            button.AddToClassList("annotation-color-button");

            // Display color with full opacity in UI
            var displayColor = color;
            displayColor.a = 1f;
            button.style.backgroundColor = displayColor;

            if (selected)
            {
                SelectColorButton(button, color);
            }

            button.clicked += () => SelectColorButton(button, color);
            return button;
        }

        void SelectColorButton(Button button, Color color)
        {
            // Deselect previous
            if (s_SelectedColorButton != null)
            {
                s_SelectedColorButton.RemoveFromClassList("selected");
                s_SelectedColorButton.AddToClassList("deselected");
            }

            // Select new
            s_SelectedColorButton = button;
            button.RemoveFromClassList("deselected");
            button.AddToClassList("selected");

            // Apply current opacity to the selected color
            color.a = m_SerializedOpacityValue / 100f;
            m_SelectedColor = color;
            m_SerializedSelectedColor = color;
            pendingPenColor = color;

            // Sync with window
            if (EditScreenCaptureWindow.Instance != null)
            {
                EditScreenCaptureWindow.Instance.PenColor = color;
            }
        }

        static Button CreateActionButton(string text, System.Action onClick, string tooltip = "")
        {
            var button = new Button();
            button.text = text;
            button.AddToClassList("annotation-action-button");

            // Apply theme-appropriate colors
            ApplyThemeColors(button);

            button.clicked += onClick;
            if (!string.IsNullOrEmpty(tooltip))
            {
                button.tooltip = tooltip;
            }
            return button;
        }

        static Button CreateIconButton(string iconPath, System.Action onClick, string tooltip = "")
        {
            var button = new Button();
            button.AddToClassList("annotation-icon-button");

            // Load and set the icon
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
            if (icon != null)
            {
                // Tint the icon based on theme using Unity standard colors
                // Dark theme: #EEEEEE (rgb(238, 238, 238)), Light theme: #090909 (rgb(9, 9, 9))
                Color iconTintColor = EditorGUIUtility.isProSkin ? new Color(238f / 255f, 238f / 255f, 238f / 255f) : new Color(9f / 255f, 9f / 255f, 9f / 255f);
                icon = TintIcon(icon, iconTintColor);
                button.style.backgroundImage = new StyleBackground(icon);
            }
            else
            {
                InternalLog.LogWarning($"[Annotation] Failed to load icon: {iconPath}");
                button.text = "?";
            }

            // Apply theme-appropriate background colors
            ApplyThemeColors(button);

            button.clicked += onClick;
            if (!string.IsNullOrEmpty(tooltip))
            {
                button.tooltip = tooltip;
            }
            return button;
        }

        /// <summary>
        /// Applies theme-appropriate CSS class to buttons (Undo, Redo, Export, Clear, Cancel, Done).
        /// Uses light theme class in light theme, dark theme class in dark theme.
        /// </summary>
        static void ApplyThemeColors(Button button)
        {
            if (EditorGUIUtility.isProSkin)
            {
                button.AddToClassList("annotation-theme-dark");
            }
            else
            {
                button.AddToClassList("annotation-theme-light");
            }
        }

        /// <summary>
        /// Tints an icon texture to a target color while preserving alpha.
        /// </summary>
        static Texture2D TintIcon(Texture2D source, Color tintColor)
        {
            // Create a temporary RenderTexture to make the source texture readable
            RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);

            // Read pixels from the RenderTexture
            RenderTexture.active = rt;
            Texture2D readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            readable.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);

            // Tint the readable texture
            var pixels = readable.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                // Multiply the pixel color by the tint color, preserving alpha
                pixels[i] = new Color(
                    pixels[i].r * tintColor.r,
                    pixels[i].g * tintColor.g,
                    pixels[i].b * tintColor.b,
                    pixels[i].a
                );
            }

            readable.SetPixels(pixels);
            readable.Apply();
            return readable;
        }

        static void OnClearClicked(EditScreenCaptureToolbarOverlay overlay)
        {
            EditScreenCaptureWindow.Instance?.ClearStrokes();
            EditScreenCaptureWindow.Instance?.FocusCanvas();
            overlay?.UpdateUndoRedoButtonStates();
        }

        static void OnUndoClicked(EditScreenCaptureToolbarOverlay overlay)
        {
            EditScreenCaptureWindow.Instance?.UndoStroke();
            EditScreenCaptureWindow.Instance?.FocusCanvas();
            overlay?.UpdateUndoRedoButtonStates();
        }

        static void OnRedoClicked(EditScreenCaptureToolbarOverlay overlay)
        {
            EditScreenCaptureWindow.Instance?.RedoStroke();
            EditScreenCaptureWindow.Instance?.FocusCanvas();
            overlay?.UpdateUndoRedoButtonStates();
        }

        static void OnExportClicked(EditScreenCaptureToolbarOverlay overlay)
        {
            EditScreenCaptureWindow.Instance?.ExportScreenshot();
            EditScreenCaptureWindow.Instance?.FocusCanvas();
        }

        static void OnCancelClicked()
        {
            EditScreenCaptureWindow.Instance?.Close();
        }

        static void OnDoneClicked()
        {
            EditScreenCaptureWindow.Instance?.Done();
        }

        void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            // When exiting play mode, refresh the button icons and textures
            if (change == PlayModeStateChange.ExitingPlayMode)
            {
                // Delay the refresh to ensure domain reload is complete
                // Use multiple delays to ensure all cleanup is done
                EditorApplication.delayCall += () =>
                {
                    EditorApplication.delayCall += RefreshButtonTextures;
                };
            }
        }

        void RefreshButtonTextures()
        {
            // Refresh icon textures on undo/redo/export buttons after domain reload
            if (m_UndoButton != null)
                SetButtonIcon(m_UndoButton, $"{k_IconsPath}/Undo@4x.png");

            if (m_RedoButton != null)
                SetButtonIcon(m_RedoButton, $"{k_IconsPath}/Redo@4x.png");

            if (m_ExportButton != null)
                SetButtonIcon(m_ExportButton, $"{k_IconsPath}/Export.png");

            // Refresh thickness button icons
            if (m_ThicknessSmallBtn != null)
                SetButtonIcon(m_ThicknessSmallBtn, $"{k_IconsPath}/LineThickness-Small.png");

            if (m_ThicknessMediumBtn != null)
                SetButtonIcon(m_ThicknessMediumBtn, $"{k_IconsPath}/LineThickness-Medium.png");

            if (m_ThicknessHeavyBtn != null)
                SetButtonIcon(m_ThicknessHeavyBtn, $"{k_IconsPath}/LineThickness-Heavy.png");

            // Mark the panel for repaint to ensure all buttons are redrawn
            if (m_PanelContent != null)
            {
                m_PanelContent.MarkDirtyRepaint();
            }
        }

        ~EditScreenCaptureToolbarOverlay()
        {
            // Unsubscribe from play mode state changes to prevent memory leaks
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }
    }
}
