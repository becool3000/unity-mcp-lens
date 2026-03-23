using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts.Components
{
    [UxmlElement]
    partial class ConnectToCloudDelayedBanner : BasicBannerContent
    {
        public ConnectToCloudDelayedBanner() : base(
            "This Unity project's cloud connection is currently invalid. <link=open-cloud-services><color=#7BAEFA>Open services settings</color></link>",
            new[] { new LabelLink("open-cloud-services", () => SettingsService.OpenProjectSettings("Project/Services")) },
            "Loading cloud project", TimeSpan.FromSeconds(10)) { }
    }
}
