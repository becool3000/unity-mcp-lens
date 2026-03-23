using Newtonsoft.Json.Linq;

namespace Unity.AI.Assistant.Data
{
    interface IAssistantMessageBlock
    {
    }

    class ThoughtBlock : IAssistantMessageBlock
    {
        public string Content;
    }

    class PromptBlock : IAssistantMessageBlock
    {
        public string Content;
    }

    class AnswerBlock : IAssistantMessageBlock
    {
        public string Content;
        public bool IsComplete;
    }

    class FunctionCallBlock : IAssistantMessageBlock
    {
        public AssistantFunctionCall Call;
    }

    class ErrorBlock : IAssistantMessageBlock
    {
        public string Error;
    }

    /// <summary>
    /// Block type for storing ACP tool calls with their raw JSON data.
    /// This preserves all ACP-specific metadata without needing polymorphic serialization.
    /// </summary>
    class AcpToolCallStorageBlock : IAssistantMessageBlock
    {
        /// <summary>
        /// The raw JSON object representing the tool call.
        /// Can be parsed back to AcpToolCallInfo/AcpToolCallUpdate when needed.
        /// </summary>
        public JObject ToolCallData;
    }

    /// <summary>
    /// Block type for storing ACP plan updates with their raw JSON data.
    /// </summary>
    class AcpPlanStorageBlock : IAssistantMessageBlock
    {
        /// <summary>
        /// The raw JSON object representing the plan update.
        /// </summary>
        public JObject PlanData;
    }

    class AcpPlanStorageData
    {
        [Newtonsoft.Json.JsonProperty("entries")]
        public System.Collections.Generic.List<AcpPlanStorageEntry> Entries { get; set; } = new();
    }

    class AcpPlanStorageEntry
    {
        [Newtonsoft.Json.JsonProperty("content")]
        public string Content { get; set; }

        [Newtonsoft.Json.JsonProperty("status")]
        public string Status { get; set; }

        [Newtonsoft.Json.JsonProperty("priority")]
        public string Priority { get; set; }
    }
}
