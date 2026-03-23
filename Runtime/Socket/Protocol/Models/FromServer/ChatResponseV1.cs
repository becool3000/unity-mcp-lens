using System.CodeDom.Compiler;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Socket.Protocol.Models.FromServer
{
    #pragma warning disable // Disable all warnings

    [GeneratedCode("NJsonSchema", "11.1.0.0 (Newtonsoft.Json v13.0.0.0)")]
    class ChatResponseV1 : IModel
    {
        [JsonProperty("$type")]
        public const string Type = "CHAT_RESPONSE_V1";
        public string GetModelType() => Type;

        [JsonProperty("message_id", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string MessageId { get; set; }

        /// <summary>
        /// whether streaming has finished
        /// <br/>
        /// </summary>
        [JsonProperty("last_message", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public bool LastMessage { get; set; }

        /// <summary>
        /// Markdown (bold/italic/hyperlinks/etc)
        /// <br/>
        /// <br/>Also uses some special blocks that are parsed by the
        /// <br/>editor.  Including:
        /// <br/>
        /// <br/>* Code
        /// <br/>* Action
        /// <br/>* Match3
        /// <br/>* ...?
        /// <br/>
        /// <br/>---
        /// <br/>
        /// <br/>Code blocks are wrapped with:
        /// <br/>
        /// <br/>  ```code
        /// <br/>  C# code here
        /// <br/>  ```
        /// <br/>
        /// <br/>Action blocks are wrapped with:
        /// <br/>
        /// <br/>  ```action
        /// <br/>  C# code here
        /// <br/>  ```
        /// <br/>
        /// </summary>
        [JsonProperty("markdown", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string Markdown { get; set; }



    }
}
