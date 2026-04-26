#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.Adapters.Unity.UI;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Models.UI;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.Services.UI;
using Becool.UnityMcpLens.Editor.ToolRegistry;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class UiSplitTools
    {
        const string PreviewEnsureHierarchyToolName = "Unity.UI.PreviewEnsureHierarchy";
        const string ApplyEnsureHierarchyToolName = "Unity.UI.ApplyEnsureHierarchy";
        const string PreviewLayoutPropertiesToolName = "Unity.UI.PreviewLayoutProperties";
        const string ApplyLayoutPropertiesToolName = "Unity.UI.ApplyLayoutProperties";
        const string VerifyScreenLayoutToolName = "Unity.UI.VerifyScreenLayout";

        const string PreviewEnsureHierarchyDescription = @"Previews persistent UI hierarchy authoring without mutating or saving scenes.

Uses the same target/search semantics as the earlier ensure hierarchy tool, but returns deterministic create, recreate, update, and preserve rows.";

        const string ApplyEnsureHierarchyDescription = @"Applies persistent UI hierarchy authoring and saves open scenes when changes are required.

Uses the same target/search semantics as the earlier ensure hierarchy tool, with per-node component and layout support.";

        const string PreviewLayoutPropertiesDescription = @"Previews authored UI RectTransform and display-property changes without mutating or saving scenes.";

        const string ApplyLayoutPropertiesDescription = @"Applies authored UI RectTransform and display-property changes and saves open scenes when changes are required.";

        const string VerifyScreenLayoutDescription = @"Verifies measured screen-space UI layout assertions without mutation.

Supports inside-screen, relative-position, axis-alignment, and ordered-stack assertions over keyed UI targets. Relative-position keeps strict rect relations (`right_of`, `left_of`, `above`, `below`) and also supports center-based comparisons (`right_of_center`, `left_of_center`, `above_center`, `below_center`).";

        static readonly UnityUiAuthoringAdapter Adapter = new UnityUiAuthoringAdapter();
        static readonly UiAuthoringService Service = new UiAuthoringService(Adapter);

        [McpSchema(PreviewEnsureHierarchyToolName)]
        public static object GetPreviewEnsureHierarchySchema()
        {
            return BuildEnsureHierarchySchema();
        }

        [McpSchema(ApplyEnsureHierarchyToolName)]
        public static object GetApplyEnsureHierarchySchema()
        {
            return BuildEnsureHierarchySchema();
        }

        [McpSchema(PreviewLayoutPropertiesToolName)]
        public static object GetPreviewLayoutPropertiesSchema()
        {
            return BuildLayoutPropertiesSchema();
        }

        [McpSchema(ApplyLayoutPropertiesToolName)]
        public static object GetApplyLayoutPropertiesSchema()
        {
            return BuildLayoutPropertiesSchema();
        }

        [McpSchema(VerifyScreenLayoutToolName)]
        public static object GetVerifyScreenLayoutSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    targets = new
                    {
                        type = "array",
                        description = "Keyed UI targets to resolve and measure.",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                key = new { type = "string", description = "Stable key used by assertions." },
                                target = new { description = "Scene GameObject, path, or instance id to resolve." },
                                searchMethod = new { type = "string", description = "How to resolve the target ('by_name', 'by_id', 'by_path')." },
                                targetPath = new { type = "string", description = "Optional relative child path under the root target." },
                                includeInactive = new { type = "boolean", description = "Include inactive scene objects when resolving the target." }
                            },
                            required = new[] { "key", "target" }
                        }
                    },
                    assertions = new
                    {
                        type = "array",
                        description = "Layout assertions to evaluate over the keyed targets. relative_position supports strict rect relations (`right_of`, `left_of`, `above`, `below`) plus center-based relations (`right_of_center`, `left_of_center`, `above_center`, `below_center`).",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                type = new { type = "string", description = "Assertion type: inside_screen, relative_position, axis_alignment, or ordered_stack." },
                                targetKey = new { type = "string", description = "Primary target key for inside_screen, relative_position, and axis_alignment." },
                                otherTargetKey = new { type = "string", description = "Secondary target key for relative_position and axis_alignment." },
                                targetKeys = new { type = "array", description = "Ordered keys for ordered_stack assertions." },
                                relation = new { type = "string", description = "For relative_position: right_of, left_of, above, below, right_of_center, left_of_center, above_center, or below_center." },
                                axis = new { type = "string", description = "For axis_alignment: horizontal_center, vertical_center, left, right, top, or bottom." },
                                edge = new { type = "string", description = "Alias for axis in axis_alignment assertions." },
                                direction = new { type = "string", description = "For ordered_stack: top_to_bottom, bottom_to_top, left_to_right, or right_to_left." },
                                tolerance = new { type = "number", description = "Allowed delta for relative_position, axis_alignment, or ordered_stack checks." },
                                margin = new { type = "number", description = "Screen margin for inside_screen checks." }
                            },
                            required = new[] { "type" }
                        }
                    }
                },
                required = new[] { "targets", "assertions" }
            };
        }

        [McpTool(PreviewEnsureHierarchyToolName, PreviewEnsureHierarchyDescription, "Preview Ensure UI Hierarchy", Groups = new[] { "ui" }, EnabledByDefault = true)]
        public static object PreviewEnsureHierarchy(JObject @params)
        {
            return HandleEnsureHierarchyTool(PreviewEnsureHierarchyToolName, "preview_ensure_hierarchy", @params, apply: false);
        }

        [McpTool(ApplyEnsureHierarchyToolName, ApplyEnsureHierarchyDescription, "Apply Ensure UI Hierarchy", Groups = new[] { "ui" }, EnabledByDefault = true)]
        public static object ApplyEnsureHierarchy(JObject @params)
        {
            return HandleEnsureHierarchyTool(ApplyEnsureHierarchyToolName, "apply_ensure_hierarchy", @params, apply: true);
        }

        [McpTool(PreviewLayoutPropertiesToolName, PreviewLayoutPropertiesDescription, "Preview UI Layout Properties", Groups = new[] { "ui" }, EnabledByDefault = true)]
        public static object PreviewLayoutProperties(JObject @params)
        {
            return HandleLayoutPropertiesTool(PreviewLayoutPropertiesToolName, "preview_layout_properties", @params, apply: false);
        }

        [McpTool(ApplyLayoutPropertiesToolName, ApplyLayoutPropertiesDescription, "Apply UI Layout Properties", Groups = new[] { "ui" }, EnabledByDefault = true)]
        public static object ApplyLayoutProperties(JObject @params)
        {
            return HandleLayoutPropertiesTool(ApplyLayoutPropertiesToolName, "apply_layout_properties", @params, apply: true);
        }

        [McpTool(VerifyScreenLayoutToolName, VerifyScreenLayoutDescription, "Verify UI Screen Layout", Groups = new[] { "ui" }, EnabledByDefault = true)]
        public static object VerifyScreenLayout(JObject @params)
        {
            @params ??= new JObject();
            var timing = new ToolOperationTiming(VerifyScreenLayoutToolName, "verify_screen_layout", GetUtf8ByteCount(@params.ToString(Formatting.None)));
            UiOperationResult result;
            string errorKind = null;

            try
            {
                UiVerifyScreenLayoutRequest request;
                using (timing.Measure("normalization"))
                {
                    request = NormalizeVerifyScreenLayoutRequest(@params);
                }

                using (timing.Measure("service"))
                {
                    result = Service.VerifyScreenLayout(request, timing);
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                result = UiOperationResult.Error($"Internal error verifying UI screen layout: {ex.Message}", errorKind);
            }

            return ShapeResponse(VerifyScreenLayoutToolName, result, timing, errorKind);
        }

        static object HandleEnsureHierarchyTool(string toolName, string action, JObject @params, bool apply)
        {
            @params ??= new JObject();
            var timing = new ToolOperationTiming(toolName, action, GetUtf8ByteCount(@params.ToString(Formatting.None)));
            UiOperationResult result;
            string errorKind = null;

            try
            {
                UiEnsureHierarchyRequest request;
                using (timing.Measure("normalization"))
                {
                    request = NormalizeEnsureHierarchyRequest(@params);
                }

                using (timing.Measure("service"))
                {
                    result = apply
                        ? Service.ApplyEnsureHierarchy(request, timing)
                        : Service.PreviewEnsureHierarchy(request, timing);
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                result = UiOperationResult.Error($"Internal error processing UI hierarchy request: {ex.Message}", errorKind);
            }

            return ShapeResponse(toolName, result, timing, errorKind);
        }

        static object HandleLayoutPropertiesTool(string toolName, string action, JObject @params, bool apply)
        {
            @params ??= new JObject();
            var timing = new ToolOperationTiming(toolName, action, GetUtf8ByteCount(@params.ToString(Formatting.None)));
            UiOperationResult result;
            string errorKind = null;

            try
            {
                UiLayoutPropertiesRequest request;
                using (timing.Measure("normalization"))
                {
                    request = NormalizeLayoutPropertiesRequest(@params);
                }

                using (timing.Measure("service"))
                {
                    result = apply
                        ? Service.ApplyLayoutProperties(request, timing)
                        : Service.PreviewLayoutProperties(request, timing);
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                result = UiOperationResult.Error($"Internal error processing UI layout properties request: {ex.Message}", errorKind);
            }

            return ShapeResponse(toolName, result, timing, errorKind);
        }

        static object BuildEnsureHierarchySchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    target = new { description = "Scene GameObject, path, or instance id to use as the root parent." },
                    searchMethod = new { type = "string", description = "How to find the target root ('by_name', 'by_id', 'by_path')." },
                    includeInactive = new { type = "boolean", description = "Include inactive scene objects when resolving the root target." },
                    nodes = new { type = "array", description = "Named UI node specs to ensure under the root target." }
                },
                required = new[] { "target", "nodes" }
            };
        }

        static object BuildLayoutPropertiesSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    target = new { type = "string", description = "Scene GameObject, path, or instance id to edit." },
                    searchMethod = new { type = "string", description = "How to find the target ('by_name', 'by_id', 'by_path')." },
                    targetPath = new { type = "string", description = "Relative child path under the target root. Use '.' or omit for the root GameObject." },
                    includeInactive = new { type = "boolean", description = "Include inactive scene objects when resolving the target." },
                    anchorMin = new { description = "RectTransform anchorMin value, as {x,y} or [x,y]." },
                    anchorMax = new { description = "RectTransform anchorMax value, as {x,y} or [x,y]." },
                    pivot = new { description = "RectTransform pivot value, as {x,y} or [x,y]." },
                    sizeDelta = new { description = "RectTransform sizeDelta value, as {x,y} or [x,y]." },
                    anchoredPosition = new { description = "RectTransform anchoredPosition value, as {x,y} or [x,y]." },
                    siblingIndex = new { type = "integer", description = "Sibling index to set on the target transform." },
                    active = new { type = "boolean", description = "Set the target GameObject active state." },
                    canvasGroupAlpha = new { type = "number", description = "CanvasGroup alpha value." },
                    canvasGroupInteractable = new { type = "boolean", description = "CanvasGroup interactable flag." },
                    canvasGroupBlocksRaycasts = new { type = "boolean", description = "CanvasGroup blocksRaycasts flag." },
                    imageSpritePath = new { type = "string", description = "Sprite asset path to assign to an Image component." },
                    imageColor = new { description = "Image color as {r,g,b,a} or [r,g,b,a]." },
                    text = new { type = "string", description = "Text content for a UI Text or TMP_Text component." },
                    textColor = new { description = "Text color as {r,g,b,a} or [r,g,b,a]." },
                    buttonInteractable = new { type = "boolean", description = "Button interactable flag." }
                },
                required = new[] { "target" }
            };
        }

        static UiEnsureHierarchyRequest NormalizeEnsureHierarchyRequest(JObject parameters)
        {
            return new UiEnsureHierarchyRequest
            {
                Target = GetToken(parameters, "target", "Target"),
                SearchMethod = GetString(parameters, "searchMethod", "SearchMethod") ?? "by_name",
                IncludeInactive = GetBool(parameters, true, "includeInactive", "IncludeInactive"),
                Nodes = UiAuthoringTools.ParseRootNodeSpecs(GetToken(parameters, "nodes", "Nodes")).ToArray()
            };
        }

        static UiLayoutPropertiesRequest NormalizeLayoutPropertiesRequest(JObject parameters)
        {
            return new UiLayoutPropertiesRequest
            {
                Target = GetString(parameters, "target", "Target"),
                SearchMethod = GetString(parameters, "searchMethod", "SearchMethod") ?? "by_name",
                TargetPath = GetString(parameters, "targetPath", "TargetPath") ?? ".",
                IncludeInactive = GetBool(parameters, true, "includeInactive", "IncludeInactive"),
                Layout = new Parameters.UiNodeLayoutSpec
                {
                    AnchorMin = GetToken(parameters, "anchorMin", "AnchorMin"),
                    AnchorMax = GetToken(parameters, "anchorMax", "AnchorMax"),
                    Pivot = GetToken(parameters, "pivot", "Pivot"),
                    SizeDelta = GetToken(parameters, "sizeDelta", "SizeDelta"),
                    AnchoredPosition = GetToken(parameters, "anchoredPosition", "AnchoredPosition"),
                    SiblingIndex = GetNullableInt(parameters, "siblingIndex", "SiblingIndex"),
                    Active = GetNullableBool(parameters, "active", "Active"),
                    CanvasGroupAlpha = GetNullableFloat(parameters, "canvasGroupAlpha", "CanvasGroupAlpha"),
                    CanvasGroupInteractable = GetNullableBool(parameters, "canvasGroupInteractable", "CanvasGroupInteractable"),
                    CanvasGroupBlocksRaycasts = GetNullableBool(parameters, "canvasGroupBlocksRaycasts", "CanvasGroupBlocksRaycasts"),
                    ImageSpritePath = GetString(parameters, "imageSpritePath", "ImageSpritePath"),
                    ImageColor = GetToken(parameters, "imageColor", "ImageColor"),
                    Text = GetString(parameters, "text", "Text"),
                    TextColor = GetToken(parameters, "textColor", "TextColor"),
                    ButtonInteractable = GetNullableBool(parameters, "buttonInteractable", "ButtonInteractable")
                }
            };
        }

        static UiVerifyScreenLayoutRequest NormalizeVerifyScreenLayoutRequest(JObject parameters)
        {
            return new UiVerifyScreenLayoutRequest
            {
                Targets = GetToken(parameters, "targets", "Targets")?.ToObject<UiVerifyTargetRequest[]>() ?? Array.Empty<UiVerifyTargetRequest>(),
                Assertions = GetToken(parameters, "assertions", "Assertions")?.ToObject<UiVerifyAssertionRequest[]>() ?? Array.Empty<UiVerifyAssertionRequest>()
            };
        }

        static object ShapeResponse(string toolName, UiOperationResult result, ToolOperationTiming timing, string fallbackErrorKind)
        {
            object response;
            using (timing.Measure("result_shaping"))
            {
                response = result.success
                    ? Response.Success(result.message, ShapeSuccessData(toolName, result.data))
                    : Response.Error(result.message, result.errorData ?? new { errorKind = result.errorKind ?? fallbackErrorKind });

                timing.SetResponseBytes(GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(result.success, result.success ? null : result.errorKind ?? fallbackErrorKind);
            return response;
        }

        static object ShapeSuccessData(string toolName, object data)
        {
            if (string.Equals(toolName, PreviewEnsureHierarchyToolName, StringComparison.Ordinal) ||
                string.Equals(toolName, ApplyEnsureHierarchyToolName, StringComparison.Ordinal))
            {
                return ToolResultCompactor.ShapeStructuredPayload(
                    toolName,
                    data,
                    BuildEnsureHierarchyCompactData(data),
                    detailRefMeta: new { kind = "ui_ensure_hierarchy_full_result" },
                    payloadClass: "ui_ensure_hierarchy");
            }

            if (string.Equals(toolName, VerifyScreenLayoutToolName, StringComparison.Ordinal))
            {
                return ToolResultCompactor.ShapeStructuredPayload(
                    toolName,
                    data,
                    BuildVerifyScreenLayoutCompactData(data),
                    detailRefMeta: new { kind = "ui_verify_screen_layout_full_result" },
                    payloadClass: "ui_verify_screen_layout");
            }

            return ToolResultCompactor.ShapeJsonPayload(toolName, "UI operation completed.", data);
        }

        static object BuildEnsureHierarchyCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            JArray nodes = root["nodes"] as JArray ?? new JArray();
            var actionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var changedNodes = new JArray();
            int preservedCount = 0;

            foreach (JObject node in nodes.OfType<JObject>())
            {
                string action = (string)node["action"] ?? "unknown";
                actionCounts[action] = actionCounts.TryGetValue(action, out int count) ? count + 1 : 1;

                bool hasChanges = node["changes"] is JArray changes && changes.Count > 0;
                if (string.Equals(action, "preserve", StringComparison.OrdinalIgnoreCase) && !hasChanges)
                {
                    preservedCount++;
                    continue;
                }

                changedNodes.Add(new JObject
                {
                    ["path"] = node["path"]?.DeepClone(),
                    ["action"] = action,
                    ["existed"] = node["existed"]?.DeepClone(),
                    ["componentTypes"] = node["componentTypes"]?.DeepClone(),
                    ["changes"] = node["changes"]?.DeepClone() ?? new JArray()
                });
            }

            return new
            {
                target = root["target"],
                applied = root["applied"],
                willModify = root["willModify"],
                nodeCount = nodes.Count,
                actionCounts,
                changedNodeCount = changedNodes.Count,
                omittedPreservedNodeCount = preservedCount,
                changedNodes
            };
        }

        static object BuildVerifyScreenLayoutCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            JArray targets = root["targets"] as JArray ?? new JArray();
            JArray assertions = root["assertions"] as JArray ?? new JArray();
            var compactTargets = new JObject();
            var compactAssertions = new JArray();
            var failedAssertions = new JArray();

            foreach (JObject target in targets.OfType<JObject>())
            {
                string key = (string)target["key"];
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                compactTargets[key] = new JObject
                {
                    ["path"] = target["path"]?.DeepClone(),
                    ["canvasPath"] = target["canvasPath"]?.DeepClone(),
                    ["activeInHierarchy"] = target["activeInHierarchy"]?.DeepClone(),
                    ["screenRect"] = target["screenRect"]?.DeepClone()
                };
            }

            foreach (JObject assertion in assertions.OfType<JObject>())
            {
                var compactAssertion = new JObject
                {
                    ["type"] = assertion["type"]?.DeepClone(),
                    ["targetKey"] = assertion["targetKey"]?.DeepClone(),
                    ["otherTargetKey"] = assertion["otherTargetKey"]?.DeepClone(),
                    ["relation"] = assertion["relation"]?.DeepClone(),
                    ["axis"] = assertion["axis"]?.DeepClone(),
                    ["direction"] = assertion["direction"]?.DeepClone(),
                    ["targetKeys"] = assertion["targetKeys"]?.DeepClone(),
                    ["passed"] = assertion["passed"]?.DeepClone(),
                    ["actual"] = assertion["actual"]?.DeepClone(),
                    ["message"] = assertion["message"]?.DeepClone()
                };
                compactAssertions.Add(compactAssertion);
                if (assertion["passed"]?.Value<bool>() == false)
                    failedAssertions.Add(compactAssertion.DeepClone());
            }

            return new
            {
                passed = root["passed"],
                screen = root["screen"],
                targetCount = targets.Count,
                assertionCount = assertions.Count,
                failedAssertionCount = failedAssertions.Count,
                targets = compactTargets,
                assertions = compactAssertions,
                failedAssertions
            };
        }

        static string GetString(JObject parameters, params string[] names)
        {
            foreach (string name in names)
            {
                if (parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token))
                    return token?.Type == JTokenType.Null ? null : token?.ToString();
            }

            return null;
        }

        static JToken GetToken(JObject parameters, params string[] names)
        {
            foreach (string name in names)
            {
                if (parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token))
                    return token;
            }

            return null;
        }

        static bool GetBool(JObject parameters, bool defaultValue, params string[] names)
        {
            foreach (string name in names)
            {
                if (parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token))
                    return token.Type == JTokenType.Boolean ? token.Value<bool>() : bool.TryParse(token.ToString(), out bool parsed) ? parsed : defaultValue;
            }

            return defaultValue;
        }

        static bool? GetNullableBool(JObject parameters, params string[] names)
        {
            foreach (string name in names)
            {
                if (parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token))
                {
                    if (token.Type == JTokenType.Null)
                        return null;
                    if (token.Type == JTokenType.Boolean)
                        return token.Value<bool>();
                    if (bool.TryParse(token.ToString(), out bool parsed))
                        return parsed;
                }
            }

            return null;
        }

        static int? GetNullableInt(JObject parameters, params string[] names)
        {
            foreach (string name in names)
            {
                if (parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token))
                {
                    if (token.Type == JTokenType.Null)
                        return null;
                    if (token.Type == JTokenType.Integer)
                        return token.Value<int>();
                    if (int.TryParse(token.ToString(), out int parsed))
                        return parsed;
                }
            }

            return null;
        }

        static float? GetNullableFloat(JObject parameters, params string[] names)
        {
            foreach (string name in names)
            {
                if (parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token))
                {
                    if (token.Type == JTokenType.Null)
                        return null;
                    if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                        return token.Value<float>();
                    if (float.TryParse(token.ToString(), out float parsed))
                        return parsed;
                }
            }

            return null;
        }

        static int GetUtf8ByteCount(string value) => Encoding.UTF8.GetByteCount(value ?? string.Empty);
    }
}
