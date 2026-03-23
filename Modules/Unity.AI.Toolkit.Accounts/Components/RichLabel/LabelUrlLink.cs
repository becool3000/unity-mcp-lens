using System;
using UnityEngine;

namespace Unity.AI.Toolkit.Accounts.Components
{
    record LabelUrlLink : LabelLink
    {
        public string url;

        public LabelUrlLink() => action += () => Application.OpenURL(url);

        public LabelUrlLink(string id, string url) : this()
        {
            this.id = id;
            this.url = url;
        }
    }
}
