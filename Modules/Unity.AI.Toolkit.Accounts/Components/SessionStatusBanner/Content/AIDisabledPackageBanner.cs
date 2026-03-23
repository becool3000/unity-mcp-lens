using System;
using Unity.AI.Toolkit.Accounts.Services.Core;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts.Components
{
    [UxmlElement]
    partial class AIDisabledPackageBanner : BasicBannerContent
    {
        public AIDisabledPackageBanner() : base(
            "Your organization has disabled the use of this package. Contact your organization’s manager to enable it, or <link=manageaccount><color=#7BAEFA>Manage account</color></link>.",
            new LabelLink("manageaccount", AccountLinks.ManageAccount)
        ) { }
    }
}
