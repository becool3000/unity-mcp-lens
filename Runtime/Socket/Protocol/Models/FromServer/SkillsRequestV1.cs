using System.CodeDom.Compiler;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Socket.Protocol.Models.FromServer
{
    #pragma warning disable // Disable all warnings

    /// <summary>
    /// Server-&gt;client request to get the skills that are available in the Unity project.
    /// <br/>
    /// <br/>This message follows the same pattern as CapabilitiesRequestV1.
    /// <br/>The server sends this request when it needs to know what skills are
    /// <br/>available in the project's skills folder.
    /// <br/>
    /// <br/>The client will respond with a SkillsResponseV1 containing:
    /// <br/>  - Skill name
    /// <br/>  - Skill description
    /// <br/>  - Skill file path
    /// <br/>  - Optional: Skill content
    /// </summary>
    [GeneratedCode("NJsonSchema", "11.1.0.0 (Newtonsoft.Json v13.0.0.0)")]
    class SkillsRequestV1 : IModel
    {
        [JsonProperty("$type")]
        public const string Type = "SKILLS_REQUEST_V1";

        public string GetModelType() => Type;
    }
}
