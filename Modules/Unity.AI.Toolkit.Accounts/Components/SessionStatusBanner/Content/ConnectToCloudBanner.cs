using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts.Components
{
    [UxmlElement]
    partial class ConnectToCloudBanner : BasicBannerContent
    {
        public ConnectToCloudBanner() : base(
            "This Unity project is missing a cloud connection. <link=connect-to-cloud><color=#7BAEFA>Select a cloud project</color></link>",
            new LabelLink("connect-to-cloud", () => SettingsService.OpenProjectSettings("Project/Services"))) { }
    }
}
