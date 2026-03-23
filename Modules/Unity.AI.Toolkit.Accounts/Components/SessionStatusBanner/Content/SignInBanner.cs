using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts.Components
{
    [UxmlElement]
    partial class SignInBanner : BasicBannerContent
    {
        public SignInBanner() : base(
            "You need to be signed in to use AI features. <link=sign-in><color=#7BAEFA>Sign in</color></link>",
            new LabelLink("sign-in", () => SettingsService.OpenProjectSettings("Project/Services"))) { }
    }
}
