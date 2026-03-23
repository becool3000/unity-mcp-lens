using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Unity.AI.Assistant.ApplicationModels
{
    /// <summary>
    /// Enum for the type of script being repaired.
    /// </summary>
    /// <value>Enum for the type of script being repaired.</value>
    [JsonConverter(typeof(StringEnumConverter))]
    internal enum ScriptType
    {
        /// <summary>
        /// Enum AgentAction for value: agent_action
        /// </summary>
        [EnumMember(Value = "agent_action")]
        AgentAction = 1,

        /// <summary>
        /// Enum Monobehaviour for value: monobehaviour
        /// </summary>
        [EnumMember(Value = "monobehaviour")]
        Monobehaviour = 2,

        /// <summary>
        /// Enum CodeGen for value: code_gen
        /// </summary>
        [EnumMember(Value = "code_gen")]
        CodeGen = 3
    }
}
