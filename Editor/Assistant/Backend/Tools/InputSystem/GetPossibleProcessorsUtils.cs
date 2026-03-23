#if UNITY_AI_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools.InputSystem
{
    static class GetPossibleProcessorsUtils
    {
        internal const string k_FunctionName = "GetPossibleProcessors";

        [Serializable]
        public class ProcessorEntry
        {
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("OutputType")]
            public string OutputType { get; set; }
            [JsonProperty("Parameters")]
            public List<InputSystemUtils.ParameterInfo> Parameters { get; set; } = new List<InputSystemUtils.ParameterInfo>();
        }

        public static List<ProcessorEntry> GetPossibleProcessors()
        {
            var processorList = new List<ProcessorEntry>();

            foreach (var processorInfo in InputProcessor.s_Processors.table)
            {
                var entry = new ProcessorEntry();
                var valueType = InputProcessor.GetValueTypeFromType(processorInfo.Value);
                if (valueType != null)
                {
                    entry.Name = processorInfo.Key;
                    entry.OutputType = valueType.Name;
                    entry.Parameters = InputSystemUtils.GetParametersForType(processorInfo.Value);
                }
                processorList.Add(entry);
            }
            return processorList;
        }
    }
}
#endif
