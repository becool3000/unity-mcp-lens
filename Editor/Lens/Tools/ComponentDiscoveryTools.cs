#nullable disable
using System;
using System.Linq;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.Services.Components;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class ComponentDiscoveryTools
    {
        const string ComponentSearchToolName = "Unity.Component.Search";
        const string ResolveCapabilityToolName = "Unity.Component.ResolveCapability";
        const string InspectSchemaToolName = "Unity.Component.InspectSchema";
        const string SceneFindComponentsToolName = "Unity.Scene.FindComponents";
        const string SuggestReusePlanToolName = "Unity.Authoring.SuggestReusePlan";

        const string ComponentSearchDescription = @"Searches reusable Unity component surfaces before script generation.

Returns built-in components, installed package components, compiled project components, prefab candidates, preset candidates, and missing-package hints with confidence and schema availability.";

        const string ResolveCapabilityDescription = @"Resolves an authoring intent into existing Unity, project, package, prefab, preset, and missing-package reuse candidates.";

        const string InspectSchemaDescription = @"Inspects a component serialized schema before values are authored.

Can inspect an existing scene component or create a hidden unsaved temporary probe component for schema discovery.";

        const string SceneFindComponentsDescription = @"Finds existing loaded-scene components that already solve or partially solve a requested component/capability need.";

        const string SuggestReusePlanDescription = @"Suggests an authoring-first reuse plan before scripts.

The plan checks existing components, scene instances, prefabs, presets, installed packages, and missing package capabilities, and reports whether a new script appears necessary.";

        static readonly ComponentDiscoveryService Service = new ComponentDiscoveryService();

        [McpSchema(ComponentSearchToolName)]
        public static object GetComponentSearchSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Free-text component or capability query, for example 'follow camera', 'button', or 'NavMesh'." },
                    providers = ProviderFilterSchema(),
                    includeComponents = new { type = "boolean", description = "Include built-in, package, and project component types. Defaults to true." },
                    includePrefabs = new { type = "boolean", description = "Include prefab asset candidates. Defaults to true." },
                    includePresets = new { type = "boolean", description = "Include preset asset candidates. Defaults to true." },
                    includeMissingPackages = new { type = "boolean", description = "Include missing package capability hints. Defaults to true." },
                    maxResults = new { type = "integer", description = "Maximum results to return. Defaults to 30." },
                    maxAssetScans = new { type = "integer", description = "Maximum prefab/preset assets to inspect. Defaults to 120." }
                }
            };
        }

        [McpSchema(ResolveCapabilityToolName)]
        public static object GetResolveCapabilitySchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    intent = new { type = "string", description = "Authoring intent to resolve, for example 'follow camera'." },
                    context = new { type = "string", description = "Optional project or scene context for ranking notes." },
                    includePrefabs = new { type = "boolean", description = "Include prefab asset candidates. Defaults to true." },
                    includePresets = new { type = "boolean", description = "Include preset asset candidates. Defaults to true." },
                    includeMissingPackages = new { type = "boolean", description = "Include missing package capability hints. Defaults to true." },
                    maxResults = new { type = "integer", description = "Maximum ranked results to return. Defaults to 20." },
                    maxAssetScans = new { type = "integer", description = "Maximum prefab/preset assets to inspect. Defaults to 120." }
                },
                required = new[] { "intent" }
            };
        }

        [McpSchema(InspectSchemaToolName)]
        public static object GetInspectSchemaSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    componentName = new { type = "string", description = "Component short or fully qualified type name." },
                    target = new { description = "Optional scene GameObject name, path, or id whose existing component should be inspected." },
                    searchMethod = new { type = "string", description = "Target search method: by_name, by_path, by_id, or by_id_or_name_or_path. Defaults to by_name." },
                    includeInactive = new { type = "boolean", description = "Include inactive objects when resolving target. Defaults to true." },
                    componentIndex = new { type = "integer", description = "Component index when a target has multiple matching components. Defaults to 0." },
                    includeDefaults = new { type = "boolean", description = "Include current/default serialized values. Defaults to false." },
                    includeReadOnly = new { type = "boolean", description = "Include non-editable serialized properties. Defaults to false." },
                    maxFields = new { type = "integer", description = "Maximum serialized fields to return. Defaults to 120." }
                },
                required = new[] { "componentName" }
            };
        }

        [McpSchema(SceneFindComponentsToolName)]
        public static object GetSceneFindComponentsSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    componentName = new { type = "string", description = "Optional component short or fully qualified type name." },
                    query = new { type = "string", description = "Optional free-text component query." },
                    intent = new { type = "string", description = "Optional authoring intent, for example 'follow camera'." },
                    scene = new { type = "string", description = "Optional loaded scene name or path." },
                    includeInactive = new { type = "boolean", description = "Include inactive scene objects. Defaults to false." },
                    maxResults = new { type = "integer", description = "Maximum matching components to return. Defaults to 50." },
                    propertyPaths = new
                    {
                        type = "array",
                        description = "Optional serialized property paths to read from each matched component.",
                        items = new { type = "string" }
                    }
                }
            };
        }

        [McpSchema(SuggestReusePlanToolName)]
        public static object GetSuggestReusePlanSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    intent = new { type = "string", description = "Requested feature or authoring intent." },
                    context = new { type = "string", description = "Optional project/scene context or constraints." },
                    includeSceneSearch = new { type = "boolean", description = "Search loaded scenes for existing partial solutions. Defaults to true." },
                    includePrefabs = new { type = "boolean", description = "Include prefab reuse candidates. Defaults to true." },
                    includePresets = new { type = "boolean", description = "Include preset reuse candidates. Defaults to true." },
                    includeMissingPackages = new { type = "boolean", description = "Include package-backed unavailable solutions. Defaults to true." },
                    maxResults = new { type = "integer", description = "Maximum candidates to include. Defaults to 12." }
                },
                required = new[] { "intent" }
            };
        }

        [McpTool(ComponentSearchToolName, ComponentSearchDescription, "Search Components", Groups = new[] { "project" }, EnabledByDefault = true)]
        public static object Search(JObject @params)
        {
            return Handle(
                ComponentSearchToolName,
                "component_search",
                @params,
                NormalizeSearchRequest,
                request => Service.Search(request),
                "Component search completed.");
        }

        [McpTool(ResolveCapabilityToolName, ResolveCapabilityDescription, "Resolve Component Capability", Groups = new[] { "project" }, EnabledByDefault = true)]
        public static object ResolveCapability(JObject @params)
        {
            return Handle(
                ResolveCapabilityToolName,
                "resolve_capability",
                @params,
                NormalizeResolveCapabilityRequest,
                request => Service.ResolveCapability(request),
                "Component capability resolution completed.");
        }

        [McpTool(InspectSchemaToolName, InspectSchemaDescription, "Inspect Component Schema", Groups = new[] { "project" }, EnabledByDefault = true)]
        public static object InspectSchema(JObject @params)
        {
            return Handle(
                InspectSchemaToolName,
                "inspect_component_schema",
                @params,
                NormalizeInspectSchemaRequest,
                request => Service.InspectSchema(request),
                "Component serialized schema inspection completed.");
        }

        [McpTool(SceneFindComponentsToolName, SceneFindComponentsDescription, "Find Scene Components", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object FindSceneComponents(JObject @params)
        {
            return Handle(
                SceneFindComponentsToolName,
                "find_scene_components",
                @params,
                NormalizeSceneFindComponentsRequest,
                request => Service.FindSceneComponents(request),
                "Scene component search completed.");
        }

        [McpTool(SuggestReusePlanToolName, SuggestReusePlanDescription, "Suggest Authoring Reuse Plan", Groups = new[] { "project" }, EnabledByDefault = true)]
        public static object SuggestReusePlan(JObject @params)
        {
            return Handle(
                SuggestReusePlanToolName,
                "suggest_reuse_plan",
                @params,
                NormalizeSuggestReusePlanRequest,
                request => Service.SuggestReusePlan(request),
                "Authoring reuse plan completed.");
        }

        static object Handle<TRequest>(
            string toolName,
            string operation,
            JObject parameters,
            Func<JObject, TRequest> normalize,
            Func<TRequest, object> execute,
            string successMessage)
        {
            parameters ??= new JObject();
            var timing = new ToolOperationTiming(toolName, operation, PayloadBudgeting.GetUtf8ByteCount(parameters.ToString(Formatting.None)));
            bool success = true;
            string errorKind = null;
            object data;

            try
            {
                TRequest request;
                using (timing.Measure("normalization"))
                {
                    request = normalize(parameters);
                }

                using (timing.Measure("service"))
                {
                    data = execute(request);
                }

                using (timing.Measure("adapter"))
                {
                }
            }
            catch (Exception ex)
            {
                success = false;
                errorKind = ex.GetType().Name;
                data = new
                {
                    status = "failed",
                    errorKind,
                    error = ex.Message
                };
            }

            object response;
            using (timing.Measure("result_shaping"))
            {
                response = success
                    ? Response.Success(successMessage, ToolResultCompactor.ShapeStructuredPayload(
                        toolName,
                        data,
                        BuildCompactData(data),
                        new { kind = "component_discovery_full_result" },
                        "component_discovery",
                        detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes))
                    : Response.Error("COMPONENT_DISCOVERY_FAILED", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, success ? null : errorKind);
            return response;
        }

        static ComponentSearchRequest NormalizeSearchRequest(JObject parameters)
        {
            return new ComponentSearchRequest
            {
                query = GetString(parameters, "query", "Query", "intent", "Intent", "capability", "Capability"),
                providers = GetStringArray(parameters, "providers", "Providers", "provider", "Provider"),
                includeComponents = GetBool(parameters, true, "includeComponents", "IncludeComponents"),
                includePrefabs = GetBool(parameters, true, "includePrefabs", "IncludePrefabs"),
                includePresets = GetBool(parameters, true, "includePresets", "IncludePresets"),
                includeMissingPackages = GetBool(parameters, true, "includeMissingPackages", "IncludeMissingPackages"),
                maxResults = GetInt(parameters, 30, "maxResults", "MaxResults", "limit", "Limit"),
                maxAssetScans = GetInt(parameters, 120, "maxAssetScans", "MaxAssetScans")
            };
        }

        static ComponentResolveCapabilityRequest NormalizeResolveCapabilityRequest(JObject parameters)
        {
            return new ComponentResolveCapabilityRequest
            {
                intent = GetString(parameters, "intent", "Intent", "query", "Query"),
                context = GetString(parameters, "context", "Context"),
                includePrefabs = GetBool(parameters, true, "includePrefabs", "IncludePrefabs"),
                includePresets = GetBool(parameters, true, "includePresets", "IncludePresets"),
                includeMissingPackages = GetBool(parameters, true, "includeMissingPackages", "IncludeMissingPackages"),
                maxResults = GetInt(parameters, 20, "maxResults", "MaxResults", "limit", "Limit"),
                maxAssetScans = GetInt(parameters, 120, "maxAssetScans", "MaxAssetScans")
            };
        }

        static ComponentInspectSchemaRequest NormalizeInspectSchemaRequest(JObject parameters)
        {
            return new ComponentInspectSchemaRequest
            {
                componentName = GetString(parameters, "componentName", "ComponentName", "component", "Component", "typeName", "TypeName"),
                target = GetString(parameters, "target", "Target"),
                searchMethod = GetString(parameters, "searchMethod", "SearchMethod") ?? "by_name",
                includeInactive = GetBool(parameters, true, "includeInactive", "IncludeInactive"),
                componentIndex = Math.Max(0, GetInt(parameters, 0, "componentIndex", "ComponentIndex")),
                includeDefaults = GetBool(parameters, false, "includeDefaults", "IncludeDefaults"),
                includeReadOnly = GetBool(parameters, false, "includeReadOnly", "IncludeReadOnly"),
                maxFields = GetInt(parameters, 120, "maxFields", "MaxFields")
            };
        }

        static SceneFindComponentsRequest NormalizeSceneFindComponentsRequest(JObject parameters)
        {
            return new SceneFindComponentsRequest
            {
                componentName = GetString(parameters, "componentName", "ComponentName", "component", "Component", "typeName", "TypeName"),
                query = GetString(parameters, "query", "Query"),
                intent = GetString(parameters, "intent", "Intent", "capability", "Capability"),
                scene = GetString(parameters, "scene", "Scene"),
                includeInactive = GetBool(parameters, false, "includeInactive", "IncludeInactive"),
                maxResults = GetInt(parameters, 50, "maxResults", "MaxResults", "limit", "Limit"),
                propertyPaths = GetStringArray(parameters, "propertyPaths", "PropertyPaths", "fields", "Fields")
            };
        }

        static AuthoringSuggestReusePlanRequest NormalizeSuggestReusePlanRequest(JObject parameters)
        {
            return new AuthoringSuggestReusePlanRequest
            {
                intent = GetString(parameters, "intent", "Intent", "query", "Query", "feature", "Feature"),
                context = GetString(parameters, "context", "Context"),
                includeSceneSearch = GetBool(parameters, true, "includeSceneSearch", "IncludeSceneSearch"),
                includePrefabs = GetBool(parameters, true, "includePrefabs", "IncludePrefabs"),
                includePresets = GetBool(parameters, true, "includePresets", "IncludePresets"),
                includeMissingPackages = GetBool(parameters, true, "includeMissingPackages", "IncludeMissingPackages"),
                maxResults = GetInt(parameters, 12, "maxResults", "MaxResults", "limit", "Limit")
            };
        }

        static object ProviderFilterSchema()
        {
            return new
            {
                type = "array",
                description = "Optional provider filters: built-in, installed package, project script, prefab, preset, missing package.",
                items = new
                {
                    type = "string",
                    @enum = new[] { "built-in", "installed package", "project script", "prefab", "preset", "missing package" }
                }
            };
        }

        static object BuildCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            CompactArray(root, "results", 25);
            CompactArray(root, "fields", 60);
            CompactArray(root, "matches", 30);
            CompactArray(root, "capabilityResults", 20);
            CompactArray(root, "reusePlan", 12);

            if (root["sceneFind"] is JObject sceneFind)
                CompactArray(sceneFind, "matches", 20);

            return root;
        }

        static void CompactArray(JObject root, string propertyName, int maxItems)
        {
            if (root == null || root[propertyName] is not JArray rows || rows.Count <= maxItems)
                return;

            int omitted = rows.Count - maxItems;
            root[propertyName] = new JArray(rows.Take(maxItems).Select(row => row.DeepClone()));
            root[$"compactOmitted{char.ToUpperInvariant(propertyName[0])}{propertyName.Substring(1)}Count"] = omitted;
        }

        static JToken GetToken(JObject parameters, params string[] names)
        {
            if (parameters == null)
                return null;

            foreach (string name in names)
            {
                if (parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken value))
                    return value;
            }

            return null;
        }

        static string GetString(JObject parameters, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            return token == null || token.Type == JTokenType.Null ? null : token.ToString().Trim();
        }

        static bool GetBool(JObject parameters, bool defaultValue, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            if (token == null || token.Type == JTokenType.Null)
                return defaultValue;

            if (token.Type == JTokenType.Boolean)
                return token.Value<bool>();

            return bool.TryParse(token.ToString(), out bool value) ? value : defaultValue;
        }

        static int GetInt(JObject parameters, int defaultValue, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            if (token == null || token.Type == JTokenType.Null)
                return defaultValue;

            return int.TryParse(token.ToString(), out int value) ? value : defaultValue;
        }

        static string[] GetStringArray(JObject parameters, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            if (token == null || token.Type == JTokenType.Null)
                return Array.Empty<string>();

            if (token is JArray array)
            {
                return array
                    .Select(item => item?.ToString()?.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();
            }

            string single = token.ToString()?.Trim();
            return string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : new[] { single };
        }
    }
}
