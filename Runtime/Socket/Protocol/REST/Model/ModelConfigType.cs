using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Unity.Ai.Assistant.Protocol.Model
{
    /// <summary>
    /// Type of model configuration option.
    /// </summary>
    /// <value>Type of model configuration option.</value>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ModelConfigType
    {
        /// <summary>
        /// Enum Profile for value: profile
        /// </summary>
        [EnumMember(Value = "profile")]
        Profile = 1,

        /// <summary>
        /// Enum Model for value: model
        /// </summary>
        [EnumMember(Value = "model")]
        Model = 2
    }

}
