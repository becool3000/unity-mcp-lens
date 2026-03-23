using System;
using Unity.Serialization.Json;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Tools.Editor
{
    class SerializedObjectJsonAdapter : IJsonAdapter<SerializedObject>
    {
        public void Serialize(in JsonSerializationContext<SerializedObject> context, SerializedObject value)
        {
            if (value == null)
            {
                context.Writer.WriteNull();
                return;
            }

            using (context.Writer.WriteObjectScope())
            {
                var property = value.GetIterator();
                var enterChildren = true;

                while (property.Next(enterChildren))
                {
                    context.SerializeValue(property);
                    enterChildren = false;
                }
            }
        }

        public SerializedObject Deserialize(in JsonDeserializationContext<SerializedObject> context)
        {
            throw new NotImplementedException();
        }
    }
}
