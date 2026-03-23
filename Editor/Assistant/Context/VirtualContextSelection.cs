namespace Unity.AI.Assistant.Editor.Context
{
    internal class VirtualContextSelection : IContextSelection
    {
        string m_Payload;
        string m_DisplayValue;
        string m_Description;
        string m_Type;
        object m_Metadata;

        public VirtualContextSelection(string payload, string displayValue, string description, string type = "Variable", object metadata = null)
        {
            m_Payload = payload;
            m_DisplayValue = displayValue;
            m_Description = description;
            m_Type = type;
            m_Metadata = metadata;
        }

        public bool Equals(IContextSelection other)
        {
            return Payload.Equals(other.Payload);
        }

        public string Classifier
        {
            get
            {
                return "Null";
            }
        }

        public string Description => m_Description;
        public string Payload => m_Payload;
        public string PayloadType => m_Type;
        public string DisplayValue => m_DisplayValue;
        public object Metadata => m_Metadata;

        string IContextSelection.DownsizedPayload => Payload;

        string IContextSelection.ContextType => m_Type;

        string IContextSelection.TargetName => m_DisplayValue;

        bool? IContextSelection.Truncated
        {
            get
            {
                return false;
            }
        }
    }
}
