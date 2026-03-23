using System;
using Unity.AI.Toolkit.Accounts.Services.Core;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts.Components
{
    [UxmlElement]
    partial class LowPointsBanner : BasicBannerContent
    {
        const string k_Tooltip = "Only 10% of your org's points remain. Request a points top-up to continue generating content without interruption.";
        const string k_DismissedPrefKey = "Unity.AI.Toolkit.LowPointsBannerDismissed";

        public static bool IsDismissed => EditorPrefs.GetBool(k_DismissedPrefKey, false);

        public static void ResetDismissed() => EditorPrefs.DeleteKey(k_DismissedPrefKey);

        public LowPointsBanner() : this(null) { }

        public LowPointsBanner(Action onDismiss)
            : base(
                "Only 10% of your org's points remain. <link=get-points><color=#7BAEFA>Request a points top-up</color></link>. Points refresh automatically each week.",
                new[] { new LabelLink("get-points", AccountLinks.GetPoints) },
            true)
        {
            this.Query<VisualElement>().ForEach(ve => ve.tooltip = k_Tooltip);

            var dismissButton = new Button(() =>
            {
                EditorPrefs.SetBool(k_DismissedPrefKey, true);
                onDismiss?.Invoke();
            }) { text = "Dismiss" };
            dismissButton.AddToClassList("banner-right-action");
            content.Add(dismissButton);
        }
    }
}
