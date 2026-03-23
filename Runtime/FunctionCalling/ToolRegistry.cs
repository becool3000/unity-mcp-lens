
namespace Unity.AI.Assistant.FunctionCalling
{
    /// <summary>
    /// Registry for all available tools (functions) that can be called by the AI assistant.
    /// </summary>
    static class ToolRegistry
    {
        static FunctionToolbox s_FunctionToolbox;

        static FunctionCache FunctionCache { get; } = new(new AttributeBasedFunctionSource());

        public static FunctionToolbox FunctionToolbox
        {
            get
            {
                if (s_FunctionToolbox == null)
                {
                    s_FunctionToolbox = new FunctionToolbox();
                    s_FunctionToolbox.Initialize(FunctionCache);
                }

                return s_FunctionToolbox;
            }
        }
    }
}
