using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Relay.Editor.Acp;

namespace Unity.AI.Assistant.Editor.Acp
{
    /// <summary>
    /// Status of an ACP tool call as reported by the protocol.
    /// </summary>
    enum AcpToolCallStatus
    {
        Pending,
        Completed,
        Failed
    }

    #region JSON Models for Deserialization

    /// <summary>
    /// UI metadata following the MCP Apps pattern.
    /// </summary>
    class UiMetadata
    {
        [JsonProperty("resourceUri")]
        public string ResourceUri { get; set; }

        [JsonProperty("context")]
        public JObject Context { get; set; }
    }

    /// <summary>
    /// Metadata block (_meta) within tool outputs.
    /// </summary>
    class ToolOutputMeta
    {
        [JsonProperty("toolName")]
        public string ToolName { get; set; }

        [JsonProperty("toolExecutionId")]
        public string ToolExecutionId { get; set; }

        [JsonProperty("ui")]
        public UiMetadata Ui { get; set; }
    }

    /// <summary>
    /// Raw output structure from tool execution.
    /// </summary>
    class ToolRawOutput
    {
        [JsonProperty("success")]
        public bool? Success { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("data")]
        public JToken Data { get; set; }

        [JsonProperty("_meta")]
        public ToolOutputMeta Meta { get; set; }
    }

    /// <summary>
    /// Raw input structure for tool calls.
    /// </summary>
    class ToolRawInput
    {
        [JsonProperty("description")]
        public string Description { get; set; }
    }

