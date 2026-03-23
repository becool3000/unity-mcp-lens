namespace Unity.AI.Assistant.Editor.CodeAnalyze
{
    class ClassCodeTextDefinition
    {
        string m_ClassName;
        string m_Code;

        public string ClassName => m_ClassName;

        public string Code => m_Code;

        public ClassCodeTextDefinition(string name, string code)
        {
            m_ClassName = name;
            m_Code = code;
        }

    }
}
