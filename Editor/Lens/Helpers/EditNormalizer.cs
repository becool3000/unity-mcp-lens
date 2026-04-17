using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Becool.UnityMcpLens.Editor.Helpers
{
    /// <summary>
    /// Comprehensive edit normalization helper that processes and validates edit operations.
    /// Based on the normalization logic from manage_script_edits.py.
    /// </summary>
    static class EditNormalizer
    {
        // Operation type constants
        /// <summary>
        /// Set of structured operation types that manipulate code at the semantic level (classes, methods).
        /// </summary>
        public static readonly HashSet<string> StructuredOps = new(StringComparer.OrdinalIgnoreCase)
        {
            "replace_class",
            "delete_class",
            "replace_method",
            "delete_method",
            "insert_method"
        };

        /// <summary>
        /// Set of text operation types that manipulate code at the character/line level.
        /// </summary>
        public static readonly HashSet<string> TextOps = new(StringComparer.OrdinalIgnoreCase)
        {
            "prepend",
            "append",
            "replace_range",
            "regex_replace",
            "anchor_insert",
            "anchor_delete",
            "anchor_replace"
        };

        /// <summary>
        /// Normalize a list of raw edit operations, handling aliases and validation
        /// </summary>
        /// <param name="rawEdits">Raw edit operations from input</param>
        /// <param name="scriptName">Name of the script being edited</param>
        /// <returns>Tuple of (normalizedEdits, structuredError)</returns>
        public static (List<Dictionary<string, object>> normalizedEdits, object structuredError) NormalizeEdits(
            List<Dictionary<string, object>> rawEdits,
            string scriptName)
        {
            var normalizedEdits = new List<Dictionary<string, object>>();

            if (rawEdits == null)
            {
                return (normalizedEdits, null);
            }

            foreach (var rawEdit in rawEdits)
            {
                try
                {
                    var normalized = UnwrapAndAlias(rawEdit, scriptName);
                    var validationError = ValidateEdit(normalized, normalizedEdits);

                    if (validationError != null)
                    {
                        // Return structured error immediately (matches Python behavior)
                        return (normalizedEdits, validationError);
                    }

                    normalizedEdits.Add(normalized);
                }
                catch (Exception ex)
                {
                    // Return structured error for exceptions too
                    var exceptionError = CreateValidationError(
                        $"Failed to normalize edit: {ex.Message}",
                        new {error = "normalization_exception"},
                        new {suggestion = "Check edit format and required fields"},
                        normalizedEdits
                    );
                    return (normalizedEdits, exceptionError);
                }
            }

            return (normalizedEdits, null);
        }

        /// <summary>
        /// Unwrap single-key wrappers and handle field aliases
        /// </summary>
        static Dictionary<string, object> UnwrapAndAlias(Dictionary<string, object> edit, string scriptName)
        {
            // Unwrap single-key wrappers like {"replace_method": {...}}
            var wrapperKeys = new[] {"replace_method", "insert_method", "delete_method", "replace_class", "delete_class", "anchor_insert", "anchor_replace", "anchor_delete"};

            foreach (var wrapperKey in wrapperKeys)
            {
                if (edit.ContainsKey(wrapperKey) && edit[wrapperKey] is Dictionary<string, object> inner)
                {
                    var unwrapped = new Dictionary<string, object>(inner);
                    unwrapped["op"] = wrapperKey;
                    edit = unwrapped;
                    break;
                }
                else if (edit.ContainsKey(wrapperKey) && edit[wrapperKey] is JObject jInner)
                {
                    var unwrapped = new Dictionary<string, object>();
                    foreach (var property in jInner.Properties())
                    {
                        unwrapped[property.Name] = property.Value?.ToObject<object>();
                    }

                    unwrapped["op"] = wrapperKey;
                    edit = unwrapped;
                    break;
                }
            }

            var e = new Dictionary<string, object>(edit);

            // Normalize operation field
            var op = GetStringValue(e, "op") ??
                GetStringValue(e, "operation") ??
                GetStringValue(e, "type") ??
                GetStringValue(e, "mode") ?? "";

            if (!string.IsNullOrEmpty(op))
            {
                e["op"] = op.Trim().ToLowerInvariant();
            }

            // Handle field aliases
            HandleFieldAliases(e);

            // Default className to script name for structured operations
            var opLower = (e.GetValueOrDefault("op") as string ?? "").ToLowerInvariant();
            if (StructuredOps.Contains(opLower) && !e.ContainsKey("className"))
            {
                e["className"] = scriptName;
            }

            // Handle special cases and conversions
            HandleSpecialCases(e);

            return e;
        }

        /// <summary>
        /// Handle common field aliases
        /// </summary>
        static void HandleFieldAliases(Dictionary<string, object> e)
        {
            // Class name aliases
            if (e.ContainsKey("class_name") && !e.ContainsKey("className"))
            {
                e["className"] = e["class_name"];
                e.Remove("class_name");
            }

            if (e.ContainsKey("class") && !e.ContainsKey("className"))
            {
                e["className"] = e["class"];
                e.Remove("class");
            }

            // Method name aliases
            if (e.ContainsKey("method_name") && !e.ContainsKey("methodName"))
            {
                e["methodName"] = e["method_name"];
                e.Remove("method_name");
            }

            if (e.ContainsKey("target") && !e.ContainsKey("methodName"))
            {
                e["methodName"] = e["target"];
                e.Remove("target");
            }

            if (e.ContainsKey("method") && !e.ContainsKey("methodName"))
            {
                e["methodName"] = e["method"];
                e.Remove("method");
            }

            // Replacement text aliases
            if (e.ContainsKey("new_content") && !e.ContainsKey("replacement"))
            {
                e["replacement"] = e["new_content"];
                e.Remove("new_content");
            }

            if (e.ContainsKey("newMethod") && !e.ContainsKey("replacement"))
            {
                e["replacement"] = e["newMethod"];
                e.Remove("newMethod");
            }

            if (e.ContainsKey("new_method") && !e.ContainsKey("replacement"))
            {
                e["replacement"] = e["new_method"];
                e.Remove("new_method");
            }

            if (e.ContainsKey("content") && !e.ContainsKey("replacement"))
            {
                e["replacement"] = e["content"];
                e.Remove("content");
            }

            // Position aliases
            if (e.ContainsKey("after") && !e.ContainsKey("afterMethodName"))
            {
                e["afterMethodName"] = e["after"];
                e.Remove("after");
            }

            if (e.ContainsKey("after_method") && !e.ContainsKey("afterMethodName"))
            {
                e["afterMethodName"] = e["after_method"];
                e.Remove("after_method");
            }

            if (e.ContainsKey("before") && !e.ContainsKey("beforeMethodName"))
            {
                e["beforeMethodName"] = e["before"];
                e.Remove("before");
            }

            if (e.ContainsKey("before_method") && !e.ContainsKey("beforeMethodName"))
            {
                e["beforeMethodName"] = e["before_method"];
                e.Remove("before_method");
            }

            // Anchor aliases
            if (e.ContainsKey("anchorText") && !e.ContainsKey("anchor"))
            {
                e["anchor"] = e["anchorText"];
                e.Remove("anchorText");
            }

            if (e.ContainsKey("pattern") && !e.ContainsKey("anchor") &&
                GetStringValue(e, "op")?.StartsWith("anchor_") == true)
            {
                e["anchor"] = e["pattern"];
                e.Remove("pattern");
            }

            // Text aliases
            if (e.ContainsKey("newText") && !e.ContainsKey("text"))
            {
                e["text"] = e["newText"];
                e.Remove("newText");
            }

            if (e.ContainsKey("insert") && !e.ContainsKey("text"))
            {
                e["text"] = e["insert"];
                e.Remove("insert");
            }
        }

        /// <summary>
        /// Handle special conversion cases
        /// </summary>
        static void HandleSpecialCases(Dictionary<string, object> e)
        {
            var op = GetStringValue(e, "op");

            // Handle anchor_method → before/after based on position
            if (e.ContainsKey("anchor_method"))
            {
                var anchor = e["anchor_method"];
                var position = GetStringValue(e, "position") ?? "after";
                e.Remove("anchor_method");

                if (position.ToLowerInvariant() == "before" && !e.ContainsKey("beforeMethodName"))
                {
                    e["beforeMethodName"] = anchor;
                }
                else if (!e.ContainsKey("afterMethodName"))
                {
                    e["afterMethodName"] = anchor;
                }
            }

            // CI compatibility: Accept method-anchored anchor_insert and upgrade to insert_method
            if (op == "anchor_insert" &&
                !e.ContainsKey("anchor") &&
                (e.ContainsKey("afterMethodName") || e.ContainsKey("beforeMethodName")))
            {
                e["op"] = "insert_method";
                if (!e.ContainsKey("replacement"))
                {
                    e["replacement"] = GetStringValue(e, "text") ?? "";
                }
            }

            // LSP-like range edit → replace_range
            if (e.ContainsKey("range") && e["range"] is Dictionary<string, object> range)
            {
                var start = range.GetValueOrDefault("start") as Dictionary<string, object>;
                var end = range.GetValueOrDefault("end") as Dictionary<string, object>;

                if (start != null && end != null)
                {
                    e["op"] = "replace_range";
                    e["startLine"] = GetIntValue(start, "line") + 1; // Convert 0-based to 1-based
                    e["startCol"] = GetIntValue(start, "character") + 1;
                    e["endLine"] = GetIntValue(end, "line") + 1;
                    e["endCol"] = GetIntValue(end, "character") + 1;

                    if (!e.ContainsKey("text") && e.ContainsKey("newText"))
                    {
                        e["text"] = e["newText"];
                    }
                }

                e.Remove("range");
            }
            else if (e.ContainsKey("range") && e["range"] is JObject jRange)
            {
                var start = jRange["start"]?.ToObject<Dictionary<string, object>>();
                var end = jRange["end"]?.ToObject<Dictionary<string, object>>();

                if (start != null && end != null)
                {
                    e["op"] = "replace_range";
                    e["startLine"] = GetIntValue(start, "line") + 1;
                    e["startCol"] = GetIntValue(start, "character") + 1;
                    e["endLine"] = GetIntValue(end, "line") + 1;
                    e["endCol"] = GetIntValue(end, "character") + 1;

                    if (!e.ContainsKey("text") && e.ContainsKey("newText"))
                    {
                        e["text"] = e["newText"];
                    }
                }

                e.Remove("range");
            }

            // Handle operation aliases
            if (op == "text_replace")
            {
                e["op"] = "replace_range";
            }
            else if (op == "regex_delete")
            {
                e["op"] = "regex_replace";
                e["text"] = "";
            }
            else if (op == "regex_replace" && !e.ContainsKey("replacement"))
            {
                if (e.ContainsKey("text"))
                {
                    e["replacement"] = e["text"];
                }
                else if (e.ContainsKey("insert") || e.ContainsKey("content"))
                {
                    e["replacement"] = e.GetValueOrDefault("insert") ?? e.GetValueOrDefault("content") ?? "";
                }
            }

            // Convert anchor_insert with no text to anchor_delete
            if (op == "anchor_insert" &&
                string.IsNullOrEmpty(GetStringValue(e, "text")) &&
                string.IsNullOrEmpty(GetStringValue(e, "insert")) &&
                string.IsNullOrEmpty(GetStringValue(e, "content")) &&
                string.IsNullOrEmpty(GetStringValue(e, "replacement")))
            {
                e["op"] = "anchor_delete";
            }
        }

        /// <summary>
        /// Create a structured validation error
        /// </summary>
        static object CreateValidationError(string message, object expected, object rewrite,
            List<Dictionary<string, object>> normalized = null, string routing = null,
            Dictionary<string, object> extra = null)
        {
            var data = new Dictionary<string, object> {["expected"] = expected, ["rewrite_suggestion"] = rewrite};

            // Include normalized edits processed so far
            if (normalized != null)
            {
                data["normalizedEdits"] = normalized;
            }

            // Include routing information
            if (!string.IsNullOrEmpty(routing))
            {
                data["routing"] = routing;
            }

            // Include extra fields
            if (extra != null)
            {
                foreach (var kvp in extra)
                {
                    data[kvp.Key] = kvp.Value;
                }
            }

            return new {success = false, code = "missing_field", message = message, data = data};
        }

        /// <summary>
        /// Validate an edit operation for required fields
        /// </summary>
        static object ValidateEdit(Dictionary<string, object> edit, List<Dictionary<string, object>> normalized = null)
        {
            var op = GetStringValue(edit, "op");
            if (string.IsNullOrEmpty(op))
            {
                return CreateValidationError("Edit operation requires 'op' field",
                    new {required = new[] {"op"}},
                    new {op = "replace_method"},
                    normalized);
            }

            switch (op.ToLowerInvariant())
            {
                case "replace_method":
                    if (string.IsNullOrEmpty(GetStringValue(edit, "methodName")))
                        return CreateValidationError("replace_method requires 'methodName'.",
                            new {op = "replace_method", required = new[] {"className", "methodName", "replacement"}},
                            new {methodName = "HasTarget"},
                            normalized);
                    if (string.IsNullOrEmpty(GetStringValue(edit, "replacement")))
                        return CreateValidationError("replace_method requires 'replacement'.",
                            new {op = "replace_method", required = new[] {"className", "methodName", "replacement"}},
                            new {replacement = "public bool HasTarget(){ return currentTarget!=null; }"},
                            normalized);
                    break;

                case "insert_method":
                    if (string.IsNullOrEmpty(GetStringValue(edit, "replacement")) &&
                        string.IsNullOrEmpty(GetStringValue(edit, "text")))
                        return CreateValidationError("insert_method requires 'replacement' or 'text'.",
                            new {op = "insert_method", required = new[] {"className", "replacement"}},
                            new {replacement = "public void PrintSeries(){ Debug.Log(\"series\"); }"},
                            normalized);

                    var position = GetStringValue(edit, "position")?.ToLowerInvariant();
                    if (position == "after" && string.IsNullOrEmpty(GetStringValue(edit, "afterMethodName")))
                        return CreateValidationError("insert_method with position='after' requires 'afterMethodName'.",
                            new {op = "insert_method", position = new {after_requires = "afterMethodName"}},
                            new {afterMethodName = "GetCurrentTarget"},
                            normalized);
                    if (position == "before" && string.IsNullOrEmpty(GetStringValue(edit, "beforeMethodName")))
                        return CreateValidationError("insert_method with position='before' requires 'beforeMethodName'.",
                            new {op = "insert_method", position = new {before_requires = "beforeMethodName"}},
                            new {beforeMethodName = "GetCurrentTarget"},
                            normalized);
                    break;

                case "delete_method":
                    if (string.IsNullOrEmpty(GetStringValue(edit, "methodName")))
                        return CreateValidationError("delete_method requires 'methodName'.",
                            new {op = "delete_method", required = new[] {"className", "methodName"}},
                            new {methodName = "PrintSeries"},
                            normalized);
                    break;

                case "anchor_insert":
                case "anchor_replace":
                case "anchor_delete":
                    if (string.IsNullOrEmpty(GetStringValue(edit, "anchor")))
                        return CreateValidationError($"{op} requires 'anchor' (regex).",
                            new {op = op, required = new[] {"anchor"}},
                            new {anchor = "(?m)^\\s*public\\s+bool\\s+HasTarget\\s*\\("},
                            normalized);
                    if ((op == "anchor_insert" || op == "anchor_replace") &&
                        string.IsNullOrEmpty(GetStringValue(edit, "text")) &&
                        string.IsNullOrEmpty(GetStringValue(edit, "replacement")))
                        return CreateValidationError($"{op} requires 'text'.",
                            new {op = op, required = new[] {"anchor", "text"}},
                            new {text = "/* comment */\n"},
                            normalized);
                    break;

                case "replace_range":
                    if (!edit.ContainsKey("startLine") || !edit.ContainsKey("startCol") ||
                        !edit.ContainsKey("endLine") || !edit.ContainsKey("endCol"))
                        return CreateValidationError("replace_range requires startLine, startCol, endLine, endCol.",
                            new {op = "replace_range", required = new[] {"startLine", "startCol", "endLine", "endCol"}},
                            new {startLine = 1, startCol = 1, endLine = 1, endCol = 10},
                            normalized);
                    break;

                case "regex_replace":
                    if (string.IsNullOrEmpty(GetStringValue(edit, "pattern")) &&
                        string.IsNullOrEmpty(GetStringValue(edit, "anchor")))
                        return CreateValidationError("regex_replace requires 'pattern'.",
                            new {op = "regex_replace", required = new[] {"pattern", "replacement"}},
                            new {pattern = "\\b\\w+\\b", replacement = "newValue"},
                            normalized);
                    break;
            }

            return null; // No validation errors
        }

        /// <summary>
        /// Determine the routing type for a set of operations
        /// </summary>
        /// <param name="edits">Collection of edit operations to analyze.</param>
        /// <returns>Returns "structured" if all operations are structured, "text" if all are text-based, or "mixed" if both types are present.</returns>
        public static string DetermineRouting(IEnumerable<Dictionary<string, object>> edits)
        {
            var ops = edits.Select(e => GetStringValue(e, "op")?.ToLowerInvariant() ?? "")
                .Where(op => !string.IsNullOrEmpty(op))
                .ToHashSet();

            bool allStructured = ops.All(op => StructuredOps.Contains(op));
            bool allText = ops.All(op => TextOps.Contains(op));

            if (allStructured)
                return "structured";
            else if (allText)
                return "text";
            else
                return "mixed";
        }

        /// <summary>
        /// Get string value from dictionary, handling various input types
        /// </summary>
        static string GetStringValue(Dictionary<string, object> dict, string key)
        {
            if (!dict.TryGetValue(key, out var value))
                return null;

            return value?.ToString();
        }

        /// <summary>
        /// Get integer value from dictionary, handling various input types
        /// </summary>
        static int GetIntValue(Dictionary<string, object> dict, string key)
        {
            if (!dict.TryGetValue(key, out var value))
                return 0;

            if (value is int intValue)
                return intValue;

            if (int.TryParse(value?.ToString(), out int parsed))
                return parsed;

            return 0;
        }

        /// <summary>
        /// Validate that all required tools are available for the given operations
        /// </summary>
        /// <param name="edits">Collection of edit operations to validate.</param>
        /// <returns>A list of error messages for any unsupported operations; empty if all operations are supported.</returns>
        public static List<string> ValidateToolAvailability(IEnumerable<Dictionary<string, object>> edits)
        {
            var errors = new List<string>();
            var ops = edits.Select(e => GetStringValue(e, "op")?.ToLowerInvariant()).ToHashSet();

            // Check for unsupported operations (this can be extended as needed)
            var unsupportedOps = ops.Where(op => !StructuredOps.Contains(op) && !TextOps.Contains(op));

            foreach (var unsupportedOp in unsupportedOps)
            {
                errors.Add($"Unsupported operation: {unsupportedOp}");
            }

            return errors;
        }
    }
}
