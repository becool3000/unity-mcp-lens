using System;
using System.Collections.Generic;
using Becool.UnityMcpLens.Editor.ToolRegistry;

namespace Becool.UnityMcpLens.Editor.Tools.Parameters
{
    /// <summary>
    /// Parameters for the Unity.ScriptApplyEdits tool that provides structured C# script editing
    /// with safer boundaries and comprehensive validation.
    /// </summary>
    [Serializable]
    public class ScriptApplyEditsParams
    {
        /// <summary>
        /// Name of the script to edit (without .cs extension)
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Path to the script under Assets/ directory
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// List of edits to apply to the script. Each edit should contain:
        /// - op: Operation type (replace_method, insert_method, delete_method, anchor_insert, etc.)
        /// - Additional fields based on operation type
        /// </summary>
        public List<Dictionary<string, object>> Edits { get; set; }

        /// <summary>
        /// Options for the script edit operation
        /// </summary>
        public Dictionary<string, object> Options { get; set; }

        /// <summary>
        /// Type of the script (e.g., MonoBehaviour, ScriptableObject)
        /// </summary>
        public string ScriptType { get; set; } = "MonoBehaviour";

        /// <summary>
        /// Namespace of the script
        /// </summary>
        public string Namespace { get; set; }

        /// <summary>
        /// Optional precondition SHA256 to prevent concurrent edits
        /// </summary>
        public string PreconditionSha256 { get; set; }

        /// <summary>
        /// Whether this is a preview/dry-run operation
        /// </summary>
        public bool Preview { get; set; }

        /// <summary>
        /// Initializes a new instance of the ScriptApplyEditsParams class.
        /// </summary>
        public ScriptApplyEditsParams()
        {
            Edits = new List<Dictionary<string, object>>();
            Options = new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// Represents a single edit operation within ScriptApplyEditsParams
    /// </summary>
    [Serializable]
    public class EditOperation
    {
        /// <summary>
        /// The operation type: replace_method, insert_method, delete_method,
        /// anchor_insert, anchor_delete, anchor_replace, prepend, append, replace_range, regex_replace
        /// </summary>
        public string Op { get; set; }

        /// <summary>
        /// Class name for method/class operations
        /// </summary>
        public string ClassName { get; set; }

        /// <summary>
        /// Method name for method operations
        /// </summary>
        public string MethodName { get; set; }

        /// <summary>
        /// Replacement text for replace operations
        /// </summary>
        public string Replacement { get; set; }

        /// <summary>
        /// Position for insert operations: start, end, after, before
        /// </summary>
        public string Position { get; set; }

        /// <summary>
        /// Method name to insert after (when position = "after")
        /// </summary>
        public string AfterMethodName { get; set; }

        /// <summary>
        /// Method name to insert before (when position = "before")
        /// </summary>
        public string BeforeMethodName { get; set; }

        /// <summary>
        /// Regex pattern for anchor operations
        /// </summary>
        public string Anchor { get; set; }

        /// <summary>
        /// Text to insert/replace for anchor operations
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Whether to ignore case in pattern matching
        /// </summary>
        public bool IgnoreCase { get; set; }

        /// <summary>
        /// Whether to prefer the last match over the first (for anchor operations)
        /// </summary>
        public bool PreferLast { get; set; } = true;

        /// <summary>
        /// Whether to allow no-op if anchor is not found
        /// </summary>
        public bool AllowNoop { get; set; } = true;

        /// <summary>
        /// Starting line number for replace_range operations
        /// </summary>
        public int StartLine { get; set; }

        /// <summary>
        /// Starting column number for replace_range operations
        /// </summary>
        public int StartCol { get; set; }

        /// <summary>
        /// Ending line number for replace_range operations
        /// </summary>
        public int EndLine { get; set; }

        /// <summary>
        /// Ending column number for replace_range operations
        /// </summary>
        public int EndCol { get; set; }

        /// <summary>
        /// Pattern for regex operations
        /// </summary>
        public string Pattern { get; set; }

        /// <summary>
        /// Count for regex replace (0 = replace all)
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// Return type filter for method operations
        /// </summary>
        public string ReturnType { get; set; }

        /// <summary>
        /// Parameters signature filter for method operations
        /// </summary>
        public string ParametersSignature { get; set; }

        /// <summary>
        /// Attributes filter for method operations
        /// </summary>
        public string AttributesContains { get; set; }

        /// <summary>
        /// Namespace for class operations
        /// </summary>
        public string Namespace { get; set; }
    }
}
