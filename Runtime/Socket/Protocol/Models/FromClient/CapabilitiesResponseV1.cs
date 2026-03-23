using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.AI.Assistant.Socket.Protocol.Models.FromClient
{
#pragma warning disable // Disable all warnings

    /// <summary>
    /// A documented list of the functions that are available to be called on the editor.
    /// <br/>
    /// <br/>The descriptions of the function and parameters are important because backend LLMs
    /// <br/>will decide to call (or not call) the functions using the info provided here.
    /// <br/>
    /// </summary>
    class CapabilitiesResponseV1 : IModel
    {
        [JsonProperty("$type")] public const string Type = "CAPABILITIES_RESPONSE_V1";

        public string GetModelType() => Type;

        [JsonProperty("functions", Required = Required.Always)]
        public List<FunctionsObject> Functions { get; set; } = new();

        [JsonProperty("agents", Required = Required.Default, NullValueHandling = NullValueHandling.Ignore)]
        public List<BaseAgentDefinitionV1> Agents { get; set; } = new();
    }
}
