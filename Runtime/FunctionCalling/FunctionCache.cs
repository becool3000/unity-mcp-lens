using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.AI.Assistant.FunctionCalling
{
    class FunctionCache
    {
        readonly List<LocalAssistantFunction> m_Functions = new();

        public IEnumerable<LocalAssistantFunction> AllFunctions => m_Functions;

        public FunctionCache(IFunctionSource contextSource)
        {
            // Build list of available tool methods:
            m_Functions.Clear();
            m_Functions.AddRange(contextSource?.GetFunctions() ?? Array.Empty<LocalAssistantFunction>());
        }

        public IEnumerable<LocalAssistantFunction> GetFunctionsByTags(params string[] tags)
            => m_Functions?
                .Where(info => info != null && info.FunctionDefinition != null && info.FunctionDefinition.Tags != null)
                .Where(info => info.FunctionDefinition.Tags.Intersect(tags).Any())
               ?? new List<LocalAssistantFunction>();

        public IEnumerable<LocalAssistantFunction> GetAllFunctions() => m_Functions;
    }
}
