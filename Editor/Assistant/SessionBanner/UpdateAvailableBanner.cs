using System;
using Unity.AI.Toolkit.Accounts.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.AI.Assistant.Editor;

namespace Unity.AI.Assistant.Editor.SessionBanner
{
    class UpdateAvailableBanner : BasicBannerContent
    {
        public UpdateAvailableBanner(string currentVersion, string latestVersion, Action onUpdate)
            : base(
                $"A new version of Assistant is available ({currentVersion} -> {latestVersion}).",
                null,
                true)
        {
            var buttonContainer = new VisualElement();
            buttonContainer.style.flexDirection = FlexDirection.Row;
            buttonContainer.style.alignSelf = Align.FlexEnd;
            buttonContainer.style.flexShrink = 0;

            var updateButton = new Button(() => onUpdate?.Invoke()) { text = "Update" };
            ColorUtility.TryParseHtmlString(AssistantConstants.UpdateButtonAccentColor, out var accentColor);
            updateButton.style.backgroundColor = accentColor;
            buttonContainer.Add(updateButton);

            var dismissButton = new Button(PackageUpdateState.instance.Dismiss) { text = "Dismiss" };
            buttonContainer.Add(dismissButton);

            var disableButton = new Button(() =>
            {
                AssistantEditorPreferences.EnablePackageAutoUpdate = false;
                PackageUpdateState.instance.Dismiss();
            }) { text = "Don't ask again" };
            buttonContainer.Add(disableButton);

            Add(buttonContainer);
        }
    }
}
