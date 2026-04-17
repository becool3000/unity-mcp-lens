using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.MCP.Editor.Tools
{
    public record UiToolkitParams
    {
        [McpDescription("Action: find_panel_settings, ensure_panel_settings, validate_asset, save_and_validate_asset, generate_uxml_schemas, get_asset_preview.", Required = true)]
        public string Action { get; set; }

        [McpDescription("UXML/USS/TSS/asset path or unity://path URI.", Required = false)]
        public string AssetPath { get; set; }

        [McpDescription("Text content for save_and_validate_asset.", Required = false)]
        public string Text { get; set; }

        [McpDescription("SHA-256 precondition required when save_and_validate_asset overwrites an existing file.", Required = false)]
        public string PreconditionSha256 { get; set; }

        [McpDescription("Optional output path for previews or generated schema artifacts.", Required = false)]
        public string OutputPath { get; set; }
    }

    public static class UiToolkitTools
    {
        const string Description = "Canonical UI Toolkit helper for panel settings, UXML/USS validation, schema generation, and compact asset previews.";

        [McpTool("Unity.UI.Toolkit", Description, "Unity UI Toolkit", Groups = new[] { "ui" }, EnabledByDefault = true)]
        public static object Handle(UiToolkitParams parameters)
        {
            parameters ??= new UiToolkitParams();
            string action = (parameters.Action ?? string.Empty).Trim().ToLowerInvariant();

            try
            {
                return action switch
                {
                    "find_panel_settings" => FindPanelSettings(),
                    "ensure_panel_settings" => EnsurePanelSettings(),
                    "validate_asset" => ValidateAsset(parameters.AssetPath),
                    "save_and_validate_asset" => SaveAndValidate(parameters),
                    "generate_uxml_schemas" => GenerateUxmlSchemas(),
                    "get_asset_preview" => GetAssetPreview(parameters.AssetPath, parameters.OutputPath),
                    _ => Response.Error("INVALID_UI_TOOLKIT_ACTION: action must be find_panel_settings, ensure_panel_settings, validate_asset, save_and_validate_asset, generate_uxml_schemas, or get_asset_preview.")
                };
            }
            catch (Exception ex)
            {
                return Response.Error($"UI_TOOLKIT_FAILED: {ex.Message}");
            }
        }

        static object FindPanelSettings()
        {
            var panels = AssetDatabase.FindAssets("t:PanelSettings")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path =>
                {
                    var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
                    return new
                    {
                        path,
                        guid = AssetDatabase.AssetPathToGUID(path),
                        name = panel != null ? panel.name : Path.GetFileNameWithoutExtension(path)
                    };
                })
                .ToArray();

            return Response.Success("Panel settings search completed.", new
            {
                count = panels.Length,
                panels
            });
        }

        static object EnsurePanelSettings()
        {
            var existing = AssetDatabase.FindAssets("t:PanelSettings").FirstOrDefault();
            if (!string.IsNullOrEmpty(existing))
            {
                string existingPath = AssetDatabase.GUIDToAssetPath(existing);
                return Response.Success("Panel settings already exist.", new { created = false, path = existingPath, guid = existing });
            }

            const string folder = "Assets/UI";
            const string path = "Assets/UI/DefaultPanelSettings.asset";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), folder));
                AssetDatabase.Refresh();
            }

            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(panel, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return Response.Success("Panel settings created.", new
            {
                created = true,
                path,
                guid = AssetDatabase.AssetPathToGUID(path)
            });
        }

        static object SaveAndValidate(UiToolkitParams parameters)
        {
            if (string.IsNullOrWhiteSpace(parameters.AssetPath))
                return Response.Error("ASSET_PATH_REQUIRED: AssetPath is required.");

            var write = ResourceMutationTools.Write(new ResourceWriteParams
            {
                Uri = parameters.AssetPath,
                Text = parameters.Text ?? string.Empty,
                PreconditionSha256 = parameters.PreconditionSha256,
                CreateDirectories = true
            });

            var validation = ValidateAsset(parameters.AssetPath);
            return Response.Success("UI asset saved and validated.", new
            {
                write,
                validation
            });
        }

        static object ValidateAsset(string assetPath)
        {
            if (!TryResolveAssetPath(assetPath, out var projectRoot, out var fullPath, out var relativePath, out var error))
                return Response.Error(error);

            if (!File.Exists(fullPath))
                return Response.Error("UI_ASSET_NOT_FOUND", new { path = relativePath });

            string extension = Path.GetExtension(fullPath).ToLowerInvariant();
            string text = File.ReadAllText(fullPath);
            var warnings = Array.Empty<string>();
            if (extension == ".uxml")
                ValidateXml(text);
            else if (extension != ".uss" && extension != ".tss")
                warnings = new[] { "Only .uxml, .uss, and .tss files receive UI Toolkit-specific validation." };

            return Response.Success("UI asset validation completed.", new
            {
                path = relativePath,
                extension,
                sha256 = ResourceUriHelper.ComputeSha256(File.ReadAllBytes(fullPath)),
                bytes = System.Text.Encoding.UTF8.GetByteCount(text),
                valid = true,
                warnings
            });
        }

        static object GenerateUxmlSchemas()
        {
            Type generatorType = Type.GetType("UnityEditor.UIElements.UxmlSchemaGenerator, UnityEditor");
            MethodInfo method = generatorType?.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name.IndexOf("Generate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     m.Name.IndexOf("Update", StringComparison.OrdinalIgnoreCase) >= 0);

            if (method == null || method.GetParameters().Length != 0)
            {
                return Response.Success("UXML schema generation API is not available in this Unity version.", new
                {
                    available = false,
                    hint = "Unity.UI.Toolkit keeps this action as a canonical compatibility surface without porting Assistant-specific schema helpers."
                });
            }

            method.Invoke(null, null);
            AssetDatabase.Refresh();
            return Response.Success("UXML schema generation completed.", new { available = true, method = method.Name });
        }

        static object GetAssetPreview(string assetPath, string outputPath)
        {
            if (!TryResolveAssetPath(assetPath, out var projectRoot, out _, out var relativePath, out var error))
                return Response.Error(error);

            var asset = AssetDatabase.LoadMainAssetAtPath(relativePath);
            if (asset == null)
                return Response.Error("UI_ASSET_NOT_FOUND", new { path = relativePath });

            Texture2D texture = AssetPreview.GetAssetPreview(asset) ?? AssetPreview.GetMiniThumbnail(asset);
            if (texture == null)
                return Response.Error("PREVIEW_UNAVAILABLE", new { path = relativePath });

            string target = ResolveOutputPath(projectRoot, outputPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.WriteAllBytes(target, texture.EncodeToPNG());

            string targetRelative = ResourceMutationTools.ToProjectRelativePath(projectRoot, target);
            return Response.Success("UI asset preview written.", new
            {
                path = relativePath,
                previewPath = targetRelative,
                previewUri = $"unity://path/{targetRelative}",
                bytes = new FileInfo(target).Length
            });
        }

        static bool TryResolveAssetPath(string assetPath, out string projectRoot, out string fullPath, out string relativePath, out string error)
        {
            projectRoot = ResourceUriHelper.ResolveProjectRoot(null);
            fullPath = ResourceMutationTools.ResolveSafePath(assetPath, projectRoot);
            relativePath = fullPath != null ? ResourceMutationTools.ToProjectRelativePath(projectRoot, fullPath) : null;
            error = null;

            if (fullPath == null || !ResourceUriHelper.IsPathUnderProject(fullPath, projectRoot))
            {
                error = "INVALID_ASSET_PATH: AssetPath must resolve under the Unity project root.";
                return false;
            }

            if (!relativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                !relativePath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                error = "PATH_OUTSIDE_ALLOWED_ROOTS: UI Toolkit assets must be under Assets/ or Packages/.";
                return false;
            }

            return true;
        }

        static string ResolveOutputPath(string projectRoot, string outputPath, string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                string safeName = Path.GetFileNameWithoutExtension(sourcePath);
                return Path.Combine(projectRoot, "Temp", "LensPreviews", $"{safeName}-preview.png");
            }

            string normalized = outputPath.Replace('\\', '/');
            if (Path.IsPathRooted(normalized))
                throw new InvalidOperationException("OutputPath must be relative to the Unity project root.");

            if (!normalized.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                normalized = $"{normalized.TrimEnd('/')}/{Path.GetFileNameWithoutExtension(sourcePath)}-preview.png";

            var full = Path.GetFullPath(Path.Combine(projectRoot, normalized));
            if (!ResourceUriHelper.IsPathUnderProject(full, projectRoot))
                throw new InvalidOperationException("OutputPath must stay under the Unity project root.");

            return full;
        }

        static void ValidateXml(string text)
        {
            var document = new XmlDocument();
            document.LoadXml(text);
        }
    }
}
