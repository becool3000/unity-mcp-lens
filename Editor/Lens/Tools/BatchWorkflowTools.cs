#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Becool.UnityMcpLens.Editor.Tools
{
    class BatchWorkflowParams
    {
        [McpDescription("Ordered Unity MCP tool calls to execute in the current Lens bridge session.", Required = true)]
        public BatchWorkflowStepParams[] Steps { get; set; } = Array.Empty<BatchWorkflowStepParams>();
    }

    class BatchWorkflowStepParams
    {
        [McpDescription("Human-readable step name for reporting.")]
        public string Name { get; set; }

        [McpDescription("Unity MCP tool name to invoke.", Required = true)]
        public string Tool { get; set; }

        [McpDescription("Tool arguments object.")]
        public JObject Arguments { get; set; }

        [McpDescription("Optional exact active pack set for this step. Foundation is added automatically.")]
        public string[] RequiredPacks { get; set; }

        [McpDescription("Continue executing later steps when this step fails.")]
        public bool ContinueOnError { get; set; }

        [McpDescription("Annotates that this step may trigger an expected reload/reconnect window.")]
        public bool ExpectReload { get; set; }

        [McpDescription("When true, fail this step before execution unless the target tool is marked read-only.")]
        public bool ReadOnlyExpected { get; set; }
    }

    [McpTool(ToolPackCatalog.BatchExecuteWorkflowToolName,
        "Executes an ordered batch of Unity MCP tool calls in the current Lens bridge session, reusing pack state and returning compact per-step summaries.",
        "Execute Unity Batch Workflow",
        Groups = new[] { "core", "assistant" },
        EnabledByDefault = true)]
    class BatchExecuteWorkflowTool : IUnityMcpTool<BatchWorkflowParams>
    {
        const int k_InlineDataBudgetBytes = 2048;
        const int k_StepDetailBudgetBytes = 4096;

        static readonly string[] k_PreferredPackOrder =
        {
            ToolPackCatalog.ConsolePackId,
            ToolPackCatalog.ProjectPackId,
            ToolPackCatalog.ScenePackId,
            ToolPackCatalog.UiPackId,
            ToolPackCatalog.RuntimePackId,
            ToolPackCatalog.ScriptingPackId,
            ToolPackCatalog.AssetsPackId,
            ToolPackCatalog.DebugPackId
        };

        public async Task<object> ExecuteAsync(BatchWorkflowParams parameters)
        {
            var execution = McpToolExecutionScope.Current;
            if (string.IsNullOrWhiteSpace(execution?.ConnectionId))
                return Response.Error("Unity.Batch.ExecuteWorkflow requires an active Lens MCP bridge connection.");

            var steps = parameters?.Steps ?? Array.Empty<BatchWorkflowStepParams>();
            if (steps.Length == 0)
                return Response.Error("Unity.Batch.ExecuteWorkflow requires at least one step.");

            string connectionId = execution.ConnectionId;
            var originalPacks = BridgeLensSessionRegistry.GetActiveToolPacks(connectionId);
            var currentPacks = originalPacks;
            var results = new List<object>();
            var success = true;
            var failedStepCount = 0;
            var packTransitions = 0;
            var restoredPacks = false;
            string restoreError = null;
            var workflowStarted = Stopwatch.StartNew();

            try
            {
                for (int i = 0; i < steps.Length; i++)
                {
                    var step = steps[i] ?? new BatchWorkflowStepParams();
                    var stepStarted = Stopwatch.StartNew();
                    string stepName = string.IsNullOrWhiteSpace(step.Name) ? $"step_{i + 1}" : step.Name.Trim();
                    string toolName = (step.Tool ?? string.Empty).Trim();

                    object AddFailure(string errorKind, string message, object extra = null)
                    {
                        failedStepCount++;
                        success = false;
                        var row = BuildFailureRow(i, stepName, toolName, Array.Empty<string>(), step, stepStarted.ElapsedMilliseconds, errorKind, message, extra);
                        results.Add(row);
                        return row;
                    }

                    if (string.IsNullOrWhiteSpace(toolName))
                    {
                        AddFailure("missing_tool", "Batch step requires a non-empty tool name.");
                        if (!step.ContinueOnError)
                            break;
                        continue;
                    }

                    string normalizedToolName = McpToolRegistry.SanitizeToolName(toolName);
                    if (string.Equals(normalizedToolName, McpToolRegistry.SanitizeToolName(ToolPackCatalog.BatchExecuteWorkflowToolName), StringComparison.OrdinalIgnoreCase))
                    {
                        AddFailure("recursive_batch_rejected", "Unity.Batch.ExecuteWorkflow cannot execute itself recursively.");
                        if (!step.ContinueOnError)
                            break;
                        continue;
                    }

                    if (string.Equals(normalizedToolName, McpToolRegistry.SanitizeToolName(ToolPackCatalog.SetToolPacksToolName), StringComparison.OrdinalIgnoreCase))
                    {
                        AddFailure("contained_pack_change_rejected", "Unity.SetToolPacks cannot be used as a contained batch step.");
                        if (!step.ContinueOnError)
                            break;
                        continue;
                    }

                    if (!ResolveRequiredPacks(step, normalizedToolName, out var requiredPacks, out var packError))
                    {
                        AddFailure("pack_resolution_failed", packError);
                        if (!step.ContinueOnError)
                            break;
                        continue;
                    }

                    if (step.ReadOnlyExpected && !ToolMetadataPolicy.IsReadOnlyHint(normalizedToolName))
                    {
                        AddFailure("read_only_expected_failed", $"Step '{stepName}' expected read-only tool '{toolName}', but metadata marks it mutating.", new { requiredPacks });
                        if (!step.ContinueOnError)
                            break;
                        continue;
                    }

                    if (!StringArraysEqual(currentPacks, requiredPacks))
                    {
                        var manifest = BridgeManifestBroker.SetToolPacks(connectionId, requiredPacks, includeSchemas: false, out var setError);
                        if (manifest == null || !string.IsNullOrWhiteSpace(setError))
                        {
                            AddFailure("pack_activation_failed", setError ?? "Failed to activate required packs.", new { requiredPacks });
                            if (!step.ContinueOnError)
                                break;
                            continue;
                        }

                        packTransitions++;
                        currentPacks = manifest.activeToolPacks ?? requiredPacks;
                    }

                    if (!BridgeManifestBroker.IsToolAllowedForConnection(connectionId, normalizedToolName))
                    {
                        AddFailure("tool_not_allowed_by_active_packs", $"Tool '{toolName}' is not available in active packs [{string.Join(", ", currentPacks)}].", new { requiredPacks = currentPacks });
                        if (!step.ContinueOnError)
                            break;
                        continue;
                    }

                    try
                    {
                        var rawResult = await McpToolRegistry.ExecuteToolAsync(normalizedToolName, step.Arguments ?? new JObject());
                        var row = BuildSuccessRow(i, stepName, normalizedToolName, currentPacks, step, stepStarted.ElapsedMilliseconds, rawResult);
                        results.Add(row.Row);
                        if (!row.Success)
                        {
                            failedStepCount++;
                            success = false;
                            if (!step.ContinueOnError)
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        AddFailure(ex.GetType().Name, ex.Message, new { requiredPacks = currentPacks });
                        if (!step.ContinueOnError)
                            break;
                    }
                }
            }
            finally
            {
                var activePacks = BridgeLensSessionRegistry.GetActiveToolPacks(connectionId);
                if (!StringArraysEqual(activePacks, originalPacks))
                {
                    var restoreManifest = BridgeManifestBroker.SetToolPacks(connectionId, originalPacks, includeSchemas: false, out restoreError);
                    restoredPacks = restoreManifest != null && string.IsNullOrWhiteSpace(restoreError);
                    if (restoredPacks)
                        packTransitions++;
                }
                else
                {
                    restoredPacks = true;
                }
            }

            if (!restoredPacks)
            {
                success = false;
            }

            return new
            {
                success,
                message = success
                    ? $"Executed {results.Count} batch workflow step(s)."
                    : $"Executed {results.Count} batch workflow step(s) with {failedStepCount} failure(s).",
                stepCount = steps.Length,
                completedStepCount = results.Count,
                failedStepCount,
                durationMs = workflowStarted.ElapsedMilliseconds,
                packTransitions,
                originalPacks,
                finalActivePacks = BridgeLensSessionRegistry.GetActiveToolPacks(connectionId),
                restoredPacks,
                restoreError,
                results
            };
        }

        static bool ResolveRequiredPacks(BatchWorkflowStepParams step, string normalizedToolName, out string[] requiredPacks, out string error)
        {
            requiredPacks = ToolPackCatalog.DefaultActivePacks;
            error = null;

            if (step.RequiredPacks != null && step.RequiredPacks.Length > 0)
            {
                if (!ToolPackCatalog.TryNormalizeSelection(step.RequiredPacks, out requiredPacks, out error))
                    return false;

                return true;
            }

            var handler = McpToolRegistry.GetTool(normalizedToolName);
            if (handler == null)
            {
                error = $"Tool '{normalizedToolName}' is not registered.";
                return false;
            }

            var groups = handler.Attribute?.Groups ?? Array.Empty<string>();
            var matchingPacks = ToolPackCatalog.GetMatchingPackIds(normalizedToolName, groups);
            var additionalPacks = matchingPacks
                .Where(pack => !string.Equals(pack, ToolPackCatalog.FoundationPackId, StringComparison.OrdinalIgnoreCase))
                .Where(pack => !string.Equals(pack, ToolPackCatalog.FullPackId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(GetPreferredPackIndex)
                .ThenBy(pack => pack, StringComparer.Ordinal)
                .Take(ToolPackCatalog.MaxAdditionalPacks)
                .ToArray();

            if (additionalPacks.Length == 0)
            {
                if (ToolPackCatalog.ShouldIncludeTool(normalizedToolName, groups, ToolPackCatalog.DefaultActivePacks))
                    return true;

                error = $"Could not infer a non-foundation pack for tool '{normalizedToolName}'. Provide requiredPacks.";
                return false;
            }

            if (!ToolPackCatalog.TryNormalizeSelection(additionalPacks, out requiredPacks, out error))
                return false;

            return true;
        }

        static int GetPreferredPackIndex(string pack)
        {
            for (int i = 0; i < k_PreferredPackOrder.Length; i++)
            {
                if (string.Equals(k_PreferredPackOrder[i], pack, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return int.MaxValue;
        }

        static object BuildFailureRow(int index, string stepName, string toolName, string[] requiredPacks, BatchWorkflowStepParams step, long durationMs, string errorKind, string message, object extra = null)
        {
            return new
            {
                index,
                name = stepName,
                tool = toolName,
                requiredPacks,
                continueOnError = step?.ContinueOnError == true,
                expectReload = step?.ExpectReload == true,
                readOnlyExpected = step?.ReadOnlyExpected == true,
                success = false,
                durationMs,
                errorKind,
                error = message,
                extra
            };
        }

        static (object Row, bool Success) BuildSuccessRow(int index, string stepName, string toolName, string[] requiredPacks, BatchWorkflowStepParams step, long durationMs, object rawResult)
        {
            var token = ToToken(rawResult);
            var success = token.Type != JTokenType.Object || token["success"]?.Value<bool?>() != false;
            var message = token.Type == JTokenType.Object ? token["message"]?.Value<string>() : null;
            var code = token.Type == JTokenType.Object ? token["code"]?.Value<string>() : null;
            var error = token.Type == JTokenType.Object ? token["error"]?.Value<string>() : null;
            var data = token.Type == JTokenType.Object && token["data"] != null ? token["data"] : token;
            var fullBytes = PayloadBudgeting.GetUtf8ByteCount(token.ToString(Formatting.None));
            object fullResultDetailRef = null;
            if (fullBytes > k_StepDetailBudgetBytes)
            {
                fullResultDetailRef = ToolResultCompactor.CreateStoredDetailRef(
                    ToolPackCatalog.BatchExecuteWorkflowToolName,
                    rawResult,
                    fullBytes,
                    new
                    {
                        stepName,
                        toolName,
                        index
                    });
            }

            return (new
            {
                index,
                name = stepName,
                tool = toolName,
                requiredPacks,
                continueOnError = step?.ContinueOnError == true,
                expectReload = step?.ExpectReload == true,
                readOnlyExpected = step?.ReadOnlyExpected == true,
                success,
                message,
                code,
                error,
                durationMs,
                data = SummarizeData(data),
                detailRefs = CollectDetailRefs(token),
                fullResultDetailAvailable = fullResultDetailRef != null,
                fullResultDetailRef
            }, success);
        }

        static JToken ToToken(object value)
        {
            if (value == null)
                return JValue.CreateNull();

            try
            {
                return JToken.FromObject(value);
            }
            catch
            {
                return new JObject
                {
                    ["value"] = value.ToString()
                };
            }
        }

        static object SummarizeData(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            var bytes = PayloadBudgeting.GetUtf8ByteCount(token.ToString(Formatting.None));
            if (bytes <= k_InlineDataBudgetBytes)
                return new { included = true, bytes, value = token };

            if (token is JObject obj)
            {
                var selected = new JObject();
                foreach (string key in new[]
                {
                    "success", "passed", "scope", "entries", "payload", "savings", "bridge",
                    "tsamCoverageSummary", "failureClasses", "findings", "detailAvailable", "detailRef", "nextLine"
                })
                {
                    if (obj.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out var value))
                        selected[key] = value.DeepClone();
                }

                return new
                {
                    included = false,
                    bytes,
                    type = "object",
                    keys = obj.Properties().Select(property => property.Name).Take(16).ToArray(),
                    selected
                };
            }

            if (token is JArray array)
            {
                return new
                {
                    included = false,
                    bytes,
                    type = "array",
                    count = array.Count,
                    sample = array.Take(3).ToArray()
                };
            }

            return new
            {
                included = false,
                bytes,
                type = token.Type.ToString(),
                preview = token.ToString(Formatting.None).Substring(0, Math.Min(512, token.ToString(Formatting.None).Length))
            };
        }

        static object[] CollectDetailRefs(JToken token)
        {
            var refs = new List<object>();
            CollectDetailRefs(token, "$", refs);
            return refs.ToArray();
        }

        static void CollectDetailRefs(JToken token, string path, List<object> refs)
        {
            if (token == null || refs.Count >= 8)
                return;

            if (token is JObject obj)
            {
                foreach (var property in obj.Properties())
                {
                    var propertyPath = $"{path}.{property.Name}";
                    if (IsDetailRefProperty(property.Name))
                    {
                        refs.Add(BuildDetailRefSummary(propertyPath, property.Value));
                        if (refs.Count >= 8)
                            return;
                    }

                    CollectDetailRefs(property.Value, propertyPath, refs);
                    if (refs.Count >= 8)
                        return;
                }
            }
            else if (token is JArray array)
            {
                for (int i = 0; i < array.Count && refs.Count < 8; i++)
                    CollectDetailRefs(array[i], $"{path}[{i}]", refs);
            }
        }

        static bool IsDetailRefProperty(string name)
        {
            return string.Equals(name, "detailRef", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("DetailRef", StringComparison.OrdinalIgnoreCase);
        }

        static object BuildDetailRefSummary(string path, JToken value)
        {
            if (value is JObject obj)
            {
                return new
                {
                    path,
                    refId = obj["refId"]?.Value<string>() ?? obj["RefId"]?.Value<string>(),
                    tool = obj["tool"]?.Value<string>() ?? obj["Tool"]?.Value<string>(),
                    bytes = obj["bytes"]?.Value<int?>() ?? obj["Bytes"]?.Value<int?>(),
                    contentType = obj["contentType"]?.Value<string>() ?? obj["ContentType"]?.Value<string>()
                };
            }

            return new
            {
                path,
                refId = value?.Type == JTokenType.String ? value.Value<string>() : value?.ToString(Formatting.None)
            };
        }

        static bool StringArraysEqual(string[] left, string[] right)
        {
            left ??= Array.Empty<string>();
            right ??= Array.Empty<string>();
            if (left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }
    }
}
