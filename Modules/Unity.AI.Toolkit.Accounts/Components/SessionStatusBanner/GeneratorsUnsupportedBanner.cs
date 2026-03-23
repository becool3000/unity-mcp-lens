using System;
using Unity.AI.Toolkit.Accounts.Services.Core;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts.Components
{
    [UxmlElement]
    partial class GeneratorsUnsupportedBanner : PackagesUnsupportedBanner
    {
        public GeneratorsUnsupportedBanner()
            : base(
                "This version of Generators is outdated. Update the package to continue generating. <link=package-manager><color=#7BAEFA>Open Package Manager</color></link>.",
                new[] { new LabelLink("package-manager", AccountLinks.OpenGeneratorsInPackageManager) }
            ) { }
    }
}
