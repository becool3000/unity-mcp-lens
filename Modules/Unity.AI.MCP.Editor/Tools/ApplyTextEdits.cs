using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry;
using Unity.AI.MCP.Editor.Tools.Parameters;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.MCP.Editor.Tools
{
    /// <summary>
    /// Handles applying small text edits to C# scripts identified by URI.
    /// </summary>
    public static class ApplyTextEdits
    {
        /// <summary>
        /// Human-readable description of the Unity.ApplyTextEdits tool functionality and usage guidelines.
        /// </summary>
        public const string Title = "Apply text edits to a file";

        public const string Description = @"Apply small text edits to a C# script identified by URI.

⚠️ IMPORTANT: This tool replaces EXACT character positions. Always verify content at target lines/columns BEFORE editing!
Common mistakes:
- Assuming what's on a line without checking
- Using wrong line numbers (they're 1-indexed)
- Miscounting column positions (also 1-indexed, tabs count as 1)

RECOMMENDED WORKFLOW:
1) First call resources/read with start_line/line_count to verify exact content
2) Count columns carefully (or use Unity.FindInFile to locate patterns)
3) Apply your edit with precise coordinates
4) Consider Unity.ScriptApplyEdits with anchors for safer pattern-based replacements

Args:
- uri: unity://path/Assets/... or file://... or Assets/...
- edits: list of {startLine,startCol,endLine,endCol,newText} (1-indexed!)
- precondition_sha256: SHA of current file (prevents concurrent edit conflicts)

