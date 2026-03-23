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
    /// VersionSupportInfo
    /// </summary>
    [DataContract(Name = "VersionSupportInfo")]
    internal partial class VersionSupportInfo
    {

        /// <summary>
        /// The support status of the version. Supported versions are versions that are currently supported. Deprecated versions are versions that are no longer supported but still work. Unsupported versions are versions that are no longer supported and do not work.
        /// </summary>
        /// <value>The support status of the version. Supported versions are versions that are currently supported. Deprecated versions are versions that are no longer supported but still work. Unsupported versions are versions that are no longer supported and do not work.</value>
        [JsonConverter(typeof(StringEnumConverter))]
        internal enum SupportStatusEnum
        {
            /// <summary>
            /// Enum Supported for value: supported
            /// </summary>
            [EnumMember(Value = "supported")]
            Supported = 1,

            /// <summary>
            /// Enum Deprecated for value: deprecated
            /// </summary>
            [EnumMember(Value = "deprecated")]
            Deprecated = 2,

            /// <summary>
            /// Enum Unsupported for value: unsupported
            /// </summary>
            [EnumMember(Value = "unsupported")]
            Unsupported = 3
        }


        /// <summary>
        /// Initializes a new instance of the <see cref="VersionSupportInfo" /> class.
        /// </summary>
        [JsonConstructorAttribute]
        public VersionSupportInfo() { }
        public VersionSupportInfo(string routePrefix, VersionSupportInfo.SupportStatusEnum supportStatus)
        {
            RoutePrefix = routePrefix;
            SupportStatus = supportStatus;
        }

        /// <summary>
        /// The support status of the version. Supported versions are versions that are currently supported. Deprecated versions are versions that are no longer supported but still work. Unsupported versions are versions that are no longer supported and do not work.
        /// </summary>
        /// <value>The support status of the version. Supported versions are versions that are currently supported. Deprecated versions are versions that are no longer supported but still work. Unsupported versions are versions that are no longer supported and do not work.</value>
        [DataMember(Name = "support_status", IsRequired = true, EmitDefaultValue = true)]
        public SupportStatusEnum SupportStatus { get; set; }

        /// <summary>
        /// The route prefix for the version, expressed as v#. For example, v1.
        /// </summary>
        /// <value>The route prefix for the version, expressed as v#. For example, v1.</value>
        [DataMember(Name = "route_prefix", IsRequired = true, EmitDefaultValue = true)]
        public string RoutePrefix { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class VersionSupportInfo {\n");
            sb.Append("  RoutePrefix: ").Append(RoutePrefix).Append("\n");
            sb.Append("  SupportStatus: ").Append(SupportStatus).Append("\n");
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
