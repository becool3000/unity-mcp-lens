using System;
using Unity.AI.Toolkit.Accounts.Services.Core;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts.Components
{
    [UxmlElement]
    partial class AssistantUnsupportedBanner : PackagesUnsupportedBanner
    {
        public AssistantUnsupportedBanner()
            : base(
                "This version of Assistant is outdated. Update the package to continue prompting. <link=package-manager><color=#7BAEFA>Open Package Manager</color></link>.",
                new[] { new LabelLink("package-manager", AccountLinks.OpenAssistantInPackageManager) }
            ) { }
    }
}
