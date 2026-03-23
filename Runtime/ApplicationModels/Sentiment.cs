using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Unity.AI.Assistant.ApplicationModels
{
    /// <summary>
    /// Defines Sentiment
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    internal enum Sentiment
    {
        /// <summary>
        /// Enum Positive for value: positive
        /// </summary>
        [EnumMember(Value = "positive")]
        Positive = 1,

        /// <summary>
        /// Enum Negative for value: negative
        /// </summary>
        [EnumMember(Value = "negative")]
        Negative = 2
    }
}
