using System;
using System.CodeDom.Compiler;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.AI.Assistant.Socket.Protocol.Models.FromServer
{
    #pragma warning disable // Disable all warnings

    [GeneratedCode("NJsonSchema", "11.1.0.0 (Newtonsoft.Json v13.0.0.0)")]
    class FunctionCallRequestV1 : IModel
    {
        [JsonProperty("$type")]
        public const string Type = "FUNCTION_CALL_REQUEST_V1";
        public string GetModelType() => Type;

        /// <summary>
        /// An id to allow us to join function-calls-requests and function-call-responses
        /// </summary>
        [JsonProperty("call_id", Required = Required.Always)]
        public Guid CallId { get; set; }

        /// <summary>
        /// The name of the function to execute on the client
        /// </summary>
        [JsonProperty("function_id", Required = Required.Always)]
        public string FunctionId { get; set; }

        /// <summary>
        /// The shape of this field depends on the function declared in the Capabilities Response V1. It is expressed as a
        ///   JSON object, i.e. Key[string]Value[Any] pairs. Each KeyValue pair represents a parameter declared in the
        /// function_parameters dictionary of the function whose function_id matches the function_id in this request.
        ///
        /// Functions are called by parameter name. The order of items inside `function_parameters` is random and should not be relied on.
        /// </summary>
        [JsonProperty("function_parameters", Required = Required.Always)]
        public JObject FunctionParameters { get; set; }
    }
}
