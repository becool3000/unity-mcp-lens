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

namespace Unity.Ai.Assistant.Protocol.Model
{
    /// <summary>
    /// Indicates the author of the message
    /// </summary>
    /// <value>Indicates the author of the message</value>
    [JsonConverter(typeof(StringEnumConverter))]
    internal enum RoleV1
    {
        /// <summary>
        /// Enum User for value: user
        /// </summary>
        [EnumMember(Value = "user")]
        User = 1,

        /// <summary>
        /// Enum Assistant for value: assistant
        /// </summary>
        [EnumMember(Value = "assistant")]
        Assistant = 2
    }

}
