using Unity.AI.Toolkit.Accounts.Services.Core;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts.Components
{
    [UxmlElement]
    partial class AssistantInsufficientPointsBanner : BasicBannerContent
    {
        public AssistantInsufficientPointsBanner()
            : base(
                "Insufficient points. <link=get-points><color=#7BAEFA>Request a points top-up</color></link> to keep using Unity AI. Your points refresh automatically each week.",
                new[] { new LabelLink("get-points", AccountLinks.GetPoints) },
                false) { }
    }
}