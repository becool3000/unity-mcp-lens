using System;
using System.Collections.Generic;
using Unity.AI.Assistant.Data;

namespace Unity.AI.Assistant.Utils
{
    static class AssistantModeUtils
    {
        // Must match Python code
        static readonly Dictionary<AssistantMode, string> k_AssistantModeNames = new()
        {
            { AssistantMode.Ask, "ask" },
            { AssistantMode.Agent, "agent" }
        };

        public static bool IsValidSingleValue(this AssistantMode mode)
        {
            return k_AssistantModeNames.ContainsKey(mode);
        }

        public static string ToName(this AssistantMode mode)
        {
            if (!mode.IsValidSingleValue())
                throw new ArgumentException($"AssistantMode contain unsupported value or multiple flags: {mode}");

            return k_AssistantModeNames[mode];
        }

        public static List<string> ToNameList(this AssistantMode modes)
        {
            AssistantMode knownFlags = 0;

            var names = new List<string>();
            foreach (var kvp in k_AssistantModeNames)
            {
                knownFlags |= kvp.Key;

                if (modes.HasFlag(kvp.Key))
                    names.Add(kvp.Value);
            }

            var invalidBits = modes & ~knownFlags;
            if (invalidBits != 0)
                throw new ArgumentException($"AssistantMode contains invalid flag(s): {invalidBits}");

            return names;
        }
    }
}
