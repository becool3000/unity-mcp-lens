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
    /// ErrorResponseV1
    /// </summary>
    [DataContract(Name = "ErrorResponseV1")]
    internal partial class ErrorResponseV1
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="ErrorResponseV1" /> class.
        /// </summary>
        [JsonConstructorAttribute]
        protected ErrorResponseV1() { }
        public ErrorResponseV1(string detail, Guid uniqueCodeReference)
        {
            Detail = detail;
            UniqueCodeReference = uniqueCodeReference;
        }

        /// <summary>
        ///          A description of the error.          CAUTION: This detail will travel to the client and may be displayed to the         user.  Preferable to keep this kind of error somewhat vague so as not to         reveal system internals to outside actors.
        /// </summary>
        /// <value>         A description of the error.          CAUTION: This detail will travel to the client and may be displayed to the         user.  Preferable to keep this kind of error somewhat vague so as not to         reveal system internals to outside actors.     </value>
        [DataMember(Name = "detail", IsRequired = true, EmitDefaultValue = true)]
        public string Detail { get; set; }

        /// <summary>
        ///              A unique reference to a specific line of code where an error was thrown.              Used primarily, for monitoring and alerting on specific errors.              Also useful because it can provided to the client without revealing system internals             (unlike a stack trace).              For logging, stack traces are typically more useful as they provide more context.              IMPORTANT: The same UUID cannot be used for multiple places in the codebase.  To             generate a new UUID, can can use the &#x60;uuidgen&#x60; bash command.
        /// </summary>
        /// <value>             A unique reference to a specific line of code where an error was thrown.              Used primarily, for monitoring and alerting on specific errors.              Also useful because it can provided to the client without revealing system internals             (unlike a stack trace).              For logging, stack traces are typically more useful as they provide more context.              IMPORTANT: The same UUID cannot be used for multiple places in the codebase.  To             generate a new UUID, can can use the &#x60;uuidgen&#x60; bash command.         </value>
        [DataMember(Name = "unique_code_reference", IsRequired = true, EmitDefaultValue = true)]
        public Guid UniqueCodeReference { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class ErrorResponseV1 {\n");
            sb.Append("  Detail: ").Append(Detail).Append("\n");
            sb.Append("  UniqueCodeReference: ").Append(UniqueCodeReference).Append("\n");
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
