#if UNITY_AI_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools.InputSystem
{
    static class GetPossibleInteractionsUtils
    {
        [Serializable]
        public class InteractionEntry
        {
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("outputType")]
            public string OutputType { get; set; }
            [JsonProperty("Parameters")]
            public List<InputSystemUtils.ParameterInfo> Parameters { get; set; } = new List<InputSystemUtils.ParameterInfo>();
        }

        public static List<InteractionEntry> GetPossibleInteractions()
        {
            var interactionsList = new List<InteractionEntry>();

            foreach (var interactionInfo in InputInteraction.s_Interactions.table)
            {
                var entry = new InteractionEntry();
                var valueType = InputInteraction.GetValueType(interactionInfo.Value);
                if (valueType != null)
                {
                    entry.Name = interactionInfo.Key;
                    entry.OutputType = valueType.Name;
                    entry.Parameters = InputSystemUtils.GetParametersForType(interactionInfo.Value);
                }
                interactionsList.Add(entry);
            }
            return interactionsList;
        }
    }
}
#endif
