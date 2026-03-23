using System;
using System.Linq;
using Unity.AI.Toolkit.Accounts.Services;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts
{
#if UNITY_6000_3_OR_NEWER
    static class AIToolbarButton
    {
        const string k_UssClassName = "ai-toolbar-button";
        const string k_OverlayUssClassName = "ai-toolbar-button-bg-overlay";
        const string k_NotificationVariantUssClassName = "ai-toolbar-button--with-points-notifications";

        const string k_StyleSheetPath =
            "Packages/com.unity.ai.assistant/Modules/Unity.AI.Toolkit.Accounts/Components/AIToolbarButton/AIToolbarButton.uss";

        const int k_NotificationDurationMs = 2000;
        const int k_RefreshDelayMs = 5000;

        static Button AIButton => AIDropdownController.aiButton;
        // Hard to pin down the exact element as it changes name id per Unity version. Structure seems somewhat stable though, so we are basing query on that.
        static TextElement STextElement => AIButton.Query<TextElement>().ToList().LastOrDefault();
        static string s_OriginalContent;
        static IVisualElementScheduledItem s_AIToolbarButtonSchedule;
        static IVisualElementScheduledItem s_RefreshSchedule;

        public static void ShowPointsCostNotification(int amount)
        {
            if (AIButton == null)
                return;

            s_AIToolbarButtonSchedule?.Pause();
            s_RefreshSchedule?.Pause();
            STextElement.text = $"-{amount}";
            s_AIToolbarButtonSchedule = AIButton.schedule.Execute(() =>
            {
                STextElement.text = s_OriginalContent;
                AIButton.RemoveFromClassList(k_NotificationVariantUssClassName);
            }).StartingIn(k_NotificationDurationMs);
            AIButton.AddToClassList(k_NotificationVariantUssClassName);
            s_RefreshSchedule = AIButton.schedule.Execute(Account.pointsBalance.Refresh)
                .StartingIn(k_RefreshDelayMs);
        }

        internal static void Init()
        {
            if (AIButton == null)
                return;

            if (string.IsNullOrEmpty(s_OriginalContent))
                s_OriginalContent = STextElement.text;

            // Inject the background overlay ---
            if (AIButton.Q(className: k_OverlayUssClassName) == null)
            {
                var overlay = new VisualElement();
                overlay.AddToClassList(k_OverlayUssClassName);
                // Insert at index 0 so it sits behind text/icons
                AIButton.Insert(0, overlay);
                overlay.pickingMode = PickingMode.Ignore;
            }

            if (AIButton.ClassListContains(k_UssClassName))
                return;

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(k_StyleSheetPath);
            if (!styleSheet)
                return;

            AIButton.styleSheets.Add(styleSheet);
            AIButton.AddToClassList(k_UssClassName);
        }
    }
#else
    static class AIToolbarButton
    {
        public static void ShowPointsCostNotification(int amount)
        {
            // Delegate to the legacy implementation
            AIToolbarButtonLegacy.ShowPointsCostNotification(amount);
        }

        internal static void Init()
        {
            // Legacy button is initialized via InitializeOnLoadMethod in AIToolbarButtonLegacy
        }
    }
#endif
}
