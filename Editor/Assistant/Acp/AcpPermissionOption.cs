using System;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Editor.Acp
{
    /// <summary>
    /// Permission option from ACP agent.
    /// </summary>
    class AcpPermissionOption
    {
        [JsonProperty("optionId")]
        public string OptionId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// Option kind: "allow_once", "allow_always", "reject_once", "reject_always"
        /// </summary>
        [JsonProperty("kind")]
        public string Kind { get; set; }
    }
}
