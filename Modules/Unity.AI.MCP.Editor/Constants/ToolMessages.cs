namespace Unity.AI.MCP.Editor.Constants
{
    /// <summary>
    /// Centralized error messages and descriptions for tools.
    /// </summary>
    static class ToolMessages
    {
        // Common error messages

        /// <summary>
        /// Error message for null parameters.
        /// </summary>
        public const string ParametersCannotBeNull = "Parameters cannot be null.";

        /// <summary>
        /// Error message when name parameter is missing.
        /// </summary>
        public const string NameIsRequired = "Name is required.";

        /// <summary>
        /// Error message when file path parameter is missing.
        /// </summary>
        public const string FilePathIsRequired = "File path is required.";

        /// <summary>
        /// Error message when action parameter is missing.
        /// </summary>
        public const string ActionIsRequired = "Action is required.";

        /// <summary>
        /// Error message format for invalid action values.
        /// Use with string.Format to include the invalid action name.
        /// </summary>
        public const string InvalidAction = "Invalid action: {0}";

        /// <summary>
        /// Error message format for unknown action types.
        /// Use with string.Format to include the unknown action name.
        /// </summary>
        public const string UnknownAction = "Unknown action: {0}";

        // File operation messages

        /// <summary>
        /// Error message format when a file cannot be found.
        /// Use with string.Format to include the file path.
        /// </summary>
        public const string FileNotFound = "File not found: {0}";

        /// <summary>
        /// Success message format for file creation.
        /// Use with string.Format to include the file path.
        /// </summary>
        public const string FileCreatedSuccessfully = "File created successfully: {0}";

        /// <summary>
        /// Success message format for file updates.
        /// Use with string.Format to include the file path.
        /// </summary>
        public const string FileUpdatedSuccessfully = "File updated successfully: {0}";

        /// <summary>
        /// Success message format for file deletion.
        /// Use with string.Format to include the file path.
        /// </summary>
        public const string FileDeletedSuccessfully = "File deleted successfully: {0}";

        // Script-specific messages

        /// <summary>
        /// Error message when script name is required but not provided.
        /// </summary>
        public const string ScriptNameRequired = "Script name is required";

        /// <summary>
        /// Error message format when a script file cannot be found.
        /// Use with string.Format to include the script path.
        /// </summary>
        public const string ScriptFileNotFound = "Script file not found: {0}";

        /// <summary>
        /// Error message when edits array is required but missing or empty.
        /// </summary>
        public const string EditsArrayRequired = "Edits array is required and cannot be empty";

        /// <summary>
        /// Error message when validation fails after applying edits.
        /// </summary>
        public const string ValidationFailed = "Validation failed after applying edits";

        /// <summary>
        /// Success message format for structured edits.
        /// Use with string.Format to include edit count and file path.
        /// </summary>
        public const string StructuredEditsAppliedSuccessfully = "Successfully applied {0} structured edits to {1}";

        // Category-related messages

        /// <summary>
        /// Log message format for category filter updates.
        /// Use with string.Format to include the enabled categories list.
        /// </summary>
        public const string CategoryFiltersUpdated = "[CategoryFilteredToolRegistry] Updated enabled categories: {0}";

        // Validation messages

        /// <summary>
        /// Error message for unbalanced braces in code.
        /// </summary>
        public const string UnbalancedBraces = "Unbalanced braces detected";

        /// <summary>
        /// Error message format for validation failures.
        /// Use with string.Format to include the specific validation error.
        /// </summary>
        public const string ValidationError = "Validation error: {0}";

        // Common operation descriptions

        /// <summary>
        /// Description text for operation parameters.
        /// </summary>
        public const string OperationToPerform = "Operation to perform";

        /// <summary>
        /// Description text for asset path parameters.
        /// </summary>
        public const string AssetPathDescription = "Relative path under Assets/";

        /// <summary>
        /// Format string for optional parameter descriptions.
        /// Use with string.Format to include the parameter name.
        /// </summary>
        public const string OptionalDescription = "Optional {0}";

        // Default values

        /// <summary>
        /// Default path for script creation operations.
        /// </summary>
        public const string DefaultScriptsPath = "Assets/Scripts";

        /// <summary>
        /// Default base class for new script creation.
        /// </summary>
        public const string DefaultMonoBehaviour = "MonoBehaviour";

        /// <summary>
        /// Default action for apply operations.
        /// </summary>
        public const string DefaultApplyAction = "apply";

        /// <summary>
        /// Default action for validate operations.
        /// </summary>
        public const string DefaultValidateAction = "validate";

        /// <summary>
        /// Default validation level.
        /// </summary>
        public const string DefaultBasicLevel = "basic";
    }
}