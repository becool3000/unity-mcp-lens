using System;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts.Components
{
    [UxmlElement]
    partial class SettingsFailedBanner : BasicBannerContent
    {
        public SettingsFailedBanner() : base("Could not retrieve settings for unknown reasons.") { }
    }
}
