using System;
using Unity.AI.Toolkit.Accounts.Services.Core;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts.Components
{
    [UxmlElement]
    partial class AIDisabledBanner : BasicBannerContent
    {
        public AIDisabledBanner() : base(
            "Your organization has disabled the use of Unity AI. Contact your organization’s managers to use these packages. <link=manageaccount><color=#7BAEFA>Manage account</color></link>.",
            new LabelLink("manageaccount", AccountLinks.ManageAccount)
        ) { }
    }
}
