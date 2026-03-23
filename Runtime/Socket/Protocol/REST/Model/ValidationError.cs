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
    /// ValidationError
    /// </summary>
    [DataContract(Name = "ValidationError")]
    internal partial class ValidationError
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="ValidationError" /> class.
        /// </summary>
        [JsonConstructorAttribute]
        protected ValidationError() { }
        public ValidationError(List<ValidationErrorLocInner> loc, string msg, string type)
        {
            Loc = loc;
            Msg = msg;
            Type = type;
        }

        /// <summary>
        /// Gets or Sets Loc
        /// </summary>
        [DataMember(Name = "loc", IsRequired = true, EmitDefaultValue = true)]
        public List<ValidationErrorLocInner> Loc { get; set; }

        /// <summary>
        /// Gets or Sets Msg
        /// </summary>
        [DataMember(Name = "msg", IsRequired = true, EmitDefaultValue = true)]
        public string Msg { get; set; }

        /// <summary>
        /// Gets or Sets Type
        /// </summary>
        [DataMember(Name = "type", IsRequired = true, EmitDefaultValue = true)]
        public string Type { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class ValidationError {\n");
            sb.Append("  Loc: ").Append(Loc).Append("\n");
            sb.Append("  Msg: ").Append(Msg).Append("\n");
            sb.Append("  Type: ").Append(Type).Append("\n");
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
