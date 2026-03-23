namespace Unity.AI.MCP.Editor.Helpers
{
    /// <summary>
    /// Provides standardized response formatting for MCP tool return values.
    /// Use these methods to create consistent success and error responses that MCP clients can reliably parse.
    /// </summary>
    /// <remarks>
    /// All MCP tools should return responses using these methods to ensure:
    /// - Consistent JSON structure across all tools
    /// - Clear success/failure indicators
    /// - Structured error reporting
    /// - Optional metadata for widget hints and other UI integration
    ///
    /// Response format:
    /// - Success: { success: true, message: "...", data?: {...}, _meta?: {...} }
    /// - Error: { success: false, code: "...", error: "...", data?: {...} }
    /// </remarks>
    /// <example>
    /// <code>
    /// [McpTool("example_tool", "Example tool")]
    /// public static object ExampleTool(ExampleParams params)
    /// {
    ///     if (string.IsNullOrEmpty(params.Name))
    ///     {
    ///         return Response.Error("INVALID_NAME", new { field = "name", reason = "Cannot be empty" });
    ///     }
    ///
    ///     var result = PerformOperation(params.Name);
    ///     return Response.Success($"Operation completed for {params.Name}", new { itemsProcessed = result });
    /// }
    ///
    /// // With widget metadata for UI rendering hints:
    /// return Response.Success("Asset created", new { path = assetPath }, meta: new {
    ///     widget = new { type = "asset_preview", data = new { assetGuid = guid } }
    /// });
    /// </code>
    /// </example>
    public static class Response
    {
        /// <summary>
        /// Creates a standardized success response for MCP tool execution.
        /// </summary>
        /// <param name="message">Human-readable message describing what was accomplished</param>
        /// <param name="data">Optional additional data to include in the response</param>
        /// <param name="meta">Optional metadata to include in the response (e.g., widget hints for UI rendering)</param>
        /// <returns>An object with { success: true, message: string, data?: object, _meta?: object } structure</returns>
        public static object Success(string message, object data = null, object meta = null)
        {
            if (data != null && meta != null)
                return new { success = true, message, data, _meta = meta };
            if (data != null)
                return new { success = true, message, data };
            if (meta != null)
                return new { success = true, message, _meta = meta };
            return new { success = true, message };
        }

        /// <summary>
        /// Creates a standardized error response for MCP tool execution failures.
        /// </summary>
        /// <remarks>
        /// Error codes should be:
        /// - UPPERCASE_SNAKE_CASE for machine parsing
        /// - Descriptive of the error category (e.g., "INVALID_PARAMETER", "FILE_NOT_FOUND", "PERMISSION_DENIED")
        /// - Consistent across similar error types
        ///
        /// The same string is included in both "code" and "error" fields for backward compatibility.
        /// </remarks>
        /// <param name="errorCodeOrMessage">Machine-parsable error code or human-readable error message</param>
        /// <param name="data">Optional additional error details</param>
        /// <returns>An object with { success: false, code: string, error: string, data?: object } structure</returns>
        public static object Error(string errorCodeOrMessage, object data = null)
        {
            // The code field Preserve original behavior while adding a machine-parsable code field.
            // If callers pass a code string, it will be echoed in both code and error.
            if (data != null)
                return new { success = false, code = errorCodeOrMessage, error = errorCodeOrMessage, data };
            return new { success = false, code = errorCodeOrMessage, error = errorCodeOrMessage };
        }
    }
}
