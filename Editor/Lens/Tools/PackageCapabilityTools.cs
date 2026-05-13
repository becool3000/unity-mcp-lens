#nullable disable
using System;
using System.Linq;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Services.Components;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class PackageCapabilityTools
    {
        const string ResolveCapabilityToolName = "Unity.Package.ResolveCapability";
        const string PreviewInstallForCapabilityToolName = "Unity.Package.PreviewInstallForCapability";

        const string ResolveCapabilityDescription = @"Resolves a requested Unity capability to package-backed solutions without mutation.

Reports installed packages, built-in module availability, missing package requirements, compatibility notes, compile/import impact, install risk, fallback plan, and whether a package install appears necessary.";

        const string PreviewInstallForCapabilityDescription = @"Previews a package install plan for a requested capability without mutating Package Manager state.

This tool never installs packages. It returns the explicit package mutation tool/action to use only after user approval.";

        static readonly ComponentDiscoveryService Service = new ComponentDiscoveryService();

        [McpSchema(ResolveCapabilityToolName)]
        public static object GetResolveCapabilitySchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    intent = new { type = "string", description = "Capability intent, for example 'follow camera', 'input system', 'TextMeshPro', 'URP', 'UI Toolkit', or 'NavMesh'." },
                    context = new { type = "string", description = "Optional project or scene context for ranking and notes." },
                    includeInstalled = new { type = "boolean", description = "Include package capabilities that are already installed or otherwise available. Defaults to true." },
                    includeMissing = new { type = "boolean", description = "Include package capabilities that would require installation or are unavailable. Defaults to true." },
                    maxResults = new { type = "integer", description = "Maximum package capability rows to return. Defaults to 12." }
                },
                required = new[] { "intent" }
            };
        }

        [McpSchema(PreviewInstallForCapabilityToolName)]
        public static object GetPreviewInstallForCapabilitySchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    intent = new { type = "string", description = "Capability intent to install for, for example 'follow camera' or 'NavMesh'." },
                    context = new { type = "string", description = "Optional project or scene context for warnings." },
                    packageId = new { type = "string", description = "Optional exact package id to preview, for example 'com.unity.cinemachine'." },
                    version = new { type = "string", description = "Optional explicit version. Omit to use the recommended project-compatible version." },
                    includeFallbackPlan = new { type = "boolean", description = "Include fallback plan text in the preview result. Defaults to true." }
                }
            };
        }

        [McpTool(ResolveCapabilityToolName, ResolveCapabilityDescription, "Resolve Package Capability", Groups = new[] { "project" }, EnabledByDefault = true)]
        public static object ResolveCapability(JObject @params)
        {
            return Handle(
                ResolveCapabilityToolName,
                "package_resolve_capability",
                @params,
                NormalizeResolveCapabilityRequest,
                request => Service.ResolvePackageCapability(request),
                "Package capability resolution completed.");
        }

        [McpTool(PreviewInstallForCapabilityToolName, PreviewInstallForCapabilityDescription, "Preview Package Install For Capability", Groups = new[] { "project" }, EnabledByDefault = true)]
        public static object PreviewInstallForCapability(JObject @params)
        {
            return Handle(
                PreviewInstallForCapabilityToolName,
                "package_preview_install_for_capability",
                @params,
                NormalizePreviewInstallRequest,
                request => Service.PreviewInstallForCapability(request),
                "Package install preview completed.");
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
                        new { kind = "package_capability_full_result" },
                        "package_capability",
                        detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes))
                    : Response.Error("PACKAGE_CAPABILITY_FAILED", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, success ? null : errorKind);
            return response;
        }

        static PackageResolveCapabilityRequest NormalizeResolveCapabilityRequest(JObject parameters)
        {
            return new PackageResolveCapabilityRequest
            {
                intent = GetString(parameters, "intent", "Intent", "query", "Query", "capability", "Capability"),
                context = GetString(parameters, "context", "Context"),
                includeInstalled = GetBool(parameters, true, "includeInstalled", "IncludeInstalled"),
                includeMissing = GetBool(parameters, true, "includeMissing", "IncludeMissing", "includeMissingPackages", "IncludeMissingPackages"),
                maxResults = GetInt(parameters, 12, "maxResults", "MaxResults", "limit", "Limit")
            };
        }

        static PackagePreviewInstallForCapabilityRequest NormalizePreviewInstallRequest(JObject parameters)
        {
            return new PackagePreviewInstallForCapabilityRequest
            {
                intent = GetString(parameters, "intent", "Intent", "query", "Query", "capability", "Capability"),
                context = GetString(parameters, "context", "Context"),
                packageId = GetString(parameters, "packageId", "PackageId", "package", "Package"),
                version = GetString(parameters, "version", "Version", "recommendedVersion", "RecommendedVersion"),
                includeFallbackPlan = GetBool(parameters, true, "includeFallbackPlan", "IncludeFallbackPlan")
            };
        }

        static object BuildCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            CompactArray(root, "packageCapabilities", 20);
            CompactArray(root, "warnings", 12);
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
    }
}
