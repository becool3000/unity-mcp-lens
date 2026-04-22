#nullable disable
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.ToolRegistry;

namespace Becool.UnityMcpLens.Editor.Lens
{
    static class ToolMetadataPolicy
    {
        static string NormalizeToolName(string toolName) => McpToolRegistry.NormalizeToolName(toolName) ?? string.Empty;

        static string NormalizeToolPrefix(string toolPrefix) => McpToolRegistry.NormalizeToolName(toolPrefix) ?? string.Empty;

        static readonly HashSet<string> k_ReadOnlyTools = new(StringComparer.OrdinalIgnoreCase)
        {
            NormalizeToolName("Unity.GameObject.Inspect"),
            NormalizeToolName("Unity.GameObject.PreviewChanges"),
            NormalizeToolName("Unity.GetLensUsageReport"),
            NormalizeToolName("Unity.GetLensHealth"),
            NormalizeToolName("Unity.ListToolPacks"),
            NormalizeToolName("Unity.ReadDetailRef"),
            NormalizeToolName("Unity.ReadConsole"),
            NormalizeToolName("Unity.ListResources"),
            NormalizeToolName("Unity.ReadResource"),
            NormalizeToolName("Unity.FindInFile"),
            NormalizeToolName("Unity.GetSha"),
            NormalizeToolName("Unity.ValidateScript"),
            NormalizeToolName("Unity.UI.Raycast"),
            NormalizeToolName("Unity.Asset.Search"),
            NormalizeToolName("Unity.Object.ValidateReferences"),
            NormalizeToolName("Unity.Project.ScanMissingScripts"),
            NormalizeToolName("Unity.Project.GetInfo"),
            NormalizeToolName("Unity.Project.GetPackages"),
            NormalizeToolName("Unity.Profiler.Query"),
            NormalizeToolName("Unity.ManageScript_capabilities")
        };

        static readonly HashSet<string> k_MutatingTools = new(StringComparer.OrdinalIgnoreCase)
        {
            NormalizeToolName("Unity.GameObject.ApplyChanges"),
            NormalizeToolName("Unity.ManageGameObject"),
            NormalizeToolName("Unity.ManageScene"),
            NormalizeToolName("Unity.ManageAsset"),
            NormalizeToolName("Unity.ManageEditor"),
            NormalizeToolName("Unity.ManageMenuItem"),
            NormalizeToolName("Unity.ManageScript"),
            NormalizeToolName("Unity.ManageShader"),
            NormalizeToolName("Unity.ImportExternalModel"),
            NormalizeToolName("Unity.ApplyTextEdits"),
            NormalizeToolName("Unity.ScriptApplyEdits"),
            NormalizeToolName("Unity.CreateScript"),
            NormalizeToolName("Unity.DeleteScript"),
            NormalizeToolName("Unity.RunCommand"),
            NormalizeToolName("Unity.Resource.Write"),
            NormalizeToolName("Unity.Resource.Delete"),
            NormalizeToolName("Unity.Project.ManagePackages"),
            NormalizeToolName("Unity.Asset.ConfigureSpriteImport"),
            NormalizeToolName("Unity.Prefab.SetSerializedProperties"),
            NormalizeToolName("Unity.Scene.SetSerializedProperties"),
            NormalizeToolName("Unity.Tile.BuildSet"),
            NormalizeToolName("Unity.Tilemap.Setup"),
            NormalizeToolName("Unity.Tilemap.Paint"),
            NormalizeToolName("Unity.UI.EnsureNamedHierarchy"),
            NormalizeToolName("Unity.UI.SetLayoutProperties"),
            NormalizeToolName("Unity.UI.Toolkit")
        };

        static readonly string[] k_ReadOnlyPrefixes =
        {
            NormalizeToolPrefix("Unity.Read"),
            NormalizeToolPrefix("Unity.Get"),
            NormalizeToolPrefix("Unity.List"),
            NormalizeToolPrefix("Unity.Find"),
            NormalizeToolPrefix("Unity.Validate"),
            NormalizeToolPrefix("Unity.Query"),
            NormalizeToolPrefix("Unity.Project.Get"),
            NormalizeToolPrefix("Unity.Runtime.Get"),
            NormalizeToolPrefix("Unity.UI.Get")
        };

        public static bool IsReadOnlyHint(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                return false;

            string normalizedToolName = NormalizeToolName(toolName);
            if (k_MutatingTools.Contains(normalizedToolName))
                return false;

            if (k_ReadOnlyTools.Contains(normalizedToolName))
                return true;

            foreach (string prefix in k_ReadOnlyPrefixes)
            {
                if (normalizedToolName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static object BuildAnnotations(string toolName, object explicitAnnotations)
        {
            var generatedAnnotations = new JObject
            {
                ["readOnlyHint"] = IsReadOnlyHint(toolName)
            };

            if (explicitAnnotations == null)
                return generatedAnnotations;

            JObject mergedAnnotations;
            try
            {
                mergedAnnotations = explicitAnnotations is JObject jObject
                    ? (JObject)jObject.DeepClone()
                    : JObject.FromObject(explicitAnnotations);
            }
            catch
            {
                return explicitAnnotations;
            }

            foreach (var property in generatedAnnotations.Properties())
            {
                if (!mergedAnnotations.ContainsKey(property.Name))
                    mergedAnnotations[property.Name] = property.Value.DeepClone();
            }

            return mergedAnnotations;
        }
    }
}
