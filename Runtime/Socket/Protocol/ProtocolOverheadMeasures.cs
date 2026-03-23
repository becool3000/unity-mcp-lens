using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.AI.Assistant.Socket.Protocol.Models.FromClient;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.Socket.Protocol
{
    static class ProtocolOverheadMeasures
    {
        static ProtocolOverheadMeasures()
        {
            var chatRequestV1 = new ChatRequestV1
            {
                Markdown = string.Empty,
                AttachedContext = new List<ChatRequestV1.AttachedContextModel>
                {
                    new()
                    {
                        Body = new ChatRequestV1.AttachedContextModel.TextBodyModel
                        {
                            Payload = string.Empty, Truncated = true
                        },
                        Metadata = new ChatRequestV1.AttachedContextModel.MetadataModel
                        {
                            DisplayValue = string.Empty,
                            EntryType = 1,
                            Value = string.Empty,
                            ValueIndex = 1,
                            ValueType = string.Empty
                        },
                        AdditionalProperties = new Dictionary<string, object> { { string.Empty, null } }
                    }
                }
            };

            var settings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Include
            };

            string json = AssistantJsonHelper.Serialize(chatRequestV1, settings);
            if (!string.IsNullOrEmpty(json))
            {
                MessageV1Overhead = json.Length;
            }
        }

        public static readonly int MessageV1Overhead;
    }
}
