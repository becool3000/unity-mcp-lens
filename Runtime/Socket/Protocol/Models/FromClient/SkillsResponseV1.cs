using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.AI.Assistant.Skills;

namespace Unity.AI.Assistant.Socket.Protocol.Models.FromClient
{
#pragma warning disable // Disable all warnings

    /// <summary>
    /// Response containing the list of skills available in the Unity project.
    /// <br/>
    /// <br/>This message is sent in response to SkillsRequestV1 from the server.
    /// <br/>It contains all valid skills found in the project's skills folder.
    /// </summary>
    class SkillsResponseV1 : IModel
    {
        [JsonProperty("$type")]
        public const string Type = "SKILLS_RESPONSE_V1";

        public string GetModelType() => Type;

        [JsonProperty("skills", Required = Required.Always)]
        public List<SkillMetaData> Skills { get; set; } = new();

        [JsonProperty("hash", Required = Required.Default, NullValueHandling = NullValueHandling.Ignore)]
        public string Hash { get; set; }

        [JsonProperty("unchanged", Required = Required.Default, NullValueHandling = NullValueHandling.Ignore)]
        public bool? Unchanged { get; set; }
    }
}
