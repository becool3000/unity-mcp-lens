using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace Unity.AI.Toolkit.Accounts.Components
{
    /// <summary>
    /// A label that abstracts away rich text links.
    /// </summary>
    [UxmlElement]
    partial class RichLabel : Label
    {
        const string k_RichTextLink = "rich-text-link";

        public IEnumerable<LabelLink> links;
        public Func<string, string> TextProcessor = str => !EditorGUIUtility.isProSkin ? str.Replace("#7BAEFA", "#0479D9") : str;

        public RichLabel() : this(String.Empty) { }
        public RichLabel(string text) : base(text) => Init();
        public RichLabel(string text, IEnumerable<LabelLink> links) : this(text) => this.links = links?.ToList();

        void Init()
        {
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.unity.ai.assistant/Modules/Unity.AI.Toolkit.Accounts/Components/RichLabel/RichLabel.uss"));
            RegisterCallback<PointerDownLinkTagEvent>(TextLinkClick);
            RegisterCallback<PointerOverLinkTagEvent>(_ => AddToClassList(k_RichTextLink));
            RegisterCallback<PointerOutLinkTagEvent>(_ => RemoveFromClassList(k_RichTextLink));
        }

        public override string text
        {
            get => base.text;
            set
            {
                var str = TextProcessor?.Invoke(value) ?? value;
                base.text = str;
            }
        }

        void TextLinkClick(PointerDownLinkTagEvent evt) =>
            links?.FirstOrDefault(link => link.id == evt.linkID)?.action?.Invoke();
    }
}
