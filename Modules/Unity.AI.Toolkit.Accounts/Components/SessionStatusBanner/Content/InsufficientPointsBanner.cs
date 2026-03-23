using System;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts.Components
{
    [UxmlElement]
    partial class InsufficientPointsBanner : BasicBannerContent
    {
        public InsufficientPointsBanner() : base("Insufficient Points") { }
    }
}
