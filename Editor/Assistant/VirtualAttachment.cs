using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor.Context;

namespace Unity.AI.Assistant.Editor
{
    internal class VirtualAttachment
    {
        public VirtualAttachment(string payload, string type, string displayName, object metadata)
        {
            Payload = payload;
            Type = type;
            DisplayName = displayName;
            Metadata =  metadata;
        }

        public readonly string Payload;
        public string Type;
        public string DisplayName;
        public object Metadata;

        public AssistantContextEntry ToContextEntry()
        {
            return new AssistantContextEntry
            {
                Value = Payload,
                ValueType = Type,
                DisplayValue = DisplayName,
                EntryType = AssistantContextType.Virtual,
                Metadata = Metadata  // Use the original metadata (e.g., ImageContextMetaData)
            };
        }

        public VirtualContextSelection ToContextSelection()
        {
            return new VirtualContextSelection(Payload,
                DisplayName,
                string.Empty,
                Type,
                metadata: Metadata);
        }

        public static VirtualAttachment FromContextEntry(AssistantContextEntry entry)
        {
            return new VirtualAttachment(entry.Value, entry.ValueType, entry.DisplayValue, entry.Metadata);
        }

        public override bool Equals(object obj)
        {
            if (obj is not VirtualAttachment other)
            {
                return false;
            }

            // Compare by Payload and Type since Payload contains the unique PNG data
            return Payload == other.Payload && Type == other.Type;
        }

        public override int GetHashCode()
        {
            // Use Payload and Type for hash code generation
            return System.HashCode.Combine(Payload, Type);
        }
    }
}
