#if !UNITY_6000_3_OR_NEWER
using System;
using System.Reflection;
using Unity.AI.Toolkit.Accounts.Components;
using Unity.AI.Toolkit.Accounts.Services;
using Unity.AI.Toolkit.Connect;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts
{
    /// <summary>
    /// Legacy AI Toolbar button implementation for Unity versions prior to 6000.3.
    /// Uses UIElements injection to add a button to the main toolbar's visual tree.
    /// </summary>
    static class AIToolbarButtonLegacy
    {
        const string k_ButtonText = "AI";
        const string k_ButtonTooltip = "Open AI dropdown";
        const string k_ButtonClassName = "ai-toolbar-button-legacy";
        const string k_ToolbarZoneLeftAlign = "ToolbarZoneLeftAlign";

        static Button s_Button;
        static Label s_Label;
        static VisualElement s_Overlay;
        static VisualElement s_IconElement;
        static VisualElement s_Arrow;
        static bool s_IsInitialized;
        static bool s_IsVisible = true;
        static string s_OriginalText;
        static IVisualElementScheduledItem s_NotificationSchedule;
        static IVisualElementScheduledItem s_RefreshSchedule;

        const int k_NotificationDurationMs = 2000;
        const int k_RefreshDelayMs = 5000;

        /// <summary>
        /// Returns true if the legacy toolbar button system is available.
        /// </summary>
        public static bool IsAvailable => GetToolbarRoot() != null;

        /// <summary>
        /// Shows the points cost notification flash on the button.
        /// </summary>
        public static void ShowPointsCostNotification(int amount)
        {
            if (s_Button == null || s_Label == null || s_Overlay == null)
                return;

            s_NotificationSchedule?.Pause();
            s_RefreshSchedule?.Pause();

            // Store original text if not already stored
            if (string.IsNullOrEmpty(s_OriginalText))
                s_OriginalText = s_Label.text;

            // Tint icon and arrow black during notification
            if (s_IconElement != null)
                s_IconElement.style.unityBackgroundImageTintColor = Color.black;
            if (s_Arrow != null)
                s_Arrow.style.unityBackgroundImageTintColor = Color.black;

            // Show the cost with flash
            s_Label.text = $"-{amount}";
            s_Label.style.color = Color.black;
            s_Overlay.style.opacity = 1;

            // Schedule reset
            s_NotificationSchedule = s_Button.schedule.Execute(() =>
            {
                s_Label.text = s_OriginalText;
                s_Label.style.color = StyleKeyword.Null; // Reset to inherit
                s_Overlay.style.opacity = 0;

                // Reset icon and arrow tint
                if (s_IconElement != null)
                    s_IconElement.style.unityBackgroundImageTintColor = StyleKeyword.Null;
                if (s_Arrow != null)
                    s_Arrow.style.unityBackgroundImageTintColor = StyleKeyword.Null;
            }).StartingIn(k_NotificationDurationMs);

            // Schedule points refresh
            s_RefreshSchedule = s_Button.schedule.Execute(Account.pointsBalance.Refresh)
                .StartingIn(k_RefreshDelayMs);
        }

        /// <summary>
        /// Initializes the legacy AI toolbar button.
        /// </summary>
        [InitializeOnLoadMethod]
        internal static void Init()
        {
            // Defer initialization to ensure toolbar is created
            EditorApplication.delayCall += TryInitialize;
        }

        static void TryInitialize()
        {
            if (s_IsInitialized)
                return;

            // The toolbar might not be ready yet, retry on next update
            if (!TryInjectButton())
            {
                EditorApplication.update += RetryInitialize;
            }
        }

        static void RetryInitialize()
        {
            if (TryInjectButton())
            {
                EditorApplication.update -= RetryInitialize;
            }
        }

        static bool TryInjectButton()
        {
            var toolbarRoot = GetToolbarRoot();
            if (toolbarRoot == null)
                return false;

            // Find the left-aligned toolbar zone
            var leftZone = toolbarRoot.Q(k_ToolbarZoneLeftAlign);
            if (leftZone == null)
            {
                // Try to find any container we can add to
                leftZone = toolbarRoot.Q(className: "unity-toolbar-zone-left");
                if (leftZone == null)
                    return false;
            }

            // Check if we already added our button
            if (leftZone.Q(className: k_ButtonClassName) != null)
            {
                s_IsInitialized = true;
                return true;
            }

            // Create the button with toolbar button styling (no text property - we'll add elements manually)
            s_Button = new Button(OnButtonClicked);
            s_Button.tooltip = k_ButtonTooltip;
            s_Button.AddToClassList(k_ButtonClassName);
            s_Button.AddToClassList("unity-toolbar-button");

            // Apply basic toolbar button styling
            s_Button.style.flexDirection = FlexDirection.Row;
            s_Button.style.alignItems = Align.Center;
            s_Button.style.height = 20;
            s_Button.style.marginLeft = 2;
            s_Button.style.marginRight = 2;
            s_Button.style.paddingLeft = 2;
            s_Button.style.paddingRight = 0;

            // Add overlay for notification flash (must be first, positioned absolute)
            s_Overlay = new VisualElement();
            s_Overlay.style.position = Position.Absolute;
            s_Overlay.style.top = 0;
            s_Overlay.style.bottom = 0;
            s_Overlay.style.left = 0;
            s_Overlay.style.right = 0;
            s_Overlay.style.backgroundColor = Color.white;
            s_Overlay.style.borderTopLeftRadius = 3;
            s_Overlay.style.borderTopRightRadius = 3;
            s_Overlay.style.borderBottomLeftRadius = 3;
            s_Overlay.style.borderBottomRightRadius = 3;
            s_Overlay.style.opacity = 0;
            s_Overlay.pickingMode = PickingMode.Ignore;
            s_Button.Add(s_Overlay);

            // Add sparkles/AI icon
            var sparklesIconPath = EditorGUIUtility.isProSkin
                ? "Packages/com.unity.ai.assistant/Editor/UI/Assets/icons/Sparkle.png"
                : "Packages/com.unity.ai.assistant/Editor/UI/Assets/icons/Sparkle_dark.png";
            var sparklesIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(sparklesIconPath);

            if (sparklesIcon != null)
            {
                s_IconElement = new VisualElement();
                s_IconElement.style.width = 16;
                s_IconElement.style.height = 16;
                s_IconElement.style.marginRight = 4;
                s_IconElement.style.backgroundImage = new StyleBackground(sparklesIcon);
                s_IconElement.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                s_Button.Add(s_IconElement);
            }

            // Add text label
            s_Label = new Label(k_ButtonText);
            s_Label.style.marginRight = 2;
            s_Label.style.unityTextAlign = TextAnchor.MiddleCenter;
            s_Button.Add(s_Label);

            // Store original text
            s_OriginalText = k_ButtonText;

            // Add dropdown arrow
            s_Arrow = new VisualElement();
            s_Arrow.style.width = 12;
            s_Arrow.style.height = 12;
            s_Arrow.style.marginLeft = 2;

            // Try to load Unity's dropdown icon
            var arrowIcon = EditorGUIUtility.IconContent("d_icon dropdown")?.image as Texture2D;
            if (arrowIcon == null)
                arrowIcon = EditorGUIUtility.IconContent("icon dropdown")?.image as Texture2D;
            if (arrowIcon == null)
                arrowIcon = EditorGUIUtility.IconContent("dropdown")?.image as Texture2D;

            if (arrowIcon != null)
            {
                s_Arrow.style.backgroundImage = new StyleBackground(arrowIcon);
                s_Arrow.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            }

            s_Button.Add(s_Arrow);

            // Insert just after PackageManager button if present, otherwise at the end
            var packageManagerButton = leftZone.Q("PackageManager");
            if (packageManagerButton != null)
            {
                var index = leftZone.IndexOf(packageManagerButton);
                leftZone.Insert(index + 1, s_Button);
            }
            else
            {
                // Add at the end if PackageManager not found
                leftZone.Add(s_Button);
            }

            // Set initial visibility
            s_IsVisible = !EditorPrefs.GetBool("HideAIMenu", false);
            UpdateVisibility();
            PreferencesUtils.RegisterHideMenuChanged(SetVisibility);

            s_IsInitialized = true;

#if UNITY_AI_TOOLKIT_DEBUG
            Debug.Log($"AIToolbarButtonLegacy: Successfully added toolbar button on Unity {Application.unityVersion}");
#endif
            return true;
        }

        static VisualElement GetToolbarRoot()
        {
            // Get the Toolbar instance
            var toolbarType = Type.GetType("UnityEditor.Toolbar, UnityEditor.CoreModule")
                ?? Type.GetType("UnityEditor.Toolbar, UnityEditor");

            if (toolbarType == null)
                return null;

            var getField = toolbarType.GetField("get", BindingFlags.Static | BindingFlags.Public);
            if (getField == null)
                return null;

            var toolbarInstance = getField.GetValue(null);
            if (toolbarInstance == null)
                return null;

            // Get the visual tree from the toolbar's window backend
            var windowBackendProp = toolbarType.GetProperty("windowBackend",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (windowBackendProp == null)
            {
                // Try the base class
                windowBackendProp = toolbarType.BaseType?.GetProperty("windowBackend",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            }

            if (windowBackendProp == null)
                return null;

            var windowBackend = windowBackendProp.GetValue(toolbarInstance);
            if (windowBackend == null)
                return null;

            // Get visualTree from the window backend
            var visualTreeProp = windowBackend.GetType().GetProperty("visualTree",
                BindingFlags.Instance | BindingFlags.Public);
            if (visualTreeProp == null)
                return null;

            return visualTreeProp.GetValue(windowBackend) as VisualElement;
        }

        static void SetVisibility(bool hidden)
        {
            s_IsVisible = !hidden;
            UpdateVisibility();
        }

        static void UpdateVisibility()
        {
            if (s_Button == null)
                return;

            s_Button.style.display = s_IsVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        static void OnButtonClicked()
        {
            if (s_Button == null)
                return;

            var popupContent = new AIDropdownContent();
            var buttonWorldBound = s_Button.worldBound;
            var popupPosition = new Rect(buttonWorldBound.x, buttonWorldBound.yMax, 0, 0);
            UnityEditor.PopupWindow.Show(popupPosition, popupContent);
        }
    }
}
#endif
