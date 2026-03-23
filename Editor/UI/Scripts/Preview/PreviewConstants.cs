namespace Unity.AI.Assistant.UI.Editor.Scripts.Preview
{
    internal static class PreviewConstants
    {
        // Assembly names
        public const string UnityEngineUIElementsModule = "UnityEngine.UIElementsModule";
        public const string UnityEditorUIElementsModule = "UnityEditor.UIElementsModule";
        public const string UnityEngineUIElements = "UnityEngine.UIElements";
        public const string UnityEditorUIElements = "UnityEditor.UIElements";
        public const string UnityEngine = "UnityEngine";
        public const string UnityEditor = "UnityEditor";

        // XML/UXML attributes to skip
        public const string XmlnsPrefix = "xmlns";
        public const string XsiPrefix = "xsi";
        public const string EngineAttribute = "engine";
        public const string EditorAttribute = "editor";
        public const string NoNamespaceSchemaLocation = "noNamespaceSchemaLocation";
        public const string EditorExtensionMode = "editor-extension-mode";

        // Factory naming
        public const string FactorySuffix = "Factory";
        public const string UxmlFactoryInterface = "IUxmlFactory";
        public const string UxmlElementAttribute = "UxmlElement";

        // USS parsing patterns
        public const string UssCommentPattern = @"/\*.*?\*/";
        public const string UssRulePattern = @"([^{}]+)\s*\{([^}]*)\}";
        public const string UssPropertyPattern = @"([^:]+):\s*([^;]+);?";
    }
}
