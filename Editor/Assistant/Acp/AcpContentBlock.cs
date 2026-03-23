namespace Unity.AI.Assistant.Editor.Acp
{
    abstract class AcpContentBlock
    {
        public abstract string Type { get; }
    }

    class AcpTextContent : AcpContentBlock
    {
        public override string Type => "text";
        public string Text { get; set; }
    }

    class AcpImageContent : AcpContentBlock
    {
        public override string Type => "image";
        public string MimeType { get; set; }
        public string Data { get; set; }
    }

    class AcpResourceContent : AcpContentBlock
    {
        public override string Type => "resource";
        public AcpResourceData Resource { get; set; }
    }

    class AcpResourceData
    {
        public string Text { get; set; }
        public string Uri { get; set; }
        public string MimeType { get; set; }
    }
}
