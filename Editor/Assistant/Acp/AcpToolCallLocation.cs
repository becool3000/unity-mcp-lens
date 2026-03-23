using System;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Editor.Acp
{
    /// <summary>
    /// Tool call location information.
    /// </summary>
    class AcpToolCallLocation
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("line")]
        public int? Line { get; set; }
    }
}
