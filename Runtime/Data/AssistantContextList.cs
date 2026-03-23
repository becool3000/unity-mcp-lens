using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.AI.Assistant.Data
{
    [Serializable]
    internal class AssistantContextList
    {
        [SerializeField]
        internal List<AssistantContextEntry> m_ContextList = new List<AssistantContextEntry>();
    }
}
