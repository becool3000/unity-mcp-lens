using System;
using System.Collections.Generic;
using Unity.AI.Toolkit.Accounts.Services.Core;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts.Components
{
    [UxmlElement]
    partial class PackagesUnsupportedBanner : BasicBannerContent
    {
        const string k_Tooltip = "Updating ensures access to the latest features, bug fixes, and continued support. Existing generated assets will not be affected.";

        public PackagesUnsupportedBanner()
            : base(
                "The installed versions of the Unity AI packages are outdated. Update the packages to continue prompting and generating. <link=package-manager><color=#7BAEFA>Open Package Manager</color></link>.",
                new[] { new LabelLink("package-manager", AccountLinks.OpenInPackageManager) }
            )
        {
            this.Query<VisualElement>().ForEach(ve => ve.tooltip = k_Tooltip);
        }

        protected PackagesUnsupportedBanner(string message, IEnumerable<LabelLink> links)
            : base(message, links)
        {
            this.Query<VisualElement>().ForEach(ve => ve.tooltip = k_Tooltip);
        }
    }
}