    /// <summary>
    /// Content item within a tool call update.
    /// </summary>
    class ContentItem
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("content")]
        public ContentItem InnerContent { get; set; }
    }

    /// <summary>
    /// Session update metadata block.
    /// </summary>
    class UpdateMeta
    {
        [JsonProperty("toolName")]
        public string ToolName { get; set; }
    }

    /// <summary>
    /// Raw session update payload for tool calls.
    /// </summary>
    class ToolCallUpdatePayload
    {
        [JsonProperty("toolCallId")]
        public string ToolCallId { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("_meta")]
        public UpdateMeta Meta { get; set; }

        [JsonProperty("rawInput")]
        public ToolRawInput RawInput { get; set; }

        [JsonProperty("rawOutput")]
        public JToken RawOutput { get; set; }

        [JsonProperty("content")]
        public ContentItem[] Content { get; set; }
    }

    #endregion

    /// <summary>
    /// Information about an ACP tool call (from tool_call session updates).
    /// </summary>
    class AcpToolCallInfo
    {
        public string ToolCallId { get; set; }
        public string ToolName { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public AcpToolCallStatus Status { get; set; }

        /// <summary>
        /// The full rawInput JObject from the tool_call event.
        /// Preserved so auto-approved tool calls can display file content inline.
        /// </summary>
        [JsonIgnore]
        public JObject RawInput { get; set; }

        /// <summary>
        /// Parse tool call info from a session update payload.
        /// </summary>
        public static AcpToolCallInfo FromUpdate(JObject update)
        {
            if (update == null)
                return null;

            // Capture the full rawInput JObject before typed deserialization discards unknown fields.
            var rawInputJObject = update["rawInput"] as JObject;

            var payload = update.ToObject<ToolCallUpdatePayload>();

            // Use _meta.toolName (provider-agnostic) with fallback to title
            var toolName = payload.Meta?.ToolName ?? payload.Title;

            // Try to get description from rawInput.description or from content array
            var description = payload.RawInput?.Description;
            if (string.IsNullOrEmpty(description) && payload.Content?.Length > 0)
            {
                var innerContent = payload.Content[0]?.InnerContent;
                if (innerContent?.Type == "text")
                    description = innerContent.Text;
            }

            return new AcpToolCallInfo
            {
                ToolCallId = payload.ToolCallId,
                ToolName = toolName,
                Title = payload.Title,
                Description = description,
                Status = ParseStatus(payload.Status),
                RawInput = rawInputJObject
            };
        }

        static AcpToolCallStatus ParseStatus(string status) => status switch
        {
            AcpConstants.Status_Completed => AcpToolCallStatus.Completed,
            AcpConstants.Status_Failed => AcpToolCallStatus.Failed,
            _ => AcpToolCallStatus.Pending
        };
    }

    /// <summary>
    /// Update information for an ACP tool call (from tool_call_update session updates).
    /// </summary>
    class AcpToolCallUpdate
    {
        /// <summary>
        /// Maximum length for displayed content. Longer content will be truncated.
        /// This prevents UI issues with very large responses (e.g., base64 image data).
        /// </summary>
        const int k_MaxContentDisplayLength = 2000;

        public string ToolCallId { get; set; }
        public string ToolName { get; set; }
        public AcpToolCallStatus Status { get; set; }
        public string Content { get; set; }

        /// <summary>
        /// Tool execution ID from MCP approval flow (for linking results back to approval UI).
        /// This is set when the tool was executed via MCP with approval required.
        /// </summary>
        public string ToolExecutionId { get; set; }

        /// <summary>
        /// UI metadata for rendering tool results with specialized UI.
        /// Parsed from rawOutput._meta.ui following the MCP Apps pattern.
        /// </summary>
        public UiMetadata Ui { get; set; }

        /// <summary>
        /// The structured rawOutput from the tool call event, preserved for custom renderers
        /// that need to navigate the output structure (e.g., executionLogs, compilationLogs).
        /// </summary>
        [JsonProperty("rawOutput")]
        public JToken RawOutput { get; set; }

        /// <summary>
        /// Parse tool call update from a session update payload.
        /// </summary>
        public static AcpToolCallUpdate FromUpdate(JObject update)
        {
            if (update == null)
                return null;

            var payload = update.ToObject<ToolCallUpdatePayload>();

            string content = null;
            ToolOutputMeta outputMeta = null;

            // Normalize rawOutput: some providers (e.g., Claude Code via MCP) send it as a
            // JSON string rather than a parsed object. Parse it so downstream consumers
            // (renderers, widget extractors) can navigate the structure.
            var rawOutputToken = payload.RawOutput;
            if (rawOutputToken is { Type: JTokenType.String })
            {
                try
                {
                    rawOutputToken = JToken.Parse(rawOutputToken.Value<string>());
                }
                catch
                {
                    // Not valid JSON — keep as string token
                }
            }

            // Get content for display - prefer rawOutput (normalized by relay), fall back to content array
            if (rawOutputToken != null && rawOutputToken.Type != JTokenType.Null)
            {
                content = rawOutputToken.ToString(Formatting.Indented);

                // Parse the raw output to extract _meta
                if (rawOutputToken.Type == JTokenType.Object)
                {
                    var rawOutput = rawOutputToken.ToObject<ToolRawOutput>();
                    outputMeta = rawOutput?.Meta;
                }
            }
            else if (payload.Content?.Length > 0)
            {
                // Fall back to content array (legacy or providers that don't send rawOutput)
                var innerContent = payload.Content[0]?.InnerContent;
                if (innerContent?.Type == "text")
                    content = innerContent.Text;
            }

            // Truncate very long content to prevent UI issues (e.g., base64 image data)
            if (content != null && content.Length > k_MaxContentDisplayLength)
            {
                content = content.Substring(0, k_MaxContentDisplayLength) + "\n\n... [content truncated]";
            }

            return new AcpToolCallUpdate
            {
                ToolCallId = payload.ToolCallId,
                ToolName = payload.Meta?.ToolName,
                Status = ParseStatus(payload.Status),
                Content = content,
                ToolExecutionId = outputMeta?.ToolExecutionId,
                Ui = outputMeta?.Ui,
                RawOutput = rawOutputToken
            };
        }

        static AcpToolCallStatus ParseStatus(string status) => status switch
        {
            AcpConstants.Status_Completed => AcpToolCallStatus.Completed,
            AcpConstants.Status_Failed => AcpToolCallStatus.Failed,
            _ => AcpToolCallStatus.Pending
        };
    }
}
