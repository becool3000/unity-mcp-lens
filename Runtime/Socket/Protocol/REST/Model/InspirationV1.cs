using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using OpenAPIDateConverter = Unity.Ai.Assistant.Protocol.Client.OpenAPIDateConverter;
using Unity.AI.Assistant.Utils;

namespace Unity.Ai.Assistant.Protocol.Model
{
    /// <summary>
    /// Model representing an instance of inspiration.  Attributes:     id (str): A unique identifier for the inspiration object.     mode (InspirationMode): The mode of operation for the inspiration.     value (str): The specific content related to the inspiration.     description (Optional[str]): A human-readable description of the inspiration (optional).  The Inspiration object is a suggested prompt that can be presented to users  to stimulate their interaction with the AI Assistant service.
    /// </summary>
    [DataContract(Name = "InspirationV1")]
    internal partial class InspirationV1
    {

        /// <summary>
        /// Inspiration mode
        /// </summary>
        /// <value>Inspiration mode</value>
        [JsonConverter(typeof(StringEnumConverter))]
        internal enum ModeEnum
        {
            /// <summary>
            /// Enum Ask for value: ask
            /// </summary>
            [EnumMember(Value = "ask")]
            Ask = 1,

            /// <summary>
            /// Enum Run for value: run
            /// </summary>
            [EnumMember(Value = "run")]
            Run = 2,

            /// <summary>
            /// Enum Code for value: code
            /// </summary>
            [EnumMember(Value = "code")]
            Code = 3
        }


        /// <summary>
        /// Initializes a new instance of the <see cref="InspirationV1" /> class.
        /// </summary>
        [JsonConstructorAttribute]
        protected InspirationV1() { }
        public InspirationV1(Guid id, InspirationV1.ModeEnum mode, string value)
        {
            Id = id;
            Mode = mode;
            Value = value;
        }

        /// <summary>
        /// Inspiration mode
        /// </summary>
        /// <value>Inspiration mode</value>
        [DataMember(Name = "mode", IsRequired = true, EmitDefaultValue = true)]
        public ModeEnum Mode { get; set; }

        /// <summary>
        /// Uniform inspiration Id
        /// </summary>
        /// <value>Uniform inspiration Id</value>
        [DataMember(Name = "id", IsRequired = true, EmitDefaultValue = true)]
        public Guid Id { get; set; }

        /// <summary>
        /// Inspiration value
        /// </summary>
        /// <value>Inspiration value</value>
        [DataMember(Name = "value", IsRequired = true, EmitDefaultValue = true)]
        public string Value { get; set; }

        /// <summary>
        /// Gets or Sets Description
        /// </summary>
        [DataMember(Name = "description", EmitDefaultValue = true)]
        public string Description { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class InspirationV1 {\n");
            sb.Append("  Id: ").Append(Id).Append("\n");
            sb.Append("  Mode: ").Append(Mode).Append("\n");
            sb.Append("  Value: ").Append(Value).Append("\n");
            sb.Append("  Description: ").Append(Description).Append("\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>
        /// Returns the JSON string presentation of the object
        /// </summary>
        /// <returns>JSON string presentation of the object</returns>
        public virtual string ToJson()
        {
            return AssistantJsonHelper.Serialize(this);
        }
    }

}
