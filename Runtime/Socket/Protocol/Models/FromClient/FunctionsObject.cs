using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.AI.Assistant.Socket.Protocol.Models.FromClient
{
    class FunctionsObject
    {
        /// <summary>
        /// Must uniquely identify the function within this function array. The client must be able to receive
        /// this string and locate the function call it is attached too. It is up to the client to choose the
        /// shape of this identifier manage the binding between this id and the function.
        ///
        /// Examples: 1, b75cb780-d943-4a42-bc9f-9780b883016a, Namespace.Tools::Class.Function_arg1_return
        /// <br/>
        /// <br/>Example: ContextExtraction
        /// <br/>
        /// </summary>
        [JsonProperty("function_id", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string FunctionId { get; set; }

        /// <summary>
        /// Groups functions together.
        /// <br/>
        /// <br/>Example: ContextExtraction
        /// <br/>
        /// </summary>
        [JsonProperty("function_tag", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public List<string> FunctionTag { get; set; }

        /// <summary>
        /// Allowed modes for this function.
        /// <br/>
        /// <br/>Example: Agent
        /// <br/>
        /// </summary>
        [JsonProperty("function_mode", Required = Required.Default, NullValueHandling = NullValueHandling.Ignore)]
        public List<string> FunctionMode { get; set; }

        /// <summary>
        /// This value MAY be overridden by the backend.
        /// <br/>
        /// <br/>We are allowing the client to specify them, so that we can dynamically add functions.
        /// <br/>
        /// </summary>
        [JsonProperty("function_description", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string FunctionDescription { get; set; }

        /// <summary>
        /// Example - Unity.Muse.Chat.Context.SmartContext.ContextRetrievalTools:ProjectStructureExtractor
        /// <br/>Warning - Function namespace + Function names must be unique.
        /// <br/>
        /// </summary>
        [JsonProperty("function_namespace", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string FunctionNamespace { get; set; }

        /// <summary>
        /// Example - ProjectStructureExtractor
        /// <br/>Warning - Function namespace + Function names must be unique.
        /// <br/>
        /// </summary>
        [JsonProperty("function_name", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string FunctionName { get; set; }

        /// <summary>
        /// The parameters that are required to call the function - order is important,
        /// <br/>name is maybe not used for function calling.
        /// <br/>
        /// </summary>
        [JsonProperty("function_parameters", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public List<FunctionParametersObject> FunctionParameters { get; set; }


        public partial class FunctionParametersObject
        {
            /// <summary>
            /// The name of the parameter
            /// <br/>
            /// </summary>
            [JsonProperty("parameter_name", Required = Required.Always)]
            public string ParameterName { get; set; }

            /// <summary>
            /// The parameters type, in the form of the origin language. I.E.
            /// <br/>functions originating from Unity should be C# types.
            /// <br/>
            /// </summary>
            [JsonProperty("parameter_type", Required = Required.Always)]
            public string ParameterType { get; set; }

            /// <summary>
            /// A description of the parameter used by the LLM
            /// <br/>
            /// </summary>
            [JsonProperty("parameter_description", Required = Required.Always)]
            public string ParameterDescription { get; set; }

            /// <summary>
            /// Whether this parameter is optional or not
            /// </summary>
            [JsonProperty("parameter_is_optional")]
            public bool ParameterIsOptional { get; set; } = false;

            /// <summary>
            /// Full JSON schema for all parameter types. This provides rich type information
            /// including nested object properties, array types, validation rules, and constraints.
            /// This field is now required for all parameter types.
            /// </summary>
            /// <value>JSON schema object defining the parameter structure</value>
            [JsonProperty("parameter_json_schema", Required = Required.Always)]
            public JObject ParameterJsonSchema { get; set; }
        }
    }
}
