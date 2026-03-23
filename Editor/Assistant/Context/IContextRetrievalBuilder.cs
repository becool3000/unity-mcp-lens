using System.Collections.Generic;

namespace Unity.AI.Assistant.Editor.Context
{
    interface IContextRetrievalBuilder
    {
        IEnumerable<IContextSelection> GetSelectors();
    }
}
