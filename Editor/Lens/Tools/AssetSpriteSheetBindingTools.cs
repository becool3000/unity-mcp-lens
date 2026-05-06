#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Becool.UnityMcpLens.Editor.Adapters.Unity.Assets;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Models.Assets;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.Services.Assets;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class AssetSpriteSheetBindingTools
    {
        const string ImportFacadeToolName = "Unity.Asset.ImportSpriteSheetAndBind";
        const string PreviewImportToolName = "Unity.Asset.PreviewImportSpriteSheetAndBind";
        const string ApplyImportToolName = "Unity.Asset.ApplyImportSpriteSheetAndBind";
        const string VerifyBindingToolName = "Unity.Asset.VerifySpriteArrayBinding";

        const string ImportFacadeDescription = @"Imports a sprite sheet, slices frames, and binds the resulting Sprite array to a ScriptableObject field.

Prefer the split preview/apply tools for routine authoring. This compatibility facade runs preview by default and applies only when mode is 'apply' or apply is true.";

        const string PreviewImportDescription = @"Previews sprite-sheet import, slicing, texture settings, and ScriptableObject Sprite-array binding without mutating assets.";

        const string ApplyImportDescription = @"Applies sprite-sheet import, slicing, texture settings, and ScriptableObject Sprite-array binding, saving changed assets.";

        const string VerifyBindingDescription = @"Verifies a ScriptableObject Sprite-array binding against expected count, texture name, texture GUID, or ordered sprite names without mutation.";

        static readonly UnityAssetSpriteSheetBindingAdapter Adapter = new UnityAssetSpriteSheetBindingAdapter();
        static readonly AssetSpriteSheetBindingService Service = new AssetSpriteSheetBindingService(Adapter);

        [McpSchema(ImportFacadeToolName)]
        public static object GetImportFacadeSchema()
        {
            return BuildImportSchema(includeMode: true);
        }

        [McpSchema(PreviewImportToolName)]
        public static object GetPreviewImportSchema()
        {
            return BuildImportSchema(includeMode: false);
        }

        [McpSchema(ApplyImportToolName)]
        public static object GetApplyImportSchema()
        {
            return BuildImportSchema(includeMode: false);
        }

        [McpSchema(VerifyBindingToolName)]
        public static object GetVerifyBindingSchema()
        {
            return BuildVerifySchema();
        }

        [McpTool(ImportFacadeToolName, ImportFacadeDescription, "Import Sprite Sheet And Bind", Groups = new[] { "assets" }, EnabledByDefault = true)]
        public static object ImportSpriteSheetAndBind(JObject @params)
        {
            return HandleImportTool(ImportFacadeToolName, "import_sprite_sheet_and_bind", @params, apply: null);
        }

        [McpTool(PreviewImportToolName, PreviewImportDescription, "Preview Import Sprite Sheet And Bind", Groups = new[] { "assets" }, EnabledByDefault = true)]
        public static object PreviewImportSpriteSheetAndBind(JObject @params)
        {
            return HandleImportTool(PreviewImportToolName, "preview_import_sprite_sheet_and_bind", @params, apply: false);
        }

        [McpTool(ApplyImportToolName, ApplyImportDescription, "Apply Import Sprite Sheet And Bind", Groups = new[] { "assets" }, EnabledByDefault = true)]
        public static object ApplyImportSpriteSheetAndBind(JObject @params)
        {
            return HandleImportTool(ApplyImportToolName, "apply_import_sprite_sheet_and_bind", @params, apply: true);
        }

        [McpTool(VerifyBindingToolName, VerifyBindingDescription, "Verify Sprite Array Binding", Groups = new[] { "assets" }, EnabledByDefault = true)]
        public static object VerifySpriteArrayBinding(JObject @params)
        {
            @params ??= new JObject();
            var timing = new ToolOperationTiming(VerifyBindingToolName, "verify_sprite_array_binding", GetUtf8ByteCount(@params.ToString(Formatting.None)));
            AssetSpritePipelineOperationResult result;
            string errorKind = null;

            try
            {
                AssetSpriteArrayBindingVerifyRequest request;
                using (timing.Measure("normalization"))
                {
                    request = NormalizeVerifyRequest(@params);
                }

                using (timing.Measure("service"))
                {
                    result = Service.VerifySpriteArrayBinding(request, timing);
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                result = AssetSpritePipelineOperationResult.Error($"Internal error verifying sprite-array binding: {ex.Message}", errorKind);
            }

            return ShapeResponse(VerifyBindingToolName, result, timing, errorKind);
        }

        static object HandleImportTool(string toolName, string action, JObject @params, bool? apply)
        {
            @params ??= new JObject();
            var timing = new ToolOperationTiming(toolName, action, GetUtf8ByteCount(@params.ToString(Formatting.None)));
            AssetSpritePipelineOperationResult result;
            string errorKind = null;

            try
            {
                AssetSpriteSheetAndBindRequest request;
                bool shouldApply;
                using (timing.Measure("normalization"))
                {
                    shouldApply = apply ?? ResolveFacadeApplyMode(@params);
                    request = NormalizeImportRequest(@params);
                }

                using (timing.Measure("service"))
                {
                    result = shouldApply
                        ? Service.ApplyImportSpriteSheetAndBind(request, timing)
                        : Service.PreviewImportSpriteSheetAndBind(request, timing);
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                result = AssetSpritePipelineOperationResult.Error($"Internal error processing sprite-sheet import/bind: {ex.Message}", errorKind);
            }

            return ShapeResponse(toolName, result, timing, errorKind);
        }

        static object BuildImportSchema(bool includeMode)
        {
            var properties = new Dictionary<string, object>
            {
                ["assetPath"] = new { type = "string", description = "Texture asset path under Assets/." },
                ["frameCount"] = new { type = "integer", description = "Number of frames to slice and bind." },
                ["frameWidth"] = new { type = "integer", description = "Frame width in source texture pixels." },
                ["frameHeight"] = new { type = "integer", description = "Frame height in source texture pixels." },
                ["paddingX"] = new { type = "integer", description = "Horizontal pixels between frames." },
                ["paddingY"] = new { type = "integer", description = "Vertical pixels between frames." },
                ["offsetX"] = new { type = "integer", description = "Left offset in pixels before the first frame." },
                ["offsetY"] = new { type = "integer", description = "Top offset in pixels before the first frame row." },
                ["spriteNamePrefix"] = new { type = "string", description = "Optional sprite-name prefix. Defaults to the texture name." },
                ["pixelsPerUnit"] = new { type = "number", description = "Optional Sprite pixels-per-unit value." },
                ["mipmapEnabled"] = new { type = "boolean", description = "Optional mipmap import setting." },
                ["alphaIsTransparency"] = new { type = "boolean", description = "Optional alpha-is-transparency import setting." },
                ["compression"] = new { type = "string", description = "Optional texture compression: Uncompressed, Compressed, CompressedHQ, or CompressedLQ." },
                ["filterMode"] = new { type = "string", description = "Optional filter mode: Point, Bilinear, or Trilinear." },
                ["wrapMode"] = new { type = "string", description = "Optional texture wrap mode, for example Clamp or Repeat." },
                ["targetAssetPath"] = new { type = "string", description = "ScriptableObject asset path under Assets/ to bind." },
                ["targetFieldName"] = new { type = "string", description = "Serialized Sprite array/list field path on the target asset." }
            };

            if (includeMode)
            {
                properties["mode"] = new { type = "string", description = "preview or apply. Defaults to preview.", @enum = new[] { "preview", "apply" } };
                properties["apply"] = new { type = "boolean", description = "Compatibility boolean; true is equivalent to mode='apply'." };
            }

            return new
            {
                type = "object",
                properties,
                required = new[] { "assetPath", "frameCount", "frameWidth", "frameHeight", "targetAssetPath", "targetFieldName" }
            };
        }

        static object BuildVerifySchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    targetAssetPath = new { type = "string", description = "ScriptableObject asset path under Assets/." },
                    targetFieldName = new { type = "string", description = "Serialized Sprite array/list field path on the target asset." },
                    expectedCount = new { type = "integer", description = "Optional expected array element count." },
                    expectedTextureName = new { type = "string", description = "Optional texture name every bound Sprite should use." },
                    expectedTextureGuid = new { type = "string", description = "Optional texture GUID every bound Sprite should use." },
                    expectedSpriteNames = new { type = "array", description = "Optional expected ordered Sprite names." }
                },
                required = new[] { "targetAssetPath", "targetFieldName" }
            };
        }

        static AssetSpriteSheetAndBindRequest NormalizeImportRequest(JObject parameters)
        {
            return new AssetSpriteSheetAndBindRequest
            {
                AssetPath = GetString(parameters, "assetPath", "AssetPath"),
                FrameCount = GetInt(parameters, 0, "frameCount", "FrameCount"),
                FrameWidth = GetInt(parameters, 0, "frameWidth", "FrameWidth"),
                FrameHeight = GetInt(parameters, 0, "frameHeight", "FrameHeight"),
                PaddingX = GetInt(parameters, 0, "paddingX", "PaddingX"),
                PaddingY = GetInt(parameters, 0, "paddingY", "PaddingY"),
                OffsetX = GetInt(parameters, 0, "offsetX", "OffsetX"),
                OffsetY = GetInt(parameters, 0, "offsetY", "OffsetY"),
                SpriteNamePrefix = GetString(parameters, "spriteNamePrefix", "SpriteNamePrefix"),
                PixelsPerUnit = GetNullableFloat(parameters, "pixelsPerUnit", "PixelsPerUnit", "ppu", "PPU"),
                MipmapEnabled = GetNullableBool(parameters, "mipmapEnabled", "MipmapEnabled"),
                AlphaIsTransparency = GetNullableBool(parameters, "alphaIsTransparency", "AlphaIsTransparency"),
                Compression = GetString(parameters, "compression", "Compression"),
                FilterMode = GetString(parameters, "filterMode", "FilterMode"),
                WrapMode = GetString(parameters, "wrapMode", "WrapMode"),
                TargetAssetPath = GetString(parameters, "targetAssetPath", "TargetAssetPath"),
                TargetFieldName = GetString(parameters, "targetFieldName", "TargetFieldName", "fieldName", "FieldName")
            };
        }

        static AssetSpriteArrayBindingVerifyRequest NormalizeVerifyRequest(JObject parameters)
        {
            return new AssetSpriteArrayBindingVerifyRequest
            {
                TargetAssetPath = GetString(parameters, "targetAssetPath", "TargetAssetPath"),
                TargetFieldName = GetString(parameters, "targetFieldName", "TargetFieldName", "fieldName", "FieldName"),
                ExpectedCount = GetNullableInt(parameters, "expectedCount", "ExpectedCount"),
                ExpectedTextureName = GetString(parameters, "expectedTextureName", "ExpectedTextureName"),
                ExpectedTextureGuid = GetString(parameters, "expectedTextureGuid", "ExpectedTextureGuid", "expectedTextureGUID", "ExpectedTextureGUID"),
                ExpectedSpriteNames = GetStringArray(parameters, "expectedSpriteNames", "ExpectedSpriteNames")
            };
        }

        static bool ResolveFacadeApplyMode(JObject parameters)
        {
            string mode = GetString(parameters, "mode", "Mode");
            if (!string.IsNullOrWhiteSpace(mode))
            {
                if (mode.Equals("preview", StringComparison.OrdinalIgnoreCase))
                    return false;
                if (mode.Equals("apply", StringComparison.OrdinalIgnoreCase))
                    return true;
                throw new InvalidOperationException("mode must be 'preview' or 'apply'.");
            }

            return GetBool(parameters, false, "apply", "Apply");
        }

        static object ShapeResponse(string toolName, AssetSpritePipelineOperationResult result, ToolOperationTiming timing, string fallbackErrorKind)
        {
            object response;
            using (timing.Measure("result_shaping"))
            {
                response = result.success
                    ? Response.Success(result.message, ToolResultCompactor.ShapeStructuredPayload(
                        toolName,
                        result.data,
                        string.Equals(toolName, VerifyBindingToolName, StringComparison.Ordinal)
                            ? BuildVerifyCompactData(result.data)
                            : BuildImportCompactData(result.data),
                        detailRefMeta: new
                        {
                            kind = string.Equals(toolName, VerifyBindingToolName, StringComparison.Ordinal)
                                ? "asset_verify_sprite_array_binding_full_result"
                                : "asset_import_sprite_sheet_bind_full_result"
                        },
                        payloadClass: string.Equals(toolName, VerifyBindingToolName, StringComparison.Ordinal)
                            ? "asset_verify_sprite_array_binding"
                            : "asset_import_sprite_sheet_bind"))
                    : Response.Error(result.message, result.errorData ?? new { errorKind = result.errorKind ?? fallbackErrorKind });

                timing.SetResponseBytes(GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(result.success, result.success ? null : result.errorKind ?? fallbackErrorKind);
            return response;
        }

        static object BuildImportCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            JArray sprites = root["sprites"] as JArray ?? new JArray();
            JArray spriteNames = root["spriteNames"] as JArray ?? new JArray();

            return new
            {
                mode = root["mode"],
                previewOnly = root["previewOnly"],
                applied = root["applied"],
                willModify = root["willModify"],
                assetPath = root["assetPath"],
                textureGuid = root["textureGuid"],
                targetAssetPath = root["targetAssetPath"],
                targetAssetGuid = root["targetAssetGuid"],
                targetFieldName = root["targetFieldName"],
                importedSpriteCount = root["importedSpriteCount"],
                targetFieldReadbackCount = root["targetFieldReadbackCount"],
                targetAssetDirty = root["targetAssetDirty"],
                saved = root["saved"],
                warnings = root["warnings"],
                importerWillModify = root["importer"]?["willModify"],
                importerApplied = root["importer"]?["applied"],
                bindingWillModify = root["binding"]?["willModify"],
                bindingApplied = root["binding"]?["applied"],
                spriteNames = TakeArray(spriteNames, 16),
                omittedSpriteNameCount = Math.Max(0, spriteNames.Count - 16),
                sprites = TakeSpriteRows(sprites, 8),
                omittedSpriteDetailCount = Math.Max(0, sprites.Count - 8)
            };
        }

        static object BuildVerifyCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            JArray references = root["references"] as JArray ?? new JArray();
            JArray spriteNames = root["spriteNames"] as JArray ?? new JArray();

            return new
            {
                targetAssetPath = root["targetAssetPath"],
                targetAssetGuid = root["targetAssetGuid"],
                targetFieldName = root["targetFieldName"],
                passed = root["passed"],
                actualCount = root["actualCount"],
                nonNullSpriteCount = root["nonNullSpriteCount"],
                expectedCount = root["expectedCount"],
                expectedTextureName = root["expectedTextureName"],
                expectedTextureGuid = root["expectedTextureGuid"],
                failedReasons = root["failedReasons"],
                textureNames = root["textureNames"],
                textureGuids = root["textureGuids"],
                spriteNames = TakeArray(spriteNames, 16),
                omittedSpriteNameCount = Math.Max(0, spriteNames.Count - 16),
                references = TakeReferenceRows(references, 8),
                omittedReferenceDetailCount = Math.Max(0, references.Count - 8)
            };
        }

        static JArray TakeArray(JArray source, int maxItems)
        {
            return new JArray(source.Take(maxItems).Select(token => token.DeepClone()));
        }

        static JArray TakeSpriteRows(JArray source, int maxItems)
        {
            var rows = new JArray();
            foreach (JObject sprite in source.OfType<JObject>().Take(maxItems))
            {
                rows.Add(new JObject
                {
                    ["name"] = sprite["name"]?.DeepClone(),
                    ["assetPath"] = sprite["assetPath"]?.DeepClone(),
                    ["rect"] = sprite["rect"]?.DeepClone()
                });
            }

            return rows;
        }

        static JArray TakeReferenceRows(JArray source, int maxItems)
        {
            var rows = new JArray();
            foreach (JObject reference in source.OfType<JObject>().Take(maxItems))
            {
                rows.Add(new JObject
                {
                    ["name"] = reference["name"]?.DeepClone(),
                    ["path"] = reference["path"]?.DeepClone(),
                    ["guid"] = reference["guid"]?.DeepClone(),
                    ["textureName"] = reference["textureName"]?.DeepClone(),
                    ["textureGuid"] = reference["textureGuid"]?.DeepClone()
                });
            }

            return rows;
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

        static bool GetBool(JObject parameters, bool defaultValue, params string[] names)
        {
            foreach (string name in names)
            {
                if (!parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token))
                    continue;

                if (token.Type == JTokenType.Boolean)
                    return token.Value<bool>();
                if (bool.TryParse(token.ToString(), out bool parsed))
                    return parsed;
            }

            return defaultValue;
        }

        static bool? GetNullableBool(JObject parameters, params string[] names)
        {
            foreach (string name in names)
            {
                if (!parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token) || token.Type == JTokenType.Null)
                    continue;

                if (token.Type == JTokenType.Boolean)
                    return token.Value<bool>();
                if (bool.TryParse(token.ToString(), out bool parsed))
                    return parsed;
            }

            return null;
        }

        static int GetInt(JObject parameters, int defaultValue, params string[] names)
        {
            foreach (string name in names)
            {
                if (!parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token) || token.Type == JTokenType.Null)
                    continue;

                if (token.Type == JTokenType.Integer)
                    return token.Value<int>();
                if (int.TryParse(token.ToString(), out int parsed))
                    return parsed;
            }

            return defaultValue;
        }

        static int? GetNullableInt(JObject parameters, params string[] names)
        {
            foreach (string name in names)
            {
                if (!parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token) || token.Type == JTokenType.Null)
                    continue;

                if (token.Type == JTokenType.Integer)
                    return token.Value<int>();
                if (int.TryParse(token.ToString(), out int parsed))
                    return parsed;
            }

            return null;
        }

        static float? GetNullableFloat(JObject parameters, params string[] names)
        {
            foreach (string name in names)
            {
                if (!parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token) || token.Type == JTokenType.Null)
                    continue;

                if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                    return token.Value<float>();
                if (float.TryParse(token.ToString(), out float parsed))
                    return parsed;
            }

            return null;
        }

        static string[] GetStringArray(JObject parameters, params string[] names)
        {
            foreach (string name in names)
            {
                if (!parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token) || token.Type == JTokenType.Null)
                    continue;

                if (token is JArray array)
                {
                    return array
                        .Select(item => item?.ToString())
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .ToArray();
                }

                string single = token.ToString();
                return string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : new[] { single };
            }

            return Array.Empty<string>();
        }

        static int GetUtf8ByteCount(string value) => Encoding.UTF8.GetByteCount(value ?? string.Empty);
    }
}
