using System;
using System.Collections.Generic;
using Unity.AI.Toolkit.Accounts.Services;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts.Components
{
    [UxmlElement]
    partial class LegalAgreement : VisualElement
    {
        internal record AIData
        {
            public string text;
            public List<LabelUrlLink> links;
            public List<string> packages;
            public string installButtonText;
            public string noInternet;
            public string installingPackages;
            public Action installButtonAction;
        }

        internal static readonly AIData data = new()
        {
            text = "I have read and agree to the <link=terms><color=#7BAEFA>Unity Terms of Service</color></link>."
                + "\n\nWhen using Unity AI, including third-party models, you are responsible for ensuring your use of " +
                "Unity AI and any generated assets do not infringe on third-party rights and are appropriate for your use." +
                "\n\nSee <link=thirdparty><color=#7BAEFA>Unity AI Models and Partners</color></link> for more information.",
            links = new()
            {
                new() {id = "terms", url = "https://unity.com/legal/terms-of-service"},
                new() {id = "supplemental", url = "https://unity.com/legal/supplemental-privacy-statement-unity-muse"},
                new() {id = "thirdparty", url = "https://unity.com/legal/unityai-models-partners"}
            },
            noInternet = "You need an internet connection to be able to use the AI features.",
            installingPackages = "Installing packages",

            packages = new()
            {
                "com.unity.ai.assistant"
            },

            installButtonText = "Agree and install Unity AI",
            installButtonAction = () => _ = AccountController.SetTermsOfService()
        };

        public LegalAgreement()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.unity.ai.assistant/Modules/Unity.AI.Toolkit.Accounts/Components/LegalAgreement/LegalAgreement.uxml");
            tree.CloneTree(this);

            var text = this.Q<RichLabel>("legal-text");
            text.links = data.links;
            text.text = data.text;

            var button = this.Q<Button>("agree-button");
            button.text = data.installButtonText;
            button.clicked += data.installButtonAction;

            Add(text);
            Add(button);
        }
    }
}
