namespace Unity.AI.Assistant.Editor
{
    class ErrorInfo
    {
        public string PublicMessage { get; set; }
        public string InternalMessage { get; set; }

        public ErrorInfo(string publicMessage, string internalMessage = null)
        {
            PublicMessage = publicMessage;
            InternalMessage = internalMessage;
        }
    }
}
