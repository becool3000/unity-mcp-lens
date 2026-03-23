using System;

namespace Unity.AI.Toolkit.Accounts.Components
{
    record LabelLink
    {
        public string id;
        public Action action;

        public LabelLink() {}
        public LabelLink(string id, Action action)
        {
            this.id = id;
            this.action = action;
        }
    }
}
