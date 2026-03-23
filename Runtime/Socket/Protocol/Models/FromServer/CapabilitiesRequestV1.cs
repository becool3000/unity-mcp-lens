using System.CodeDom.Compiler;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Socket.Protocol.Models.FromServer
{
    #pragma warning disable // Disable all warnings

    /// <summary>
    /// Server-&gt;client request to get the functions that the client supports.
    /// <br/>
    /// <br/>This message is strictly not required and we could probably exclusively
    /// <br/>have the response message but it is probably easier if we make the server
    /// <br/>explicitly control the workflow.
    /// <br/>
    /// <br/>Primarily we need to get:
    /// <br/>
    /// <br/>  1. The functions that we can call.
    /// <br/>
    /// <br/>      Examples: context, code-repair, etc
    /// <br/>
    /// <br/>  2. The output formats that the cleint supports.
    /// <br/>
    /// <br/>      Examples: markdown, code, action
    /// <br/>
    /// </summary>
    [GeneratedCode("NJsonSchema", "11.1.0.0 (Newtonsoft.Json v13.0.0.0)")]
    class CapabilitiesRequestV1 : IModel
    {
        [JsonProperty("$type")]
        public const string Type = "CAPABILITIES_REQUEST_V1";

        public string GetModelType() => Type;
    }
}
