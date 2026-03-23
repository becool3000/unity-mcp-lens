using System;

namespace Unity.AI.Assistant.Editor.Serialization
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    class SerializationOverrideAttribute : Attribute
    {
        public readonly string DeclaringType;
        public readonly string Field;

        public SerializationOverrideAttribute(Type declaringType, string field) : this(declaringType.FullName, field)
        {}

        public SerializationOverrideAttribute(string declaringType, string field)
        {
            DeclaringType = declaringType;
            Field = field;
        }
    }
}
