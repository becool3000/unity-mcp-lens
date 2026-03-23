namespace Unity.AI.MCP.Editor.Constants
{
    /// <summary>
    /// Centralized tool descriptions and metadata.
    /// </summary>
    static class ToolDescriptions
    {
        // Parameter descriptions

        /// <summary>
        /// Description for script name parameter (without .cs extension).
        /// </summary>
        public const string ScriptNameDescription = "Script name (without .cs extension)";

        /// <summary>
        /// Description for asset path parameter relative to the project root.
        /// </summary>
        public const string AssetPathDescription = "Asset path relative to the project root";

        /// <summary>
        /// Description for URI parameter used in script validation.
        /// Supports unity://, file://, and Assets/ path formats.
        /// </summary>
        public const string UriDescription = "URI of the script to validate (unity://path/... or file://... or Assets/...)";

        /// <summary>
        /// Description for edits array parameter used in structured edit operations.
        /// </summary>
        public const string EditsArrayDescription = "Array of structured edit operations";

        /// <summary>
        /// Description for validation level parameter.
        /// </summary>
        public const string ValidationLevelDescription = "Validation level";

        /// <summary>
        /// Description for precondition SHA256 hash parameter used for conflict prevention.
        /// </summary>
        public const string PreconditionSha256Description = "Optional SHA256 hash of current file content for conflict prevention";

        // Schema descriptions for common types

        /// <summary>
        /// JSON schema type description for string values.
        /// </summary>
        public const string StringTypeDescription = "string";

        /// <summary>
        /// JSON schema type description for array values.
        /// </summary>
        public const string ArrayTypeDescription = "array";

        /// <summary>
        /// JSON schema type description for object values.
        /// </summary>
        public const string ObjectTypeDescription = "object";

        /// <summary>
        /// JSON schema type description for boolean values.
        /// </summary>
        public const string BooleanTypeDescription = "boolean";

        /// <summary>
        /// JSON schema type description for integer values.
        /// </summary>
        public const string IntegerTypeDescription = "integer";

        // Common enum values

        /// <summary>
        /// Supported validation levels for code validation operations.
        /// Includes: basic, standard, comprehensive, and strict.
        /// </summary>
        public static readonly string[] ValidationLevels = { "basic", "standard", "comprehensive", "strict" };

        /// <summary>
        /// Common action types used across MCP tools.
        /// Includes: apply, validate, create, read, update, and delete.
        /// </summary>
        public static readonly string[] CommonActions = { "apply", "validate", "create", "read", "update", "delete" };

        /// <summary>
        /// Refresh modes for editor operations.
        /// Includes: immediate, deferred, and none.
        /// </summary>
        public static readonly string[] RefreshModes = { "immediate", "deferred", "none" };
    }
}