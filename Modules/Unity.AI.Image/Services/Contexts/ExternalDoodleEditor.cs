namespace Unity.AI.Image.Services.Contexts
{
    record ExternalDoodleEditor(bool enabled)
    {
        public bool enabled { get; set; } = enabled;

        public const string key = nameof(ExternalDoodleEditor);
    }
}
