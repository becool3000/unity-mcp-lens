using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Unity.AI.Assistant.ApplicationModels
{
    /// <summary>
    /// Defines Category
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    internal enum Category
    {
        /// <summary>
        /// Enum ResponseQuality for value: response quality
        /// </summary>
        [EnumMember(Value = "response quality")]
        ResponseQuality = 1,

        /// <summary>
        /// Enum CodeGeneration for value: code generation
        /// </summary>
        [EnumMember(Value = "code generation")]
        CodeGeneration = 2,

        /// <summary>
        /// Enum SpeedToResponse for value: speed to response
        /// </summary>
        [EnumMember(Value = "speed to response")]
        SpeedToResponse = 3,

        /// <summary>
        /// Enum Sources for value: sources
        /// </summary>
        [EnumMember(Value = "sources")]
        Sources = 4,

        /// <summary>
        /// Enum AdditionalResources for value: additional resources
        /// </summary>
        [EnumMember(Value = "additional resources")]
        AdditionalResources = 5
    }
}