Notes:
- Path must resolve under Assets/
- For method/class operations, use Unity.ScriptApplyEdits (safer, structured edits)
- For pattern-based replacements, consider anchor operations in Unity.ScriptApplyEdits";

        /// <summary>
        /// Returns the output schema for this tool.
        /// </summary>
        /// <returns>An object describing the structure of the tool's output including success status, message, and data fields</returns>
        [McpOutputSchema("Unity.ApplyTextEdits")]
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
                        description = "Text edit results",
                        properties = new
                        {
                            uri = new { type = "string", description = "Unity URI of the edited script" },
                            path = new { type = "string", description = "Relative path of the edited script" },
                            editsApplied = new { type = "integer", description = "Number of edits applied" },
                            sha256 = new { type = "string", description = "SHA256 hash of the modified script" },
                            scheduledRefresh = new { type = "boolean", description = "Whether a refresh was scheduled" },
                            no_op = new { type = "boolean", description = "Whether this was a no-op (no changes made)" },
                            normalizedEdits = new { type = "array", description = "Normalized edit operations that were applied" },
                            warnings = new { type = "array", description = "Any warnings generated during processing" }
                        }
                    }
                },
                required = new[] { "success", "message" }
            };
        }

        /// <summary>
        /// Main handler for applying text edits.
        /// </summary>
        /// <param name="parameters">Parameters containing the URI, edits array, optional precondition SHA, and options</param>
        /// <returns>A response object indicating success or failure with detailed results including normalized edits and any warnings</returns>
        [McpTool("Unity.ApplyTextEdits", Description, Title, Groups = new string[] { "core", "scripting" })]
        public static object HandleCommand(ApplyTextEditsParams parameters)
        {
            string uri = parameters?.Uri;
            if (string.IsNullOrEmpty(uri))
            {
                return Response.Error("uri parameter is required.");
            }

            if (parameters.Edits == null || parameters.Edits.Count == 0)
            {
                return Response.Error("edits parameter is required and must not be empty.");
            }

            // Split URI into name and directory using ScriptRefreshHelpers
            var (name, directory) = ScriptRefreshHelpers.SplitUri(uri);

            // Validate the split result
            if (string.IsNullOrEmpty(name))
            {
                return Response.Error("invalid_uri: URI must include a script file name.");
            }

            if (string.IsNullOrEmpty(directory))
            {
                return Response.Error("invalid_uri: URI must include a valid directory path.");
            }

            // Ensure directory is under Assets/
            if (!directory.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                return Response.Error("path_outside_assets: URI must resolve under 'Assets/'.");
            }

            try
            {
                // Normalize edits and detect if we need to read the file for coordinate conversion
                var (normalizedEdits, warnings) = NormalizeEdits(parameters.Edits, name, directory, parameters.Strict);

                if (normalizedEdits == null)
                {
                    // Error occurred during normalization, already returned
                    return Response.Error("Failed to normalize edit coordinates: " + warnings.ToString());
                }

                // Consider only true replace ranges (non-zero length). Pure insertions (zero-width) don't overlap.
                var spans = new List<((int line, int col) start, (int line, int col) end)>();

                foreach (var e in normalizedEdits ?? new List<Dictionary<string, object>>())
                {
                    var s = GetPositionTuple(e, true);
                    var t = GetPositionTuple(e, false);
                    if (s != t)
                    {
                        spans.Add((s, t));
                    }
                }

                if (spans.Count > 0)
                {
                    var spansorted = spans.OrderBy(p => p.start.line).ThenBy(p => p.start.col).ToList();

                    for (int i = 1; i < spansorted.Count; i++)
                    {
                        var prevEnd = spansorted[i - 1].end;
                        var currStart = spansorted[i].start;

                        // Overlap if prev_end > curr_start (strict), i.e., not prev_end <= curr_start
                        if (!IsLessOrEqual(prevEnd, currStart))
                        {
                            var conflicts = new List<Dictionary<string, object>>
                            {
                                new()
                                {
                                    ["startA"] = new Dictionary<string, object> { ["line"] = spansorted[i - 1].start.line, ["col"] = spansorted[i - 1].start.col },
                                    ["endA"] = new Dictionary<string, object> { ["line"] = spansorted[i - 1].end.line, ["col"] = spansorted[i - 1].end.col },
                                    ["startB"] = new Dictionary<string, object> { ["line"] = spansorted[i].start.line, ["col"] = spansorted[i].start.col },
                                    ["endB"] = new Dictionary<string, object> { ["line"] = spansorted[i].end.line, ["col"] = spansorted[i].end.col }
                                }
                            };

                            return Response.Error("Overlapping edit ranges detected", new { status = "overlap", conflicts });
                        }
                    }
                }

                // Note: Do not auto-compute precondition if missing; callers should supply it
                // via mcp__unity__get_sha or a prior read. This avoids hidden extra calls and
                // preserves existing call-count expectations in clients/tests.

                // Default options: for multi-span batches, prefer atomic to avoid mid-apply imbalance
                var opts = new Dictionary<string, object>(parameters.Options ?? new Dictionary<string, object>());
                if (normalizedEdits.Count > 1 && !opts.ContainsKey("applyMode"))
                {
                    opts["applyMode"] = "atomic";
                }

                // Support optional debug preview for span-by-span simulation without write
                if (opts.TryGetValue("debug_preview", out var debugPreview) && Convert.ToBoolean(debugPreview))
                {
                    try
                    {
                        // Apply locally to preview final result
                        // We cannot guarantee file contents here without a read; return normalized spans only
                        return Response.Success("Preview only (no write)", new
                        {
                            normalizedEdits,
                            preview = true
                        });
                    }
                    catch (Exception e)
                    {
                        return Response.Error($"debug_preview failed: {e.Message}", new { normalizedEdits });
                    }
                }

                // Create parameters for the Unity.ManageScript command
                var scriptParams = new JObject();
                scriptParams["action"] = "apply_text_edits";
                scriptParams["name"] = name;
                scriptParams["path"] = directory;
                scriptParams["edits"] = JArray.FromObject(normalizedEdits);

                if (!string.IsNullOrEmpty(parameters.PreconditionSha256))
                {
                    scriptParams["precondition_sha256"] = parameters.PreconditionSha256;
                }

                scriptParams["options"] = JObject.FromObject(opts);

                // Call the Unity.ManageScript command
                var resp = ManageScript.HandleCommand(scriptParams);

                Dictionary<string, object> respDict = GetAnonymousProperties(resp);

                object data;

                if (respDict.ContainsKey("data") && respDict["data"] != null)
                {
                    data = new
                    {
                        normalizedEdits = normalizedEdits,
                        applyTextEditsDetailInfo = respDict["data"]
                    };
                }
                else
                {
                    data = new
                    {
                        normalizedEdits = normalizedEdits
                    };
                }

                // Optional: flip sentinel via menu if explicitly requested
                if (Convert.ToBoolean(respDict.GetValueOrDefault("success", false)))
                {
                    if (parameters.Options != null &&
                        parameters.Options.TryGetValue("force_sentinel_reload", out var forceReload) &&
                        Convert.ToBoolean(forceReload))
                    {

                        // Start async task to flip reload sentinel
                        System.Threading.Tasks.Task.Run(async () =>
                        {

                            await System.Threading.Tasks.Task.Delay(100);

                            // Get latest status to check if already reloading
                            var statusFiles = Settings.MCPConstants.StatusFiles;

                            if (statusFiles.Length > 0)
                            {
                                var statusJson = System.IO.File.ReadAllText(statusFiles[0]);
                                var status = JObject.Parse(statusJson);
                                if (status["reloading"]?.Value<bool>() == true)
                                {
                                    return; // Already reloading, don't flip again
                                }

                            }

                            // Execute menu item to flip reload sentinel using ManageMenuItem
                            var menuParams = new ManageMenuItemParams
                            {
                                Action = MenuItemAction.Execute,
                                MenuPath = "MCP/Flip Reload Sentinel",
                                Refresh = false
                            };

                            ManageMenuItem.HandleCommand(menuParams);

                        });

                        return Response.Success(respDict.GetValueOrDefault("message", "Run ApplyTextEdits successfully").ToString(), data);
                    }
                    else
                    {
                        return Response.Success(respDict.GetValueOrDefault("message", "Run ApplyTextEdits successfully").ToString(), data);
                    }
                }
                else
                {
                    return Response.Error(respDict.GetValueOrDefault("message", "Failed to apply text edits").ToString(), data);
                }

            }
            catch (Exception e)
            {
                return Response.Error($"Failed to apply text edits: {e.Message}");
            }
        }

        /// <summary>
        /// Extracts all public properties from an anonymous object and returns them as a dictionary.
        /// </summary>
        static Dictionary<string, object> GetAnonymousProperties(object anon)
        {
            if (anon == null) throw new ArgumentNullException(nameof(anon));
                var type = anon.GetType();

            var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
            var dict = new Dictionary<string, object>(props.Length);
            foreach (var p in props)
            {
                dict[p.Name] = p.GetValue(anon);
            }
            return dict;
        }

        /// <summary>
        /// Helper method to get position tuple from edit for overlap detection.
        /// </summary>
        static (int, int) GetPositionTuple(Dictionary<string, object> edit, bool isStart)
        {
            return isStart
                ? (Convert.ToInt32(edit.GetValueOrDefault("startLine", 1)), Convert.ToInt32(edit.GetValueOrDefault("startCol", 1)))
                : (Convert.ToInt32(edit.GetValueOrDefault("endLine", 1)), Convert.ToInt32(edit.GetValueOrDefault("endCol", 1)));
        }

        /// <summary>
        /// Helper method to check if position A is less than or equal to position B.
        /// </summary>
        static bool IsLessOrEqual((int line, int col) a, (int line, int col) b)
        {
            return a.line < b.line || (a.line == b.line && a.col <= b.col);
        }

        /// <summary>
        /// Normalize common aliases/misuses for resilience:
        /// - Accept LSP-style range objects: {range:{start:{line,character}, end:{...}}, newText|text}
        /// - Accept index ranges as a 2-int array: {range:[startIndex,endIndex], text}
        /// If normalization is required, read current contents to map indices -> 1-based line/col.
        /// </summary>
        static (List<Dictionary<string, object>> normalizedEdits, List<string> warnings) NormalizeEdits(
            List<Dictionary<string, object>> edits, string name, string directory, bool? strict)
        {
            var normalizedEdits = new List<Dictionary<string, object>>();
            var warnings = new List<string>();

            // Helper function to check if normalization is needed
            bool NeedsNormalization(List<Dictionary<string, object>> editList)
            {
                foreach (var e in editList ?? new List<Dictionary<string, object>>())
                {
                    if (!e.ContainsKey("startLine") || !e.ContainsKey("startCol") ||
                        !e.ContainsKey("endLine") || !e.ContainsKey("endCol") ||
                        (!e.ContainsKey("newText") && e.ContainsKey("text")))
                    {
                        return true;
                    }
                }
                return false;
            }

            if (NeedsNormalization(edits))
            {
                if (!ManageScript.TryReadScriptText(name, directory, out var contents, out _, out _, out var error))
                {
                    warnings.Add(error ?? "Failed to read file");
                    return (null, warnings);
                }

                // Helper to map 0-based character index to 1-based line/col
                (int line, int col) LineColFromIndex(int idx)
                {
                    if (idx <= 0)
                        return (1, 1);
                    // Count lines up to idx and position within line
                    int nlCount = 0;
                    for (int i = 0; i < idx && i < contents.Length; i++)
                    {
                        if (contents[i] == '\n') nlCount++;
                    }
                    int line = nlCount + 1;
                    int lastNl = contents.LastIndexOf('\n', Math.Min(idx - 1, contents.Length - 1));
                    int col = lastNl >= 0 ? (idx - (lastNl + 1)) + 1 : idx + 1;
                    return (line, col);
                }

                foreach (var e in edits ?? new List<Dictionary<string, object>>())
                {
                    var e2 = new Dictionary<string, object>(e);
                    // Map text->newText if needed
                    if (!e2.ContainsKey("newText") && e2.ContainsKey("text"))
                    {
                        e2["newText"] = e2["text"];
                        e2.Remove("text");
                    }

                    if (e2.ContainsKey("startLine") && e2.ContainsKey("startCol") &&
                        e2.ContainsKey("endLine") && e2.ContainsKey("endCol"))
                    {
                        // Guard: explicit fields must be 1-based.
                        bool zeroBased = false;
                        var requiredFields = new[] { "startLine", "startCol", "endLine", "endCol" };
                        foreach (var k in requiredFields)
                        {
                            if (Convert.ToInt32(e2.GetValueOrDefault(k, 1)) < 1)
                            {
                                zeroBased = true;
                            }
                        }
                        if (zeroBased)
                        {
                            if (strict == true)
                            {
                                return (null, new List<string> { "Explicit line/col fields are 1-based; received zero-based." });
                            }
                            // Normalize by clamping to 1 and warn
                            foreach (var k in requiredFields)
                            {
                                try
                                {
                                    if (Convert.ToInt32(e2.GetValueOrDefault(k, 1)) < 1)
                                    {
                                        e2[k] = 1;
                                    }
                                }
                                catch
                                {
                                    // Ignore conversion errors
                                }
                            }
                            if (!warnings.Contains("zero_based_explicit_fields_normalized"))
                            {
                                warnings.Add("zero_based_explicit_fields_normalized");
                            }
                        }
                        normalizedEdits.Add(e2);
                        continue;
                    }

                    var rng = e2.GetValueOrDefault("range");
                    if (rng is JObject rangeDict)
                    {
                        // LSP style: 0-based
                        var start = rangeDict["start"] as JObject;
                        var end = rangeDict["end"] as JObject;
                        e2["startLine"] = (start?["line"]?.Value<int>() ?? 0) + 1;
                        e2["startCol"] = (start?["character"]?.Value<int>() ?? 0) + 1;
                        e2["endLine"] = (end?["line"]?.Value<int>() ?? 0) + 1;
                        e2["endCol"] = (end?["character"]?.Value<int>() ?? 0) + 1;
                        e2.Remove("range");
                        normalizedEdits.Add(e2);
                        continue;
                    }
                    if (rng is JArray rangeArray && rangeArray.Count == 2)
                    {
                        int a = rangeArray[0].Value<int>();
                        int b = rangeArray[1].Value<int>();
                        if (b < a)
                        {
                            (a, b) = (b, a);
                        }
                        var (sl, sc) = LineColFromIndex(a);
                        var (el, ec) = LineColFromIndex(b);
                        e2["startLine"] = sl;
                        e2["startCol"] = sc;
                        e2["endLine"] = el;
                        e2["endCol"] = ec;
                        e2.Remove("range");
                        normalizedEdits.Add(e2);
                        continue;
                    }
                    // Could not normalize this edit
                    return (null, new List<string> { "apply_text_edits requires startLine/startCol/endLine/endCol/newText or a normalizable 'range', expected: [\"startLine\",\"startCol\",\"endLine\",\"endCol\",\"newText\"]" });
                }

            }
            else
            {
                // Even when edits appear already in explicit form, validate 1-based coordinates.
                foreach (var e in edits ?? new List<Dictionary<string, object>>())
                {
                    var e2 = new Dictionary<string, object>(e);
                    var requiredFields = new[] { "startLine", "startCol", "endLine", "endCol" };
                    bool hasAll = requiredFields.All(k => e2.ContainsKey(k));

                    if (hasAll)
                    {
                        bool zeroBased = false;
                        foreach (var k in requiredFields)
                        {
                            if (Convert.ToInt32(e2.GetValueOrDefault(k, 1)) < 1)
                            {
                                zeroBased = true;
                            }
                        }

                        if (zeroBased)
                        {
                            if (strict == true)
                            {
                                return (null, new List<string> { "Explicit line/col fields are 1-based; received zero-based." });
                            }

                            foreach (var k in requiredFields)
                            {
                                if (Convert.ToInt32(e2.GetValueOrDefault(k, 1)) < 1)
                                {
                                    e2[k] = 1;
                                }
                            }

                            if (!warnings.Contains("zero_based_explicit_fields_normalized"))
                            {
                                warnings.Add("zero_based_explicit_fields_normalized");
                            }
                        }
                    }

                    normalizedEdits.Add(e2);
                }
            }

            return (normalizedEdits, warnings);
        }
    }
}
