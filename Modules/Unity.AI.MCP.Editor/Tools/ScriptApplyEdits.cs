using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry;
using Unity.AI.MCP.Editor.Tools.Parameters;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Unity.AI.MCP.Editor.Tools
{
    /// <summary>
    /// Structured C# script editing with safer boundaries and comprehensive validation.
    /// This tool provides advanced script editing capabilities including method/class operations
    /// and anchor-based pattern matching with improved heuristics.
    /// </summary>
    public static class ScriptApplyEdits
    {
        /// <summary>
        /// Description of the ScriptApplyEdits tool functionality and parameters.
        /// </summary>
        public const string Title = "Apply structured C# edits";

        public const string Description = @"Structured C# edits (methods/classes) with safer boundaries - prefer this over raw text.

Best practices:
- Prefer anchor_* ops for pattern-based insert/replace near stable markers
- Use replace_method/delete_method for whole-method changes (keeps signatures balanced)
- Avoid whole-file regex deletes; validators will guard unbalanced braces
- For tail insertions, prefer anchor/regex_replace on final brace (class closing)
- Pass options.validate='standard' for structural checks; 'relaxed' for interior-only edits

Canonical fields (use these exact keys):
- op: replace_method | insert_method | delete_method | anchor_insert | anchor_delete | anchor_replace
- className: string (defaults to 'name' if omitted on method/class ops)
- methodName: string (required for replace_method, delete_method)
- replacement: string (required for replace_method, insert_method)
- position: start | end | after | before (insert_method only)
- afterMethodName / beforeMethodName: string (required when position='after'/'before')
- anchor: regex string (for anchor_* ops)
- text: string (for anchor_insert/anchor_replace)

Examples:
1) Replace a method:
{
  ""name"": ""SmartReach"",
  ""path"": ""Assets/Scripts/Interaction"",
  ""edits"": [{
    ""op"": ""replace_method"",
    ""className"": ""SmartReach"",
    ""methodName"": ""HasTarget"",
    ""replacement"": ""public bool HasTarget(){ return currentTarget!=null; }""
  }],
  ""options"": {""validate"": ""standard"", ""refresh"": ""immediate""}
}

2) Insert a method after another:
{
  ""name"": ""SmartReach"",
  ""path"": ""Assets/Scripts/Interaction"",
  ""edits"": [{
    ""op"": ""insert_method"",
    ""className"": ""SmartReach"",
    ""replacement"": ""public void PrintSeries(){ Debug.Log(seriesName); }"",
    ""position"": ""after"",
    ""afterMethodName"": ""GetCurrentTarget""
  }]
}";

        /// <summary>
        /// Returns the output schema for this tool.
        /// </summary>
        /// <returns>The JSON schema object describing the tool's output structure.</returns>
        [McpOutputSchema("Unity.ScriptApplyEdits")]
        public static object GetOutputSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    success = new { type = "boolean", description = "Whether the operation succeeded" },
                    message = new { type = "string", description = "Human-readable message about the operation" },
                    data = new
                    {
                        type = "object",
                        description = "Script edit results",
                        properties = new
                        {
                            uri = new { type = "string", description = "Unity URI of the edited script" },
                            path = new { type = "string", description = "Relative path of the edited script" },
                            editsApplied = new { type = "integer", description = "Number of edits applied" },
                            sha256 = new { type = "string", description = "SHA256 hash of the modified script" },
                            scheduledRefresh = new { type = "boolean", description = "Whether a refresh was scheduled" },
                            no_op = new { type = "boolean", description = "Whether this was a no-op (no changes made)" },
                            normalizedEdits = new { type = "array", description = "Normalized edit operations that were applied" },
                            routing = new { type = "string", description = "Edit routing method used (structured/text/mixed)" },
                            warnings = new { type = "array", description = "Any warnings generated during processing" }
                        }
                    }
                },
                required = new[] { "success", "message" }
            };
        }

        /// <summary>
        /// Main handler for structured script edits.
        /// </summary>
        /// <param name="parameters">The parameters specifying the script edits to apply.</param>
        /// <returns>A response object containing success status, message, and optional data.</returns>
        [McpTool("Unity.ScriptApplyEdits", Description, Title, Groups = new string[] { "core", "scripting" })]
        public static object HandleCommand(ScriptApplyEditsParams parameters)
        {
            if (parameters == null)
            {
                return Response.Error("Parameters cannot be null.");
            }

            string name = parameters.Name?.Trim();
            string path = parameters.Path?.Trim();

            if (string.IsNullOrEmpty(name))
            {
                return Response.Error("Name parameter is required.");
            }

            // Normalize script locator
            var (normalizedName, normalizedPath) = NormalizeScriptLocator(name, path);

            // Validate script name
            if (!Regex.IsMatch(normalizedName, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
            {
                return Response.Error($"Invalid script name: '{normalizedName}'. Use only letters, numbers, underscores, and don't start with a number.");
            }

            try
            {
                // Normalize edits and handle aliases
                var (normalizedEdits, structuredError) = EditNormalizer.NormalizeEdits(
                    parameters.Edits ?? new List<Dictionary<string, object>>(),
                    normalizedName);

                if (structuredError != null)
                {
                    return structuredError;
                }

                // Validate tool availability
                var availabilityErrors = EditNormalizer.ValidateToolAvailability(normalizedEdits);
                if (availabilityErrors.Any())
                {
                    return Response.Error($"Unsupported operations: {string.Join(", ", availabilityErrors)}", new
                    {
                        normalizedEdits = normalizedEdits,
                        errors = availabilityErrors
                    });
                }

                // Determine routing strategy
                string routing = EditNormalizer.DetermineRouting(normalizedEdits);

                // Add top-level Preview parameter to options if not already present
                var optionsToUse = parameters.Options ?? new Dictionary<string, object>();
                if (parameters.Preview && !optionsToUse.ContainsKey("preview"))
                {
                    optionsToUse = new Dictionary<string, object>(optionsToUse);
                    optionsToUse["preview"] = true;
                }

                // Execute edits based on routing
                switch (routing)
                {
                    case "structured":
                        return ExecuteStructuredEdits(normalizedName, normalizedPath, normalizedEdits, optionsToUse, parameters.ScriptType, parameters.Namespace);

                    case "text":
                        return ExecuteTextEdits(normalizedName, normalizedPath, normalizedEdits, optionsToUse, parameters.ScriptType, parameters.Namespace);

                    case "mixed":
                        return ExecuteMixedEdits(normalizedName, normalizedPath, normalizedEdits, optionsToUse, parameters.ScriptType, parameters.Namespace);

                    default:
                        return Response.Error($"Unknown routing strategy: {routing}", new
                        {
                            normalizedEdits = normalizedEdits,
                            routing = routing
                        });
                }
            }
            catch (Exception ex)
            {
                return Response.Error($"Script edit failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Execute purely structured edits (method/class operations)
        /// </summary>
        static object ExecuteStructuredEdits(string name, string path, List<Dictionary<string, object>> edits,
            Dictionary<string, object> options, string scriptType, string namespaceName)
        {
            var opts = new Dictionary<string, object>(options ?? new Dictionary<string, object>());
            opts.TryAdd("refresh", "immediate"); // Prefer immediate refresh for structured edits

            var managementParams = new JObject
            {
                ["action"] = "edit",
                ["name"] = name,
                ["path"] = path,
                ["namespace"] = namespaceName,
                ["scriptType"] = scriptType,
                ["edits"] = JArray.FromObject(edits),
                ["options"] = JObject.FromObject(opts)
            };

            var result = ManageScript.HandleCommand(managementParams);

            // Enhance result with routing information
            if (result is object resultObj)
            {
                var resultDict = GetObjectProperties(resultObj);
                var data = resultDict.GetValueOrDefault("data") as Dictionary<string, object> ?? new Dictionary<string, object>();
                data["normalizedEdits"] = edits;
                data["routing"] = "structured";
                resultDict["data"] = data;
            }

            return result;
        }

        /// <summary>
        /// Execute text-based edits (anchor operations, regex, ranges)
        /// </summary>
        static object ExecuteTextEdits(string name, string path, List<Dictionary<string, object>> edits,
            Dictionary<string, object> options, string scriptType, string namespaceName)
        {
            // First read the current script content
            var readParams = new JObject
            {
                ["action"] = "read",
                ["name"] = name,
                ["path"] = path,
                ["namespace"] = namespaceName,
                ["scriptType"] = scriptType
            };

            var readResult = ManageScript.HandleCommand(readParams);
            if (!IsSuccessResponse(readResult))
            {
                return readResult;
            }

            string contents = ExtractContentsFromReadResult(readResult);
            if (contents == null)
            {
                return Response.Error("Failed to read script contents for text editing.");
            }

            // Try to convert and apply text edits directly
            var result = ConvertAndApplyTextEdits(name, path, namespaceName, scriptType, edits, contents, options);
            if (result != null)
            {
                return result;
            }

            // Handle preview logic for regex_replace
            bool preview = GetBoolValue(options, "preview");
            bool confirm = GetBoolValue(options, "confirm", false);
            var textOps = edits.Select(e => GetStringValue(e, "op")?.ToLowerInvariant() ?? "").ToHashSet();
            var hasRegexReplace = textOps.Contains("regex_replace");

            if (hasRegexReplace && (preview || !confirm))
            {
                try
                {
                    // Apply edits locally to generate preview
                    string previewText = ApplyEditsLocally(contents, edits);
                    string diff = GenerateUnifiedDiff(contents, previewText);

                    if (preview)
                    {
                        return Response.Success("Preview only (no write)", new
                        {
                            diff = diff,
                            normalizedEdits = edits,
                            routing = "text"
                        });
                    }

                    // For regex_replace without confirm, show preview and require confirmation
                    return Response.Error("Preview diff; set options.confirm=true to apply.", new
                    {
                        diff = diff,
                        normalizedEdits = edits,
                        routing = "text"
                    });
                }
                catch (Exception ex)
                {
                    return Response.Error($"Preview failed: {ex.Message}", new
                    {
                        normalizedEdits = edits,
                        routing = "text"
                    });
                }
            }

            // Apply edits locally
            string newContents;
            try
            {
                newContents = ApplyEditsLocally(contents, edits);
            }
            catch (Exception ex)
            {
                return Response.Error($"Edit application failed: {ex.Message}");
            }

            // Short-circuit no-op edits
            if (newContents == contents)
            {
                return Response.Success("No-op: contents unchanged", new
                {
                    no_op = true,
                    evidence = new { reason = "identical_content" },
                    normalizedEdits = edits,
                    routing = "text"
                });
            }

            // Handle general preview mode
            if (preview)
            {
                try
                {
                    string diff = GenerateUnifiedDiff(contents, newContents);
                    return Response.Success("Preview only (no write)", new
                    {
                        diff = diff,
                        normalizedEdits = edits,
                        routing = "text"
                    });
                }
                catch (Exception ex)
                {
                    return Response.Error($"Preview diff generation failed: {ex.Message}", new
                    {
                        normalizedEdits = edits,
                        routing = "text"
                    });
                }
            }

            // Fallback: send as whole-file replacement
            try
            {

                var lines = contents.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                int endLine = lines.Length + 1; // 1-based exclusive end
                string sha = ComputeSha256(contents);

                var fallbackParams = new JObject
                {
                    ["action"] = "apply_text_edits",
                    ["name"] = name,
                    ["path"] = path,
                    ["namespace"] = namespaceName,
                    ["scriptType"] = scriptType,
                    ["edits"] = new JArray
                    {
                        new JObject
                        {
                            ["startLine"] = 1,
                            ["startCol"] = 1,
                            ["endLine"] = endLine,
                            ["endCol"] = 1,
                            ["newText"] = newContents
                        }
                    },
                    ["precondition_sha256"] = sha,
                    ["options"] = JObject.FromObject(new Dictionary<string, object>
                    {
                        ["validate"] = GetStringValue(options, "validate") ?? "standard",
                        ["refresh"] = GetStringValue(options, "refresh") ?? "debounced"
                    })
                };

                var fallbackResult = ManageScript.HandleCommand(fallbackParams);

                // Enhance result with routing information
                if (fallbackResult is object fallbackResultObj)
                {
                    var resultDict = GetObjectProperties(fallbackResultObj);
                    var data = resultDict.GetValueOrDefault("data") as Dictionary<string, object> ?? new Dictionary<string, object>();
                    data["normalizedEdits"] = edits;
                    data["routing"] = "text/fallback";
                    resultDict["data"] = data;
                }

                return fallbackResult;
            }
            catch (Exception ex)
            {
                return Response.Error($"Fallback edit application failed: {ex.Message}", new
                {
                    normalizedEdits = edits,
                    routing = "text/fallback"
                });
            }
        }

        /// <summary>
        /// Execute mixed edits (combination of structured and text operations)
        /// </summary>
        static object ExecuteMixedEdits(string name, string path, List<Dictionary<string, object>> edits,
            Dictionary<string, object> options, string scriptType, string namespaceName)
        {
            // First read the current script content
            var readParams = new JObject
            {
                ["action"] = "read",
                ["name"] = name,
                ["path"] = path,
                ["namespace"] = namespaceName,
                ["scriptType"] = scriptType
            };

            var readResult = ManageScript.HandleCommand(readParams);
            if (!IsSuccessResponse(readResult))
            {
                return readResult;
            }

            string contents = ExtractContentsFromReadResult(readResult);
            if (contents == null)
            {
                return Response.Error("Failed to read script contents for mixed editing.");
            }

            // Separate text and structured operations
            var TEXT = EditNormalizer.TextOps;
            var STRUCT = EditNormalizer.StructuredOps;
            var textEdits = edits.Where(e => TEXT.Contains(GetStringValue(e, "op")?.ToLowerInvariant() ?? "")).ToList();
            var structEdits = edits.Where(e => STRUCT.Contains(GetStringValue(e, "op")?.ToLowerInvariant() ?? "")).ToList();

            try
            {
                var baseText = contents;

                // Process text edits with inline conversion
                if (textEdits.Any())
                {
                    var atEdits = new List<Dictionary<string, object>>();

                    foreach (var edit in textEdits)
                    {
                        var opx = GetStringValue(edit, "op") ??
                                 GetStringValue(edit, "operation") ??
                                 GetStringValue(edit, "type") ??
                                 GetStringValue(edit, "mode") ?? "";
                        opx = opx.Trim().ToLowerInvariant();

                        var textField = GetStringValue(edit, "text") ??
                                       GetStringValue(edit, "insert") ??
                                       GetStringValue(edit, "content") ??
                                       GetStringValue(edit, "replacement") ?? "";

                        switch (opx)
                        {
                            case "anchor_insert":
                                var anchorEdit = ProcessMixedAnchorInsert(edit, baseText);
                                if (anchorEdit != null) atEdits.Add(anchorEdit);
                                break;

                            case "replace_range":
                                var rangeEdit = ProcessMixedReplaceRange(edit, textField);
                                if (rangeEdit != null) atEdits.Add(rangeEdit);
                                break;

                            case "regex_replace":
                                // NOTE: NO confirmation logic here
                                var regexEdit = ProcessMixedRegexReplace(edit, baseText, textField);
                                if (regexEdit != null) atEdits.Add(regexEdit);
                                break;

                            case "prepend":
                                atEdits.Add(new Dictionary<string, object>
                                {
                                    ["startLine"] = 1,
                                    ["startCol"] = 1,
                                    ["endLine"] = 1,
                                    ["endCol"] = 1,
                                    ["newText"] = textField
                                });
                                break;

                            case "append":
                                var eofIdx = baseText.Length;
                                var (sl, sc) = GetLineColFromIndex(baseText, eofIdx);
                                var newText = (!baseText.EndsWith("\n") ? "\n" : "") + textField;
                                atEdits.Add(new Dictionary<string, object>
                                {
                                    ["startLine"] = sl,
                                    ["startCol"] = sc,
                                    ["endLine"] = sl,
                                    ["endCol"] = sc,
                                    ["newText"] = newText
                                });
                                break;

                            default:
                                return Response.Error($"Unsupported text edit op: {opx}", new
                                {
                                    normalizedEdits = edits,
                                    routing = "mixed/text-first"
                                });
                        }
                    }

                    // Send text edits to Unity
                    if (atEdits.Any())
                    {
                        string sha = ComputeSha256(baseText);
                        var paramsText = new JObject
                        {
                            ["action"] = "apply_text_edits",
                            ["name"] = name,
                            ["path"] = path,
                            ["namespace"] = namespaceName,
                            ["scriptType"] = scriptType,
                            ["edits"] = JArray.FromObject(atEdits),
                            ["precondition_sha256"] = sha,
                            ["options"] = JObject.FromObject(new Dictionary<string, object>
                            {
                                ["refresh"] = GetStringValue(options, "refresh") ?? "debounced",
                                ["validate"] = GetStringValue(options, "validate") ?? "standard",
                                ["applyMode"] = atEdits.Count > 1 ? "atomic" : GetStringValue(options, "applyMode") ?? "sequential"
                            })
                        };

                        var respText = ManageScript.HandleCommand(paramsText);
                        if (!IsSuccessResponse(respText))
                        {
                            return Response.Error("Text edit failed in mixed processing", new
                            {
                                normalizedEdits = edits,
                                routing = "mixed/text-first"
                            });
                        }
                    }
                }

                // Then execute structured edits
                if (structEdits.Any())
                {
                    var opts = new Dictionary<string, object>(options ?? new Dictionary<string, object>());
                    opts.TryAdd("refresh", "debounced"); // Use debounced for mixed operations

                    var managementParams = new JObject
                    {
                        ["action"] = "edit",
                        ["name"] = name,
                        ["path"] = path,
                        ["namespace"] = namespaceName,
                        ["scriptType"] = scriptType,
                        ["edits"] = JArray.FromObject(structEdits),
                        ["options"] = JObject.FromObject(opts)
                    };

                    var structResult = ManageScript.HandleCommand(managementParams);
                    if (!IsSuccessResponse(structResult))
                    {
                        return structResult;
                    }

                    // Enhance result with mixed routing info
                    if (structResult is object resultObj)
                    {
                        var resultDict = GetObjectProperties(resultObj);
                        var data = resultDict.GetValueOrDefault("data") as Dictionary<string, object> ?? new Dictionary<string, object>();
                        data["normalizedEdits"] = edits;
                        data["routing"] = "mixed/text-first";
                        resultDict["data"] = data;
                    }

                    return structResult;
                }

                return Response.Success("Applied text edits (no structured ops)", new
                {
                    normalizedEdits = edits,
                    routing = "mixed/text-first"
                });
            }
            catch (Exception ex)
            {
                return Response.Error($"Text edit conversion failed: {ex.Message}", new
                {
                    normalizedEdits = edits,
                    routing = "mixed/text-first"
                });
            }
        }

        /// <summary>
        /// Convert structured operations to apply_text_edits format
        /// </summary>
        static object ConvertAndApplyTextEdits(string name, string path, string namespaceName, string scriptType,
            List<Dictionary<string, object>> edits, string contents, Dictionary<string, object> options)
        {
            try
            {
                var atEdits = new List<Dictionary<string, object>>();

                foreach (var edit in edits)
                {
                    var op = GetStringValue(edit, "op")?.ToLowerInvariant();
                    var textField = GetStringValue(edit, "text") ??
                                   GetStringValue(edit, "insert") ??
                                   GetStringValue(edit, "content") ?? "";

                    switch (op)
                    {
                        case "anchor_insert":
                            var anchorEdit = ProcessAnchorInsert(edit, contents);
                            if (anchorEdit != null) atEdits.Add(anchorEdit);
                            break;

                        case "anchor_delete":
                            var anchorDeleteEdit = ProcessAnchorDelete(edit, contents);
                            if (anchorDeleteEdit != null) atEdits.Add(anchorDeleteEdit);
                            break;

                        case "anchor_replace":
                            var anchorReplaceEdit = ProcessAnchorReplace(edit, contents);
                            if (anchorReplaceEdit != null) atEdits.Add(anchorReplaceEdit);
                            break;

                        case "replace_range":
                            var rangeEdit = ProcessReplaceRange(edit);
                            if (rangeEdit != null) atEdits.Add(rangeEdit);
                            break;

                        case "regex_replace":
                            var regexEdit = ProcessRegexReplace(edit, contents);
                            if (regexEdit != null) atEdits.Add(regexEdit);
                            break;

                        case "prepend":
                            atEdits.Add(new Dictionary<string, object>
                            {
                                ["startLine"] = 1,
                                ["startCol"] = 1,
                                ["endLine"] = 1,
                                ["endCol"] = 1,
                                ["newText"] = textField
                            });
                            break;

                        case "append":
                            var (line, col) = GetEndOfFilePosition(contents);
                            var newText = (!contents.EndsWith("\n") ? "\n" : "") + textField;
                            atEdits.Add(new Dictionary<string, object>
                            {
                                ["startLine"] = line,
                                ["startCol"] = col,
                                ["endLine"] = line,
                                ["endCol"] = col,
                                ["newText"] = newText
                            });
                            break;

                        default:
                            // Unsupported operation, return null to trigger fallback
                            return null;
                    }
                }

                if (!atEdits.Any())
                {
                    return Response.Error("No applicable text edit spans computed (anchor not found or zero-length).", new
                    {
                        routing = "text"
                    });
                }

                string sha = ComputeSha256(contents);
                var managementParams = new JObject
                {
                    ["action"] = "apply_text_edits",
                    ["name"] = name,
                    ["path"] = path,
                    ["namespace"] = namespaceName,
                    ["scriptType"] = scriptType,
                    ["edits"] = JArray.FromObject(atEdits),
                    ["precondition_sha256"] = sha,
                    ["options"] = JObject.FromObject(new Dictionary<string, object>
                    {
                        ["refresh"] = GetStringValue(options, "refresh") ?? "debounced",
                        ["validate"] = GetStringValue(options, "validate") ?? "standard",
                        ["applyMode"] = atEdits.Count > 1 ? "atomic" : GetStringValue(options, "applyMode") ?? "sequential"
                    })
                };

                var result = ManageScript.HandleCommand(managementParams);

                // Enhance result with routing information
                if (result is object resultObj)
                {
                    var resultDict = GetObjectProperties(resultObj);
                    var data = resultDict.GetValueOrDefault("data") as Dictionary<string, object> ?? new Dictionary<string, object>();
                    data["normalizedEdits"] = edits;
                    data["routing"] = "text";
                    resultDict["data"] = data;
                }

                return result;
            }
            catch (Exception ex)
            {
                return Response.Error($"Text edit conversion and application failed: {ex.Message}", new
                {
                    routing = "text"
                });
            }
        }

        /// <summary>
        /// Process anchor_insert operation
        /// </summary>
        static Dictionary<string, object> ProcessAnchorInsert(Dictionary<string, object> edit, string contents)
        {
            var anchor = GetStringValue(edit, "anchor");
            var position = GetStringValue(edit, "position")?.ToLowerInvariant() ?? "before";
            var text = GetStringValue(edit, "text") ?? GetStringValue(edit, "replacement") ?? "";
            var ignoreCase = GetBoolValue(edit, "ignoreCase");
            var preferLast = GetBoolValue(edit, "preferLast", true);

            if (string.IsNullOrEmpty(anchor))
                return null;

            var options = RegexOptions.Multiline;
            if (ignoreCase)
                options |= RegexOptions.IgnoreCase;

            var match = AnchorMatcher.FindBestAnchorMatch(anchor, contents, options, preferLast);
            if (match == null)
            {
                if (GetBoolValue(edit, "allowNoop", true))
                    return null;
                throw new InvalidOperationException($"Anchor not found: {anchor}");
            }

            int index = position == "before" ? match.Index : match.Index + match.Length;
            var (line, col) = GetLineColFromIndex(contents, index);

            // Normalize text with newlines
            if (!string.IsNullOrEmpty(text))
            {
                if (!text.StartsWith("\n"))
                    text = "\n" + text;
                if (!text.EndsWith("\n"))
                    text = text + "\n";
            }

            return new Dictionary<string, object>
            {
                ["startLine"] = line,
                ["startCol"] = col,
                ["endLine"] = line,
                ["endCol"] = col,
                ["newText"] = text
            };
        }

        /// <summary>
        /// Process anchor_delete operation
        /// </summary>
        static Dictionary<string, object> ProcessAnchorDelete(Dictionary<string, object> edit, string contents)
        {
            var anchor = GetStringValue(edit, "anchor");
            var ignoreCase = GetBoolValue(edit, "ignoreCase");
            var preferLast = GetBoolValue(edit, "preferLast", true);

            if (string.IsNullOrEmpty(anchor))
                return null;

            var options = RegexOptions.Multiline;
            if (ignoreCase)
                options |= RegexOptions.IgnoreCase;

            var match = AnchorMatcher.FindBestAnchorMatch(anchor, contents, options, preferLast);
            if (match == null)
            {
                if (GetBoolValue(edit, "allowNoop", true))
                    return null;
                throw new InvalidOperationException($"Anchor not found: {anchor}");
            }

            var (startLine, startCol) = GetLineColFromIndex(contents, match.Index);
            var (endLine, endCol) = GetLineColFromIndex(contents, match.Index + match.Length);

            return new Dictionary<string, object>
            {
                ["startLine"] = startLine,
                ["startCol"] = startCol,
                ["endLine"] = endLine,
                ["endCol"] = endCol,
                ["newText"] = ""
            };
        }

        /// <summary>
        /// Process anchor_replace operation
        /// </summary>
        static Dictionary<string, object> ProcessAnchorReplace(Dictionary<string, object> edit, string contents)
        {
            var anchor = GetStringValue(edit, "anchor");
            var replacement = GetStringValue(edit, "text") ?? GetStringValue(edit, "replacement") ?? "";
            var ignoreCase = GetBoolValue(edit, "ignoreCase");
            var preferLast = GetBoolValue(edit, "preferLast", true);

            if (string.IsNullOrEmpty(anchor))
                return null;

            var options = RegexOptions.Multiline;
            if (ignoreCase)
                options |= RegexOptions.IgnoreCase;

            var match = AnchorMatcher.FindBestAnchorMatch(anchor, contents, options, preferLast);
            if (match == null)
            {
                if (GetBoolValue(edit, "allowNoop", true))
                    return null;
                throw new InvalidOperationException($"Anchor not found: {anchor}");
            }

            var (startLine, startCol) = GetLineColFromIndex(contents, match.Index);
            var (endLine, endCol) = GetLineColFromIndex(contents, match.Index + match.Length);

            return new Dictionary<string, object>
            {
                ["startLine"] = startLine,
                ["startCol"] = startCol,
                ["endLine"] = endLine,
                ["endCol"] = endCol,
                ["newText"] = replacement
            };
        }

        /// <summary>
        /// Process replace_range operation
        /// </summary>
        static Dictionary<string, object> ProcessReplaceRange(Dictionary<string, object> edit)
        {
            return new Dictionary<string, object>
            {
                ["startLine"] = GetIntValue(edit, "startLine"),
                ["startCol"] = GetIntValue(edit, "startCol"),
                ["endLine"] = GetIntValue(edit, "endLine"),
                ["endCol"] = GetIntValue(edit, "endCol"),
                ["newText"] = GetStringValue(edit, "text") ?? ""
            };
        }

        /// <summary>
        /// Process regex_replace operation
        /// </summary>
        static Dictionary<string, object> ProcessRegexReplace(Dictionary<string, object> edit, string contents)
        {
            var pattern = GetStringValue(edit, "pattern") ?? GetStringValue(edit, "anchor");
            var replacement = GetStringValue(edit, "replacement") ?? GetStringValue(edit, "text") ?? "";
            var ignoreCase = GetBoolValue(edit, "ignoreCase");

            if (string.IsNullOrEmpty(pattern))
                return null;

            var options = RegexOptions.Multiline;
            if (ignoreCase)
                options |= RegexOptions.IgnoreCase;

            var match = AnchorMatcher.FindBestAnchorMatch(pattern, contents, options, true);
            if (match == null)
                return null;

            // Expand $1, $2... backreferences using the match groups
            var expandedReplacement = Regex.Replace(replacement, @"\$(\d+)", m =>
            {
                int groupNum = int.Parse(m.Groups[1].Value);
                return groupNum < match.Groups.Count ? (match.Groups[groupNum]?.Value ?? "") : "";
            });

            var (startLine, startCol) = GetLineColFromIndex(contents, match.Index);
            var (endLine, endCol) = GetLineColFromIndex(contents, match.Index + match.Length);

            return new Dictionary<string, object>
            {
                ["startLine"] = startLine,
                ["startCol"] = startCol,
                ["endLine"] = endLine,
                ["endCol"] = endCol,
                ["newText"] = expandedReplacement
            };
        }

        /// <summary>
        /// Best-effort normalization of script "name" and "path".
        ///
        /// Accepts any of:
        /// - name = "SmartReach", path = "Assets/Scripts/Interaction"
        /// - name = "SmartReach.cs", path = "Assets/Scripts/Interaction"
        /// - name = "Assets/Scripts/Interaction/SmartReach.cs", path = ""
        /// - path = "Assets/Scripts/Interaction/SmartReach.cs" (name empty)
        /// - name or path using uri prefixes: unity://path/..., file://...
        /// - accidental duplicates like "Assets/.../SmartReach.cs/SmartReach.cs"
        ///
        /// Returns (name_without_extension, directory_path_under_Assets).
        /// </summary>
        /// <param name="name">Script name or full path</param>
        /// <param name="path">Directory path or full path</param>
        /// <returns>Tuple of (normalized_name, normalized_path)</returns>
        static (string name, string path) NormalizeScriptLocator(string name, string path)
        {
            string n = (name ?? "").Trim();
            string p = (path ?? "").Trim();

            string StripPrefix(string s)
            {
                if (s.StartsWith("unity://path/"))
                    return s.Substring("unity://path/".Length);
                if (s.StartsWith("file://"))
                    return s.Substring("file://".Length);
                return s;
            }

            string CollapseDuplicateTail(string inputPath)
            {
                if (string.IsNullOrEmpty(inputPath))
                    return inputPath;
                var parts = inputPath.Split('/');
                if (parts.Length >= 2 && parts[parts.Length - 1] == parts[parts.Length - 2])
                    return string.Join("/", parts.Take(parts.Length - 1));
                return inputPath;
            }

            // Prefer a full path if provided in either field
            string candidate = "";
            foreach (var v in new[] { n, p })
            {
                var v2 = StripPrefix(v);
                if (v2.EndsWith(".cs") || v2.StartsWith("Assets/"))
                {
                    candidate = v2;
                    break;
                }
            }

            if (!string.IsNullOrEmpty(candidate))
            {
                candidate = CollapseDuplicateTail(candidate);
                // If a directory was passed in path and file in name, join them
                if (!candidate.EndsWith(".cs") && n.EndsWith(".cs"))
                {
                    var v2 = StripPrefix(n);
                    candidate = candidate.TrimEnd('/') + "/" + v2.Split('/').Last();
                }
                if (candidate.EndsWith(".cs"))
                {
                    var parts = candidate.Split('/');
                    var fileName = parts[parts.Length - 1];
                    var dirPath = parts.Length > 1 ? string.Join("/", parts.Take(parts.Length - 1)) : "Assets";
                    var baseName = fileName.Length > 3 && fileName.ToLowerInvariant().EndsWith(".cs") ?
                        fileName.Substring(0, fileName.Length - 3) : fileName;
                    return (baseName, dirPath);
                }
            }

            // Fall back: remove extension from name if present and return given path
            var baseName2 = n.ToLowerInvariant().EndsWith(".cs") ? n.Substring(0, n.Length - 3) : n;
            return (baseName2, string.IsNullOrEmpty(p) ? "Assets" : p);
        }

        /// <summary>
        /// Get line and column from character index (1-based)
        /// </summary>
        static (int line, int col) GetLineColFromIndex(string text, int index)
        {
            int line = 1;
            int col = 1;

            for (int i = 0; i < index && i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    col = 1;
                }
                else if (text[i] != '\r') // Don't count \r in CRLF
                {
                    col++;
                }
            }

            return (line, col);
        }

        /// <summary>
        /// Get end of file position
        /// </summary>
        static (int line, int col) GetEndOfFilePosition(string contents)
        {
            return GetLineColFromIndex(contents, contents.Length);
        }

        /// <summary>
        /// Compute SHA256 hash of content
        /// </summary>
        static string ComputeSha256(string content)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                var hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// Extract string value from dictionary
        /// </summary>
        static string GetStringValue(Dictionary<string, object> dict, string key)
        {
            return dict?.GetValueOrDefault(key)?.ToString();
        }

        /// <summary>
        /// Extract integer value from dictionary
        /// </summary>
        static int GetIntValue(Dictionary<string, object> dict, string key)
        {
            if (dict?.GetValueOrDefault(key) is int intValue)
                return intValue;

            if (int.TryParse(dict?.GetValueOrDefault(key)?.ToString(), out int parsed))
                return parsed;

            return 0;
        }

        /// <summary>
        /// Extract boolean value from dictionary
        /// </summary>
        static bool GetBoolValue(Dictionary<string, object> dict, string key, bool defaultValue = false)
        {
            if (dict?.GetValueOrDefault(key) is bool boolValue)
                return boolValue;

            if (bool.TryParse(dict?.GetValueOrDefault(key)?.ToString(), out bool parsed))
                return parsed;

            return defaultValue;
        }

        /// <summary>
        /// Check if response indicates success
        /// </summary>
        static bool IsSuccessResponse(object response)
        {
            if (response == null)
                return false;

            var props = GetObjectProperties(response);
            return props.GetValueOrDefault("success") as bool? == true;
        }

        /// <summary>
        /// Extract contents from ManageScript read result
        /// </summary>
        static string ExtractContentsFromReadResult(object result)
        {
            try
            {
                var resultDict = GetObjectProperties(result);
                var data = resultDict.GetValueOrDefault("data");

                if (data != null)
                {
                    var dataDict = GetObjectProperties(data);
                    var contents = dataDict.GetValueOrDefault("contents") as string;

                    if (!string.IsNullOrEmpty(contents))
                        return contents;

                    // Try encoded contents
                    var encodedContents = dataDict.GetValueOrDefault("encoded_contents") as string;
                    if (!string.IsNullOrEmpty(encodedContents))
                    {
                        try
                        {
                            var bytes = Convert.FromBase64String(encodedContents);
                            return Encoding.UTF8.GetString(bytes);
                        }
                        catch
                        {
                            // Fall through to return null
                        }
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Get properties from anonymous object or dictionary
        /// </summary>
        static Dictionary<string, object> GetObjectProperties(object obj)
        {
            if (obj is Dictionary<string, object> dict)
                return dict;

            if (obj == null)
                return new Dictionary<string, object>();

            var result = new Dictionary<string, object>();
            var type = obj.GetType();
            var properties = type.GetProperties();

            foreach (var prop in properties)
            {
                try
                {
                    result[prop.Name] = prop.GetValue(obj);
                }
                catch
                {
                    // Skip properties that can't be read
                }
            }

            return result;
        }

        /// <summary>
        /// Minimal local edit application for preview
        /// </summary>
        static string ApplyEditsLocally(string originalText, List<Dictionary<string, object>> edits)
        {
            string text = originalText;
            foreach (var edit in edits ?? new List<Dictionary<string, object>>())
            {
                var op = GetStringValue(edit, "op")?.ToLowerInvariant();

                if (string.IsNullOrEmpty(op))
                {
                    throw new ArgumentException("Edit operation is required");
                }

                switch (op)
                {
                    case "prepend":
                        var prependText = GetStringValue(edit, "text") ?? "";
                        text = (prependText.EndsWith("\n") ? prependText : prependText + "\n") + text;
                        break;

                    case "append":
                        var appendText = GetStringValue(edit, "text") ?? "";
                        if (!text.EndsWith("\n"))
                            text += "\n";
                        text += appendText;
                        if (!text.EndsWith("\n"))
                            text += "\n";
                        break;

                    case "anchor_insert":
                        var anchor = GetStringValue(edit, "anchor") ?? "";
                        var position = GetStringValue(edit, "position")?.ToLowerInvariant() ?? "before";
                        var insertText = GetStringValue(edit, "text") ?? "";
                        var ignoreCase = GetBoolValue(edit, "ignoreCase");
                        var preferLast = GetBoolValue(edit, "preferLast", true);

                        if (!string.IsNullOrEmpty(anchor))
                        {
                            var flags = RegexOptions.Multiline;
                            if (ignoreCase) flags |= RegexOptions.IgnoreCase;

                            // Use improved anchor matching logic
                            var match = AnchorMatcher.FindBestAnchorMatch(anchor, text, flags, preferLast);
                            if (match != null)
                            {
                                int idx = position == "before" ? match.Index : match.Index + match.Length;
                                text = text.Substring(0, idx) + insertText + text.Substring(idx);
                            }
                            else if (!GetBoolValue(edit, "allowNoop", true))
                            {
                                throw new InvalidOperationException($"Anchor not found: {anchor}");
                            }
                        }
                        break;

                    case "replace_range":
                        var startLine = GetIntValue(edit, "startLine");
                        var startCol = GetIntValue(edit, "startCol");
                        var endLine = GetIntValue(edit, "endLine");
                        var endCol = GetIntValue(edit, "endCol");
                        var rangeReplacement = GetStringValue(edit, "text") ?? "";

                        if (startLine > 0 && startCol > 0 && endLine > 0 && endCol > 0)
                        {
                            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                            var maxLine = lines.Length + 1; // 1-based, exclusive end

                            if (startLine >= 1 && endLine >= startLine && endLine <= maxLine &&
                                startCol >= 1 && endCol >= 1)
                            {
                                // Convert 1-based line/col to 0-based character indices
                                int IndexOf(int line, int col)
                                {
                                    if (line <= lines.Length)
                                    {
                                        int index = 0;
                                        for (int i = 0; i < line - 1; i++)
                                        {
                                            index += lines[i].Length + 1; // +1 for \n
                                        }
                                        return index + (col - 1);
                                    }
                                    return text.Length;
                                }

                                int a = IndexOf(startLine, startCol);
                                int b = IndexOf(endLine, endCol);
                                text = text.Substring(0, a) + rangeReplacement + text.Substring(b);
                            }
                            else
                            {
                                throw new ArgumentOutOfRangeException("replace_range out of bounds");
                            }
                        }
                        break;

                    case "regex_replace":
                        var pattern = GetStringValue(edit, "pattern") ?? GetStringValue(edit, "anchor");
                        var replacement = GetStringValue(edit, "replacement") ?? GetStringValue(edit, "text") ?? "";
                        var regexIgnoreCase = GetBoolValue(edit, "ignoreCase");
                        var count = GetIntValue(edit, "count"); // 0 = replace all

                        if (!string.IsNullOrEmpty(pattern))
                        {
                            var options = RegexOptions.Multiline;
                            if (regexIgnoreCase) options |= RegexOptions.IgnoreCase;

                            // Convert $1, $2.. backreferences to \g<n> format
                            var convertedReplacement = Regex.Replace(replacement, @"\$(\d+)", @"\g<$1>");

                            if (count > 0)
                            {
                                // Replace only 'count' occurrences
                                var matches = Regex.Matches(text, pattern, options);
                                int replacements = 0;
                                for (int i = matches.Count - 1; i >= 0 && replacements < count; i--)
                                {
                                    var match = matches[i];
                                    text = text.Substring(0, match.Index) +
                                           Regex.Replace(match.Value, pattern, convertedReplacement, options) +
                                           text.Substring(match.Index + match.Length);
                                    replacements++;
                                }
                            }
                            else
                            {
                                // Replace all occurrences (count = 0)
                                text = Regex.Replace(text, pattern, convertedReplacement, options);
                            }
                        }
                        break;

                    default:
                        throw new ArgumentException($"Unknown edit operation for local preview: {op}");
                }
            }
            return text;
        }

        /// <summary>
        /// Process anchor_insert operation for mixed processing
        /// </summary>
        static Dictionary<string, object> ProcessMixedAnchorInsert(Dictionary<string, object> edit, string baseText)
        {
            var anchor = GetStringValue(edit, "anchor") ?? "";
            var position = GetStringValue(edit, "position")?.ToLowerInvariant() ?? "after";
            var textField = GetStringValue(edit, "text") ?? GetStringValue(edit, "insert") ??
                           GetStringValue(edit, "content") ?? GetStringValue(edit, "replacement") ?? "";
            var ignoreCase = GetBoolValue(edit, "ignore_case");

            if (string.IsNullOrEmpty(anchor))
                return null;

            var flags = RegexOptions.Multiline;
            if (ignoreCase) flags |= RegexOptions.IgnoreCase;

            try
            {
                var match = AnchorMatcher.FindBestAnchorMatch(anchor, baseText, flags, true);
                if (match == null)
                    return null; // Continue processing

                int idx = position == "before" ? match.Index : match.Index + match.Length;

                // Normalize insertion to avoid jammed methods
                var textFieldNorm = textField;
                if (!string.IsNullOrEmpty(textFieldNorm))
                {
                    if (!textFieldNorm.StartsWith("\n"))
                        textFieldNorm = "\n" + textFieldNorm;
                    if (!textFieldNorm.EndsWith("\n"))
                        textFieldNorm = textFieldNorm + "\n";
                }

                var (sl, sc) = GetLineColFromIndex(baseText, idx);
                return new Dictionary<string, object>
                {
                    ["startLine"] = sl,
                    ["startCol"] = sc,
                    ["endLine"] = sl,
                    ["endCol"] = sc,
                    ["newText"] = textFieldNorm
                };
            }
            catch
            {
                return null; // Continue processing on error
            }
        }

        /// <summary>
        /// Process replace_range operation for mixed processing
        /// </summary>
        static Dictionary<string, object> ProcessMixedReplaceRange(Dictionary<string, object> edit, string textField)
        {
            var requiredKeys = new[] { "startLine", "startCol", "endLine", "endCol" };
            if (!requiredKeys.All(k => edit.ContainsKey(k)))
                return null; // Skip if missing required fields

            return new Dictionary<string, object>
            {
                ["startLine"] = GetIntValue(edit, "startLine"),
                ["startCol"] = GetIntValue(edit, "startCol"),
                ["endLine"] = GetIntValue(edit, "endLine"),
                ["endCol"] = GetIntValue(edit, "endCol"),
                ["newText"] = textField
            };
        }

        /// <summary>
        /// Process regex_replace operation for mixed processing
        /// NO confirmation logic
        /// </summary>
        static Dictionary<string, object> ProcessMixedRegexReplace(Dictionary<string, object> edit, string baseText, string textField)
        {
            var pattern = GetStringValue(edit, "pattern") ?? "";
            var ignoreCase = GetBoolValue(edit, "ignore_case");

            if (string.IsNullOrEmpty(pattern))
                return null;

            try
            {
                var flags = RegexOptions.Multiline;
                if (ignoreCase) flags |= RegexOptions.IgnoreCase;

                var regexObj = new Regex(pattern, flags);
                var match = regexObj.Match(baseText);
                if (!match.Success)
                    return null; // Continue processing if no match

                // Expand $1, $2... in replacement using this match
                var expandedReplacement = Regex.Replace(textField, @"\$(\d+)", m =>
                {
                    if (int.TryParse(m.Groups[1].Value, out int groupNum) && groupNum < match.Groups.Count)
                        return match.Groups[groupNum]?.Value ?? "";
                    return "";
                });

                var (sl, sc) = GetLineColFromIndex(baseText, match.Index);
                var (el, ec) = GetLineColFromIndex(baseText, match.Index + match.Length);

                return new Dictionary<string, object>
                {
                    ["startLine"] = sl,
                    ["startCol"] = sc,
                    ["endLine"] = el,
                    ["endCol"] = ec,
                    ["newText"] = expandedReplacement
                };
            }
            catch
            {
                return null; // Continue processing on error
            }
        }

        /// <summary>
        /// Generate unified diff with improved formatting
        /// </summary>
        static string GenerateUnifiedDiff(string before, string after)
        {
            if (before == after)
            {
                return "--- before\n+++ after\n(no changes)";
            }

            var beforeLines = before.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var afterLines = after.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            var diff = new List<string>
            {
                "--- before",
                "+++ after"
            };

            // Simple but effective line-by-line diff
            int maxLines = Math.Max(beforeLines.Length, afterLines.Length);
            bool hasChanges = false;

            for (int i = 0; i < maxLines; i++)
            {
                string beforeLine = i < beforeLines.Length ? beforeLines[i] : null;
                string afterLine = i < afterLines.Length ? afterLines[i] : null;

                if (beforeLine == afterLine)
                {
                    // Unchanged line - show as context
                    if (beforeLine != null)
                        diff.Add($" {beforeLine}");
                }
                else
                {
                    hasChanges = true;
                    // Show removed line
                    if (beforeLine != null)
                        diff.Add($"-{beforeLine}");
                    // Show added line
                    if (afterLine != null)
                        diff.Add($"+{afterLine}");
                }
            }

            if (!hasChanges)
            {
                return "--- before\n+++ after\n(no changes)";
            }

            // Limit diff size to keep responses manageable
            if (diff.Count > 800)
            {
                diff = diff.Take(800).ToList();
                diff.Add("... (diff truncated) ...");
            }

            return string.Join("\n", diff);
        }
    }
}
