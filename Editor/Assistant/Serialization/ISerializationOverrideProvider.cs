namespace Unity.AI.Assistant.Editor.Serialization
{
    interface ISerializationOverrideProvider
    {
        ISerializationOverride Find(string declaringType, string field);
    }
}
