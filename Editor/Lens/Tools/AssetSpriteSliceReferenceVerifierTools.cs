#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils;
using Becool.UnityMcpLens.Editor.Utils.Graphics;
using Becool.UnityMcpLens.Editor.Utils.Scene;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class AssetSpriteSliceReferenceVerifierTools
    {
        const string ToolName = "Unity.Asset.VerifySpriteSlicesAndReferences";
        const int DefaultMaxPrefabs = 50;
        const int DefaultMaxSprites = 512;
        const int DefaultMaxFindings = 200;
        const int MaxPrefabLimit = 500;
        const int MaxSpriteLimit = 2048;
        const int MaxFindingLimit = 2000;

        sealed class Request
        {
            public string AssetPath;
            public string[] ExpectedSpriteNames = Array.Empty<string>();
            public ExpectedSpriteRow[] ExpectedSprites = Array.Empty<ExpectedSpriteRow>();
            public JObject ExpectedSettings;
            public string PrefabPath;
            public string[] PrefabPaths = Array.Empty<string>();
            public string Under = "Assets";
            public bool UnderWasProvided;
            public string NameFilter;
            public ExpectedPrefabReference[] ExpectedPrefabReferences = Array.Empty<ExpectedPrefabReference>();
            public bool RequireAllScannedImagesUseAtlas;
            public bool IncludeInactive = true;
            public bool VerifyAlpha = true;
            public float AlphaThreshold = 0.02f;
            public float EmptyAlphaCoverageThreshold = 0.005f;
            public int MaxPrefabs = DefaultMaxPrefabs;
            public int MaxSprites = DefaultMaxSprites;
            public int MaxFindings = DefaultMaxFindings;
        }

        sealed class ExpectedSpriteRow
        {
            public string name;
            public float? x;
            public float? y;
            public float? width;
            public float? height;
            public float? pixelsPerUnit;
        }

        sealed class ExpectedPrefabReference
        {
            public string prefabPath;
            public string target;
            public string targetPath;
            public string searchMethod = "by_id_or_name_or_path";
            public string expectedSpriteName;
        }

        sealed class FindingRow
        {
            public int index;
            public string severity;
            public string kind;
            public string message;
            public string assetPath;
            public string prefabPath;
            public string hierarchyPath;
            public string componentType;
            public string propertyPath;
            public string spriteName;
            public string expectedSpriteName;
            public object expected;
            public object actual;
        }

        sealed class ReferenceRow
        {
            public string prefabPath;
            public string prefabGuid;
            public string hierarchyPath;
            public string objectName;
            public string componentType;
            public string propertyPath;
            public string spriteName;
            public string spriteAssetPath;
            public string textureAssetPath;
            public string textureGuid;
            public bool hasSprite;
            public bool usesRequestedAtlas;
        }

        sealed class PrefabSummary
        {
            public string prefabPath;
            public string guid;
            public bool dirtyBefore;
            public bool dirtyAfter;
            public int imageCount;
            public int spriteRendererCount;
            public int referenceCount;
            public int requestedAtlasReferenceCount;
            public int missingSpriteCount;
            public int findingCount;
        }

        sealed class VerificationContext
        {
            public Request Request;
            public List<FindingRow> Findings = new();
            public List<ReferenceRow> References = new();
            public List<PrefabSummary> PrefabSummaries = new();
            public int TotalFindingCount;
            public bool Truncated;
        }

        [McpSchema(ToolName)]
        public static object GetSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    assetPath = new { type = "string", description = "Texture2D sprite sheet or UI atlas path under Assets/ or Packages/." },
                    expectedSpriteNames = new { type = "array", description = "Optional expected ordered sprite-name list.", items = new { type = "string" } },
                    expectedSprites = new
                    {
                        type = "array",
                        description = "Optional expected sprite slice rows. When present, name and rect checks are driven from these rows.",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                name = new { type = "string" },
                                x = new { type = "number" },
                                y = new { type = "number" },
                                width = new { type = "number" },
                                height = new { type = "number" },
                                pixelsPerUnit = new { type = "number" }
                            }
                        }
                    },
                    expectedSettings = new
                    {
                        type = "object",
                        description = "Optional importer setting expectations.",
                        properties = new
                        {
                            textureType = new { type = "string" },
                            spriteMode = new { type = "string" },
                            pixelsPerUnit = new { type = "number" },
                            mipmapEnabled = new { type = "boolean" },
                            alphaIsTransparency = new { type = "boolean" },
                            filterMode = new { type = "string" },
                            wrapMode = new { type = "string" },
                            compression = new { type = "string" },
                            maxTextureSize = new { type = "integer" }
                        }
                    },
                    prefabPath = new { type = "string", description = "Optional single prefab asset path to scan." },
                    prefabPaths = new { type = "array", description = "Optional prefab asset paths to scan.", items = new { type = "string" } },
                    under = new { type = "string", description = "Optional prefab scan root under Assets/ or Packages/. Defaults to Assets when prefab scanning is requested." },
                    nameFilter = new { type = "string", description = "Optional prefab filename substring filter for folder scans." },
                    expectedPrefabReferences = new
                    {
                        type = "array",
                        description = "Optional expected prefab sprite-reference rows.",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                prefabPath = new { type = "string" },
                                target = new { type = "string" },
                                targetPath = new { type = "string" },
                                searchMethod = new { type = "string" },
                                expectedSpriteName = new { type = "string" }
                            }
                        }
                    },
                    requireAllScannedImagesUseAtlas = new { type = "boolean", description = "When true, every scanned Image/SpriteRenderer with a sprite must reference the requested atlas texture. Defaults to false." },
                    includeInactive = new { type = "boolean", description = "Include inactive prefab children when scanning references. Defaults to true." },
                    verifyAlpha = new { type = "boolean", description = "Measure sprite alpha coverage and report empty slices. Defaults to true." },
                    alphaThreshold = new { type = "number", description = "Alpha threshold used to decide visible pixels. Defaults to 0.02." },
                    emptyAlphaCoverageThreshold = new { type = "number", description = "Slices at or below this visible-pixel coverage are reported empty. Defaults to 0.005." },
                    maxPrefabs = new { type = "integer", description = "Maximum prefab assets to scan. Defaults to 50 and is clamped to 1..500." },
                    maxSprites = new { type = "integer", description = "Maximum sprite rows to keep inline/full-result payload. Defaults to 512 and is clamped to 1..2048." },
                    maxFindings = new { type = "integer", description = "Maximum finding rows to keep inline/full-result payload. Defaults to 200 and is clamped to 1..2000." }
                },
                required = new[] { "assetPath" }
            };
        }

        [McpTool(ToolName,
            "Verifies sprite-sheet/importer slices and prefab Image/SpriteRenderer references to the expected atlas without importing, saving, or mutating assets.",
            "Verify Sprite Slices And References",
            Groups = new[] { "assets" },
            EnabledByDefault = true)]
        public static object VerifySpriteSlicesAndReferences(JObject @params)
        {
            @params ??= new JObject();
            var timing = new ToolOperationTiming(ToolName, "verify_sprite_slices_and_references", PayloadBudgeting.GetUtf8ByteCount(@params.ToString(Formatting.None)));
            bool success = false;
            string errorKind = null;
            object data = null;
            string message = null;

            try
            {
                Request request;
                string[] prefabPaths;
                using (timing.Measure("normalization"))
                {
                    request = Normalize(@params);
                    if (!TryValidateAssetPath(request.AssetPath, out object assetErrorData, out string assetErrorMessage))
                    {
                        errorKind = "INVALID_ASSET_PATH";
                        data = assetErrorData;
                        message = assetErrorMessage;
                        return Response.Error(assetErrorMessage, assetErrorData);
                    }

                    if (!TryResolvePrefabPaths(request, out prefabPaths, out object prefabErrorData, out string prefabErrorMessage))
                    {
                        errorKind = "INVALID_PREFAB_PATH";
                        data = prefabErrorData;
                        message = prefabErrorMessage;
                        return Response.Error(prefabErrorMessage, prefabErrorData);
                    }
                }

                using (timing.Measure("service"))
                {
                    data = Execute(request, prefabPaths);
                    var shaped = JObject.FromObject(data);
                    int findingCount = shaped.Value<int?>("findingCount") ?? 0;
                    success = true;
                    message = findingCount == 0
                        ? $"Sprite slice/reference verification passed for '{request.AssetPath}'."
                        : $"Sprite slice/reference verification found {findingCount} issue(s) for '{request.AssetPath}'.";
                }
            }
            catch (Exception ex)
            {
                success = false;
                errorKind = ex.GetType().Name;
                message = $"Sprite slice/reference verification failed: {ex.Message}";
                data = new
                {
                    status = "failed",
                    errorKind,
                    error = ex.Message,
                    saveState = BuildReadOnlySaveState()
                };
            }
            finally
            {
                timing.Record(success, success ? null : errorKind);
            }

            object response;
            using (timing.Measure("result_shaping"))
            {
                response = success
                    ? Response.Success(message, ToolResultCompactor.ShapeStructuredPayload(
                        ToolName,
                        data,
                        BuildCompactData(data),
                        new { kind = "asset_sprite_slice_reference_verification_full_result" },
                        "asset_sprite_slice_reference_verification",
                        detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes))
                    : Response.Error(message ?? "Sprite slice/reference verification failed.", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            return response;
        }

        static object Execute(Request request, string[] prefabPaths)
        {
            var context = new VerificationContext { Request = request };
            TextureImporter importer = AssetImporter.GetAtPath(request.AssetPath) as TextureImporter;
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(request.AssetPath);
            Sprite[] sprites = LoadSprites(request.AssetPath);
            bool textureDirtyBefore = texture != null && EditorUtility.IsDirty(texture);
            object sceneDirtyBefore = SceneDirtyStateUtility.CaptureLoadedScenes();

            List<object> spriteRows = BuildSpriteRows(context, texture, sprites);
            VerifyImporterSettings(context, importer);
            VerifySpriteInventory(context, sprites);
            VerifyExpectedSprites(context, sprites);

            foreach (string prefabPath in prefabPaths)
                ScanPrefabReferences(context, prefabPath);

            VerifyExpectedPrefabReferences(context);

            bool textureDirtyAfter = texture != null && EditorUtility.IsDirty(texture);
            object sceneDirtyAfter = SceneDirtyStateUtility.CaptureLoadedScenes();
            bool passed = context.TotalFindingCount == 0;

            return new
            {
                status = passed ? "passed" : "findings",
                passed,
                readOnly = true,
                assetPath = request.AssetPath,
                textureGuid = AssetDatabase.AssetPathToGUID(request.AssetPath),
                textureName = texture != null ? texture.name : null,
                textureSize = texture != null ? new { width = texture.width, height = texture.height } : null,
                textureDimensions = texture != null ? DescribeTextureDimensions(importer, texture) : null,
                importer = DescribeImporter(importer, sprites.Length),
                spriteSummary = new
                {
                    importedSpriteCount = sprites.Length,
                    returnedSpriteCount = spriteRows.Count,
                    expectedSpriteNameCount = ExpectedNames(request).Length,
                    expectedSpriteRowCount = request.ExpectedSprites.Length,
                    duplicateSpriteNameCount = sprites.GroupBy(sprite => sprite.name, StringComparer.Ordinal).Count(group => group.Count() > 1),
                    maxSprites = request.MaxSprites
                },
                prefabReferenceSummary = new
                {
                    scannedPrefabCount = context.PrefabSummaries.Count,
                    referenceCount = context.References.Count,
                    requestedAtlasReferenceCount = context.References.Count(reference => reference.usesRequestedAtlas),
                    missingSpriteCount = context.References.Count(reference => !reference.hasSprite),
                    requireAllScannedImagesUseAtlas = request.RequireAllScannedImagesUseAtlas,
                    expectedPrefabReferenceCount = request.ExpectedPrefabReferences.Length
                },
                findingCount = context.TotalFindingCount,
                returnedFindingCount = context.Findings.Count,
                severityCounts = Count(context.Findings, finding => finding.severity),
                kindCounts = Count(context.Findings, finding => finding.kind),
                truncated = context.Truncated,
                policy = new
                {
                    verifyAlpha = request.VerifyAlpha,
                    alphaThreshold = request.AlphaThreshold,
                    emptyAlphaCoverageThreshold = request.EmptyAlphaCoverageThreshold,
                    includeInactive = request.IncludeInactive,
                    maxPrefabs = request.MaxPrefabs,
                    maxSprites = request.MaxSprites,
                    maxFindings = request.MaxFindings
                },
                prefabScan = new
                {
                    prefabPath = request.PrefabPath,
                    prefabPaths = request.PrefabPaths,
                    under = request.Under,
                    nameFilter = request.NameFilter,
                    resolvedPrefabPaths = prefabPaths
                },
                sprites = spriteRows,
                prefabSummaries = context.PrefabSummaries,
                prefabReferences = context.References,
                findings = context.Findings,
                saveState = BuildReadOnlySaveState(),
                dirtyEvidence = new
                {
                    textureDirtyBefore,
                    textureDirtyAfter,
                    textureDirtiedByVerification = !textureDirtyBefore && textureDirtyAfter,
                    prefabAssetsDirtyBeforeCount = context.PrefabSummaries.Count(summary => summary.dirtyBefore),
                    prefabAssetsDirtyAfterCount = context.PrefabSummaries.Count(summary => summary.dirtyAfter),
                    prefabAssetsDirtiedByVerification = context.PrefabSummaries
                        .Where(summary => !summary.dirtyBefore && summary.dirtyAfter)
                        .Select(summary => summary.prefabPath)
                        .ToArray(),
                    sceneDirtyBefore,
                    sceneDirtyAfter
                }
            };
        }

        static List<object> BuildSpriteRows(VerificationContext context, Texture2D texture, Sprite[] sprites)
        {
            var rows = new List<object>();
            Texture2D readableTexture = null;
            Color32[] pixels = null;
            if (context.Request.VerifyAlpha && texture != null)
            {
                readableTexture = texture.ReadableCopy();
                pixels = readableTexture.GetPixels32();
            }

            try
            {
                foreach (Sprite sprite in sprites.Take(context.Request.MaxSprites))
                {
                    double? alphaCoverage = null;
                    if (pixels != null && texture != null)
                    {
                        alphaCoverage = CalculateAlphaCoverage(sprite.rect, texture.width, texture.height, pixels, context.Request.AlphaThreshold);
                        if (alphaCoverage.Value <= context.Request.EmptyAlphaCoverageThreshold)
                        {
                            AddFinding(
                                context,
                                "warning",
                                "empty_alpha_slice",
                                $"Sprite slice '{sprite.name}' has alpha coverage {alphaCoverage.Value.ToString("0.####", CultureInfo.InvariantCulture)}, at or below the empty threshold.",
                                spriteName: sprite.name,
                                actual: alphaCoverage.Value,
                                expected: new { greaterThan = context.Request.EmptyAlphaCoverageThreshold });
                        }
                    }

                    rows.Add(new
                    {
                        index = rows.Count,
                        name = sprite.name,
                        rect = DescribeRect(sprite.rect),
                        pivot = new { x = sprite.pivot.x, y = sprite.pivot.y },
                        pixelsPerUnit = sprite.pixelsPerUnit,
                        textureAssetPath = AssetDatabase.GetAssetPath(sprite.texture),
                        textureGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sprite.texture)),
                        alphaCoverage
                    });
                }
            }
            finally
            {
                if (readableTexture != null && !ReferenceEquals(readableTexture, texture))
                    UnityEngine.Object.DestroyImmediate(readableTexture);
            }

            if (sprites.Length > context.Request.MaxSprites)
                context.Truncated = true;

            return rows;
        }

        static void VerifyImporterSettings(VerificationContext context, TextureImporter importer)
        {
            if (importer == null)
                return;

            JObject expected = context.Request.ExpectedSettings;
            if (expected == null)
                return;

            CompareStringSetting(context, expected, "textureType", importer.textureType.ToString());
            CompareStringSetting(context, expected, "spriteMode", importer.spriteImportMode.ToString());
            CompareFloatSetting(context, expected, "pixelsPerUnit", importer.spritePixelsPerUnit);
            CompareBoolSetting(context, expected, "mipmapEnabled", importer.mipmapEnabled);
            CompareBoolSetting(context, expected, "alphaIsTransparency", importer.alphaIsTransparency);
            CompareStringSetting(context, expected, "filterMode", importer.filterMode.ToString());
            CompareStringSetting(context, expected, "wrapMode", importer.wrapMode.ToString());
            CompareStringSetting(context, expected, "compression", importer.textureCompression.ToString());
            CompareIntSetting(context, expected, "maxTextureSize", importer.maxTextureSize);
        }

        static void VerifySpriteInventory(VerificationContext context, Sprite[] sprites)
        {
            foreach (var duplicate in sprites.GroupBy(sprite => sprite.name, StringComparer.Ordinal).Where(group => group.Count() > 1))
            {
                AddFinding(
                    context,
                    "error",
                    "duplicate_sprite_name",
                    $"Sprite name '{duplicate.Key}' appears {duplicate.Count()} times in the imported subassets.",
                    spriteName: duplicate.Key,
                    actual: duplicate.Count(),
                    expected: 1);
            }

            string[] expectedNames = ExpectedNames(context.Request);
            if (expectedNames.Length == 0)
                return;

            string[] actualNames = sprites.Select(sprite => sprite.name).ToArray();
            foreach (string expectedName in expectedNames.Where(name => !actualNames.Contains(name, StringComparer.Ordinal)))
            {
                AddFinding(context, "error", "missing_sprite_slice", $"Expected sprite slice '{expectedName}' was not imported.", spriteName: expectedName);
            }

            foreach (string actualName in actualNames.Where(name => !expectedNames.Contains(name, StringComparer.Ordinal)))
            {
                AddFinding(context, "warning", "unexpected_sprite_slice", $"Imported sprite slice '{actualName}' was not in the expected list.", spriteName: actualName);
            }

            int compareCount = Math.Min(expectedNames.Length, actualNames.Length);
            for (int index = 0; index < compareCount; index++)
            {
                if (!string.Equals(expectedNames[index], actualNames[index], StringComparison.Ordinal))
                {
                    AddFinding(
                        context,
                        "warning",
                        "sprite_order_mismatch",
                        $"Sprite slice at index {index} was '{actualNames[index]}' but expected '{expectedNames[index]}'.",
                        spriteName: actualNames[index],
                        expectedSpriteName: expectedNames[index],
                        expected: new { index, name = expectedNames[index] },
                        actual: new { index, name = actualNames[index] });
                }
            }
        }

        static void VerifyExpectedSprites(VerificationContext context, Sprite[] sprites)
        {
            foreach (ExpectedSpriteRow expected in context.Request.ExpectedSprites)
            {
                if (string.IsNullOrWhiteSpace(expected.name))
                    continue;

                Sprite sprite = sprites.FirstOrDefault(candidate => string.Equals(candidate.name, expected.name, StringComparison.Ordinal));
                if (sprite == null)
                    continue;

                if (expected.x.HasValue || expected.y.HasValue || expected.width.HasValue || expected.height.HasValue)
                    CompareRect(context, expected, sprite);

                if (expected.pixelsPerUnit.HasValue && Math.Abs(sprite.pixelsPerUnit - expected.pixelsPerUnit.Value) > 0.001f)
                {
                    AddFinding(
                        context,
                        "error",
                        "sprite_ppu_mismatch",
                        $"Sprite '{sprite.name}' pixels-per-unit was {sprite.pixelsPerUnit} but expected {expected.pixelsPerUnit.Value}.",
                        spriteName: sprite.name,
                        expected: expected.pixelsPerUnit.Value,
                        actual: sprite.pixelsPerUnit);
                }
            }
        }

        static void ScanPrefabReferences(VerificationContext context, string prefabPath)
        {
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var summary = new PrefabSummary
            {
                prefabPath = prefabPath,
                guid = AssetDatabase.AssetPathToGUID(prefabPath),
                dirtyBefore = prefabAsset != null && EditorUtility.IsDirty(prefabAsset)
            };

            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                if (root == null)
                {
                    AddFinding(context, "error", "prefab_load_failed", $"Prefab '{prefabPath}' could not be loaded.", prefabPath: prefabPath);
                    return;
                }

                foreach (Image image in root.GetComponentsInChildren<Image>(context.Request.IncludeInactive))
                {
                    summary.imageCount++;
                    AddReference(context, summary, prefabPath, root.transform, image.gameObject.transform, typeof(Image).FullName, "m_Sprite", image.sprite);
                }

                foreach (SpriteRenderer spriteRenderer in root.GetComponentsInChildren<SpriteRenderer>(context.Request.IncludeInactive))
                {
                    summary.spriteRendererCount++;
                    AddReference(context, summary, prefabPath, root.transform, spriteRenderer.gameObject.transform, typeof(SpriteRenderer).FullName, "m_Sprite", spriteRenderer.sprite);
                }
            }
            finally
            {
                if (root != null)
                    PrefabUtility.UnloadPrefabContents(root);

                GameObject afterAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                summary.dirtyAfter = afterAsset != null && EditorUtility.IsDirty(afterAsset);
                context.PrefabSummaries.Add(summary);
            }
        }

        static void AddReference(VerificationContext context, PrefabSummary summary, string prefabPath, Transform root, Transform target, string componentType, string propertyPath, Sprite sprite)
        {
            string hierarchyPath = GetRelativePath(root, target);
            string spriteAssetPath = sprite != null ? AssetDatabase.GetAssetPath(sprite) : null;
            string textureAssetPath = sprite != null && sprite.texture != null ? AssetDatabase.GetAssetPath(sprite.texture) : null;
            bool usesAtlas = string.Equals(textureAssetPath, context.Request.AssetPath, StringComparison.OrdinalIgnoreCase);
            var row = new ReferenceRow
            {
                prefabPath = prefabPath,
                prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath),
                hierarchyPath = hierarchyPath,
                objectName = target != null ? target.name : null,
                componentType = componentType,
                propertyPath = propertyPath,
                spriteName = sprite != null ? sprite.name : null,
                spriteAssetPath = spriteAssetPath,
                textureAssetPath = textureAssetPath,
                textureGuid = AssetDatabase.AssetPathToGUID(textureAssetPath),
                hasSprite = sprite != null,
                usesRequestedAtlas = usesAtlas
            };

            context.References.Add(row);
            summary.referenceCount++;
            if (usesAtlas)
                summary.requestedAtlasReferenceCount++;
            if (sprite == null)
            {
                summary.missingSpriteCount++;
                AddFinding(context, "warning", "prefab_visual_without_sprite", $"{componentType} at '{hierarchyPath}' has no sprite assigned.", prefabPath: prefabPath, hierarchyPath: hierarchyPath, componentType: componentType, propertyPath: propertyPath);
                return;
            }

            if (context.Request.RequireAllScannedImagesUseAtlas && !usesAtlas)
            {
                AddFinding(
                    context,
                    "error",
                    "prefab_sprite_reference_wrong_atlas",
                    $"{componentType} at '{hierarchyPath}' references sprite '{sprite.name}' from '{textureAssetPath}', not '{context.Request.AssetPath}'.",
                    prefabPath: prefabPath,
                    hierarchyPath: hierarchyPath,
                    componentType: componentType,
                    propertyPath: propertyPath,
                    spriteName: sprite.name,
                    expected: context.Request.AssetPath,
                    actual: textureAssetPath);
            }
        }

        static void VerifyExpectedPrefabReferences(VerificationContext context)
        {
            foreach (ExpectedPrefabReference expected in context.Request.ExpectedPrefabReferences)
            {
                ReferenceRow[] candidates = context.References
                    .Where(row => MatchesExpectedPrefab(row, expected))
                    .ToArray();

                if (candidates.Length == 0)
                {
                    AddFinding(
                        context,
                        "error",
                        "expected_prefab_target_not_found",
                        "Expected prefab sprite-reference target was not found.",
                        prefabPath: NormalizeAssetPath(expected.prefabPath),
                        hierarchyPath: NormalizePath(expected.targetPath ?? expected.target),
                        expectedSpriteName: expected.expectedSpriteName,
                        expected: expected);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(expected.expectedSpriteName))
                    continue;

                if (candidates.Any(row => string.Equals(row.spriteName, expected.expectedSpriteName, StringComparison.Ordinal)))
                    continue;

                ReferenceRow first = candidates[0];
                AddFinding(
                    context,
                    "error",
                    "expected_prefab_sprite_mismatch",
                    $"Expected prefab target '{first.hierarchyPath}' to use sprite '{expected.expectedSpriteName}', but found '{first.spriteName ?? "<null>"}'.",
                    prefabPath: first.prefabPath,
                    hierarchyPath: first.hierarchyPath,
                    componentType: first.componentType,
                    propertyPath: first.propertyPath,
                    spriteName: first.spriteName,
                    expectedSpriteName: expected.expectedSpriteName,
                    expected: expected.expectedSpriteName,
                    actual: first.spriteName);
            }
        }

        static bool MatchesExpectedPrefab(ReferenceRow row, ExpectedPrefabReference expected)
        {
            string expectedPrefabPath = NormalizeAssetPath(expected.prefabPath);
            if (!string.IsNullOrWhiteSpace(expectedPrefabPath) &&
                !string.Equals(row.prefabPath, expectedPrefabPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string targetPath = NormalizePath(expected.targetPath);
            if (!string.IsNullOrWhiteSpace(targetPath) &&
                string.Equals(row.hierarchyPath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string target = expected.target?.Trim();
            if (string.IsNullOrWhiteSpace(target))
                return false;

            string method = string.IsNullOrWhiteSpace(expected.searchMethod)
                ? "by_id_or_name_or_path"
                : expected.searchMethod.Trim();
            if (method.Equals("by_name", StringComparison.OrdinalIgnoreCase))
                return string.Equals(row.objectName, target, StringComparison.OrdinalIgnoreCase);
            if (method.Equals("by_path", StringComparison.OrdinalIgnoreCase))
                return string.Equals(row.hierarchyPath, NormalizePath(target), StringComparison.OrdinalIgnoreCase);

            string normalizedTarget = NormalizePath(target);
            return string.Equals(row.objectName, target, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(row.hierarchyPath, normalizedTarget, StringComparison.OrdinalIgnoreCase) ||
                row.hierarchyPath.EndsWith("/" + target, StringComparison.OrdinalIgnoreCase);
        }

        static Request Normalize(JObject parameters)
        {
            return new Request
            {
                AssetPath = NormalizeAssetPath(GetString(parameters, "assetPath", "AssetPath", "path", "Path")),
                ExpectedSpriteNames = GetStringArray(parameters, "expectedSpriteNames", "ExpectedSpriteNames"),
                ExpectedSprites = GetObjectArray(parameters, "expectedSprites", "ExpectedSprites").Select(ParseExpectedSprite).Where(row => row != null).ToArray(),
                ExpectedSettings = GetToken(parameters, "expectedSettings", "ExpectedSettings") as JObject,
                PrefabPath = NormalizeAssetPath(GetString(parameters, "prefabPath", "PrefabPath")),
                PrefabPaths = GetStringArray(parameters, "prefabPaths", "PrefabPaths").Select(NormalizeAssetPath).ToArray(),
                Under = NormalizeFolderPath(GetString(parameters, "under", "Under") ?? "Assets"),
                UnderWasProvided = GetToken(parameters, "under", "Under") != null,
                NameFilter = GetString(parameters, "nameFilter", "NameFilter"),
                ExpectedPrefabReferences = GetObjectArray(parameters, "expectedPrefabReferences", "ExpectedPrefabReferences").Select(ParseExpectedPrefabReference).Where(row => row != null).ToArray(),
                RequireAllScannedImagesUseAtlas = GetBool(parameters, false, "requireAllScannedImagesUseAtlas", "RequireAllScannedImagesUseAtlas"),
                IncludeInactive = GetBool(parameters, true, "includeInactive", "IncludeInactive"),
                VerifyAlpha = GetBool(parameters, true, "verifyAlpha", "VerifyAlpha"),
                AlphaThreshold = Clamp01(GetFloat(parameters, 0.02f, "alphaThreshold", "AlphaThreshold")),
                EmptyAlphaCoverageThreshold = Clamp01(GetFloat(parameters, 0.005f, "emptyAlphaCoverageThreshold", "EmptyAlphaCoverageThreshold")),
                MaxPrefabs = Math.Clamp(GetInt(parameters, DefaultMaxPrefabs, "maxPrefabs", "MaxPrefabs"), 1, MaxPrefabLimit),
                MaxSprites = Math.Clamp(GetInt(parameters, DefaultMaxSprites, "maxSprites", "MaxSprites"), 1, MaxSpriteLimit),
                MaxFindings = Math.Clamp(GetInt(parameters, DefaultMaxFindings, "maxFindings", "MaxFindings"), 1, MaxFindingLimit)
            };
        }

        static bool TryValidateAssetPath(string assetPath, out object errorData, out string errorMessage)
        {
            errorData = null;
            errorMessage = null;
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                errorMessage = "assetPath is required.";
                errorData = new { status = "asset_path_required", saveState = BuildReadOnlySaveState() };
                return false;
            }

            if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                !assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "assetPath must be under Assets/ or Packages/.";
                errorData = new { status = "invalid_asset_path", assetPath, saveState = BuildReadOnlySaveState() };
                return false;
            }

            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter || AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath) == null)
            {
                errorMessage = $"Texture asset '{assetPath}' could not be loaded with a TextureImporter.";
                errorData = new { status = "invalid_texture_asset", assetPath, saveState = BuildReadOnlySaveState() };
                return false;
            }

            return true;
        }

        static bool TryResolvePrefabPaths(Request request, out string[] prefabPaths, out object errorData, out string errorMessage)
        {
            errorData = null;
            errorMessage = null;
            var explicitPaths = new List<string>();
            if (!string.IsNullOrWhiteSpace(request.PrefabPath))
                explicitPaths.Add(request.PrefabPath);
            explicitPaths.AddRange(request.PrefabPaths.Where(path => !string.IsNullOrWhiteSpace(path)));
            explicitPaths.AddRange(request.ExpectedPrefabReferences.Select(row => NormalizeAssetPath(row.prefabPath)).Where(path => !string.IsNullOrWhiteSpace(path)));

            if (explicitPaths.Count > 0)
            {
                prefabPaths = explicitPaths
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(request.MaxPrefabs)
                    .ToArray();
                var invalid = prefabPaths
                    .Where(path => !IsPrefabAssetPath(path) || AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                    .ToArray();
                if (invalid.Length > 0)
                {
                    errorMessage = "One or more explicit prefab paths are invalid or could not be loaded.";
                    errorData = new { status = "invalid_prefab_paths", invalidPaths = invalid, saveState = BuildReadOnlySaveState() };
                    return false;
                }

                return true;
            }

            bool shouldScan = request.RequireAllScannedImagesUseAtlas || !string.IsNullOrWhiteSpace(request.NameFilter) || request.UnderWasProvided;
            if (!shouldScan)
            {
                prefabPaths = Array.Empty<string>();
                return true;
            }

            string folder = string.IsNullOrWhiteSpace(request.Under) ? "Assets" : request.Under;
            if (!folder.StartsWith("Assets", StringComparison.OrdinalIgnoreCase) && !folder.StartsWith("Packages", StringComparison.OrdinalIgnoreCase))
            {
                prefabPaths = Array.Empty<string>();
                errorMessage = "under must be a project folder path under Assets/ or Packages/.";
                errorData = new { status = "invalid_scan_root", under = request.Under, saveState = BuildReadOnlySaveState() };
                return false;
            }

            prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(IsPrefabAssetPath)
                .Where(path => string.IsNullOrWhiteSpace(request.NameFilter) || System.IO.Path.GetFileNameWithoutExtension(path).IndexOf(request.NameFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Take(request.MaxPrefabs)
                .ToArray();
            return true;
        }

        static ExpectedSpriteRow ParseExpectedSprite(JObject obj)
        {
            if (obj == null)
                return null;

            return new ExpectedSpriteRow
            {
                name = GetString(obj, "name", "Name"),
                x = GetNullableFloat(obj, "x", "X"),
                y = GetNullableFloat(obj, "y", "Y"),
                width = GetNullableFloat(obj, "width", "Width"),
                height = GetNullableFloat(obj, "height", "Height"),
                pixelsPerUnit = GetNullableFloat(obj, "pixelsPerUnit", "PixelsPerUnit", "ppu", "PPU")
            };
        }

        static ExpectedPrefabReference ParseExpectedPrefabReference(JObject obj)
        {
            if (obj == null)
                return null;

            return new ExpectedPrefabReference
            {
                prefabPath = NormalizeAssetPath(GetString(obj, "prefabPath", "PrefabPath")),
                target = GetString(obj, "target", "Target"),
                targetPath = NormalizePath(GetString(obj, "targetPath", "TargetPath")),
                searchMethod = GetString(obj, "searchMethod", "SearchMethod") ?? "by_id_or_name_or_path",
                expectedSpriteName = GetString(obj, "expectedSpriteName", "ExpectedSpriteName")
            };
        }

        static void CompareStringSetting(VerificationContext context, JObject expected, string name, string actual)
        {
            string expectedValue = GetString(expected, name);
            if (string.IsNullOrWhiteSpace(expectedValue) || string.Equals(expectedValue, actual, StringComparison.OrdinalIgnoreCase))
                return;

            AddFinding(context, "error", "importer_setting_mismatch", $"Importer setting '{name}' was '{actual}' but expected '{expectedValue}'.", expected: expectedValue, actual: actual);
        }

        static void CompareBoolSetting(VerificationContext context, JObject expected, string name, bool actual)
        {
            JToken token = GetToken(expected, name);
            if (token == null || token.Type == JTokenType.Null)
                return;

            bool expectedValue = token.Value<bool>();
            if (expectedValue == actual)
                return;

            AddFinding(context, "error", "importer_setting_mismatch", $"Importer setting '{name}' was '{actual}' but expected '{expectedValue}'.", expected: expectedValue, actual: actual);
        }

        static void CompareFloatSetting(VerificationContext context, JObject expected, string name, float actual)
        {
            float? expectedValue = GetNullableFloat(expected, name);
            if (!expectedValue.HasValue || Math.Abs(actual - expectedValue.Value) <= 0.001f)
                return;

            AddFinding(context, "error", "importer_setting_mismatch", $"Importer setting '{name}' was {actual} but expected {expectedValue.Value}.", expected: expectedValue.Value, actual: actual);
        }

        static void CompareIntSetting(VerificationContext context, JObject expected, string name, int actual)
        {
            int? expectedValue = GetNullableInt(expected, name);
            if (!expectedValue.HasValue || actual == expectedValue.Value)
                return;

            AddFinding(context, "error", "importer_setting_mismatch", $"Importer setting '{name}' was {actual} but expected {expectedValue.Value}.", expected: expectedValue.Value, actual: actual);
        }

        static void CompareRect(VerificationContext context, ExpectedSpriteRow expected, Sprite actual)
        {
            var failures = new List<string>();
            if (expected.x.HasValue && Math.Abs(actual.rect.x - expected.x.Value) > 0.001f)
                failures.Add("x");
            if (expected.y.HasValue && Math.Abs(actual.rect.y - expected.y.Value) > 0.001f)
                failures.Add("y");
            if (expected.width.HasValue && Math.Abs(actual.rect.width - expected.width.Value) > 0.001f)
                failures.Add("width");
            if (expected.height.HasValue && Math.Abs(actual.rect.height - expected.height.Value) > 0.001f)
                failures.Add("height");

            if (failures.Count == 0)
                return;

            AddFinding(
                context,
                "error",
                "sprite_rect_mismatch",
                $"Sprite '{actual.name}' rect mismatched expected field(s): {string.Join(", ", failures)}.",
                spriteName: actual.name,
                expected: new { expected.x, expected.y, expected.width, expected.height },
                actual: DescribeRect(actual.rect));
        }

        static void AddFinding(
            VerificationContext context,
            string severity,
            string kind,
            string message,
            string assetPath = null,
            string prefabPath = null,
            string hierarchyPath = null,
            string componentType = null,
            string propertyPath = null,
            string spriteName = null,
            string expectedSpriteName = null,
            object expected = null,
            object actual = null)
        {
            context.TotalFindingCount++;
            if (context.Findings.Count >= context.Request.MaxFindings)
            {
                context.Truncated = true;
                return;
            }

            context.Findings.Add(new FindingRow
            {
                index = context.TotalFindingCount - 1,
                severity = severity,
                kind = kind,
                message = message,
                assetPath = assetPath ?? context.Request.AssetPath,
                prefabPath = prefabPath,
                hierarchyPath = hierarchyPath,
                componentType = componentType,
                propertyPath = propertyPath,
                spriteName = spriteName,
                expectedSpriteName = expectedSpriteName,
                expected = expected,
                actual = actual
            });
        }

        static double CalculateAlphaCoverage(Rect rect, int textureWidth, int textureHeight, Color32[] pixels, float alphaThreshold)
        {
            RectInt clamped = ClampRect(rect, textureWidth, textureHeight);
            if (clamped.width <= 0 || clamped.height <= 0)
                return 0d;

            int visible = 0;
            int total = 0;
            for (int y = clamped.yMin; y < clamped.yMax; y++)
            {
                for (int x = clamped.xMin; x < clamped.xMax; x++)
                {
                    total++;
                    Color32 pixel = pixels[(y * textureWidth) + x];
                    if (pixel.a / 255f > alphaThreshold)
                        visible++;
                }
            }

            return total == 0 ? 0d : Math.Round((double)visible / total, 4);
        }

        static RectInt ClampRect(Rect rect, int textureWidth, int textureHeight)
        {
            int xMin = Math.Clamp(Mathf.FloorToInt(rect.x), 0, textureWidth);
            int yMin = Math.Clamp(Mathf.FloorToInt(rect.y), 0, textureHeight);
            int xMax = Math.Clamp(Mathf.CeilToInt(rect.x + rect.width), 0, textureWidth);
            int yMax = Math.Clamp(Mathf.CeilToInt(rect.y + rect.height), 0, textureHeight);
            return new RectInt(xMin, yMin, Math.Max(0, xMax - xMin), Math.Max(0, yMax - yMin));
        }

        static Sprite[] LoadSprites(string assetPath)
        {
            return AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<Sprite>()
                .OrderByDescending(sprite => sprite.rect.y)
                .ThenBy(sprite => sprite.rect.x)
                .ThenBy(sprite => sprite.name, StringComparer.Ordinal)
                .ToArray();
        }

        static string[] ExpectedNames(Request request)
        {
            if (request.ExpectedSprites.Length > 0)
                return request.ExpectedSprites
                    .Select(row => row.name)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();

            return request.ExpectedSpriteNames ?? Array.Empty<string>();
        }

        static object DescribeImporter(TextureImporter importer, int importedSpriteCount)
        {
            if (importer == null)
                return null;

            return new
            {
                textureType = importer.textureType.ToString(),
                spriteImportMode = importer.spriteImportMode.ToString(),
                alphaIsTransparency = importer.alphaIsTransparency,
                mipmapEnabled = importer.mipmapEnabled,
                filterMode = importer.filterMode.ToString(),
                compression = importer.textureCompression.ToString(),
                wrapMode = importer.wrapMode.ToString(),
                pixelsPerUnit = importer.spritePixelsPerUnit,
                maxTextureSize = importer.maxTextureSize,
                npotScale = importer.npotScale.ToString(),
                isReadable = importer.isReadable,
                importedSpriteCount
            };
        }

        static object DescribeTextureDimensions(TextureImporter importer, Texture2D texture)
        {
            int? sourceWidth = null;
            int? sourceHeight = null;
            try
            {
                MethodInfo method = typeof(TextureImporter).GetMethod(
                    "GetSourceTextureWidthAndHeight",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method != null)
                {
                    object[] args = { 0, 0 };
                    method.Invoke(importer, args);
                    sourceWidth = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
                    sourceHeight = Convert.ToInt32(args[1], CultureInfo.InvariantCulture);
                }
            }
            catch
            {
                sourceWidth = null;
                sourceHeight = null;
            }

            bool sourceAvailable = sourceWidth.HasValue && sourceHeight.HasValue && sourceWidth.Value > 0 && sourceHeight.Value > 0;
            return new
            {
                imported = new { width = texture.width, height = texture.height },
                source = sourceAvailable ? new { width = sourceWidth.Value, height = sourceHeight.Value } : null,
                sourceAvailable,
                importedMatchesSource = sourceAvailable ? sourceWidth.Value == texture.width && sourceHeight.Value == texture.height : (bool?)null
            };
        }

        static object DescribeRect(Rect rect)
        {
            return new
            {
                x = rect.x,
                y = rect.y,
                width = rect.width,
                height = rect.height
            };
        }

        static object BuildCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            TruncateArray(root, "sprites", 60);
            TruncateArray(root, "prefabSummaries", 30);
            TruncateArray(root, "prefabReferences", 80);
            TruncateArray(root, "findings", 80);
            if (root["dirtyEvidence"] is JObject dirtyEvidence)
            {
                dirtyEvidence.Remove("sceneDirtyBefore");
                dirtyEvidence.Remove("sceneDirtyAfter");
            }

            return root;
        }

        static void TruncateArray(JObject root, string propertyName, int maxRows)
        {
            if (root[propertyName] is not JArray array || array.Count <= maxRows)
                return;

            root[$"{propertyName}OmittedCount"] = array.Count - maxRows;
            root[propertyName] = new JArray(array.Take(maxRows));
        }

        static Dictionary<string, int> Count(IEnumerable<FindingRow> findings, Func<FindingRow, string> selector)
        {
            return findings
                .GroupBy(selector, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        }

        static object BuildReadOnlySaveState()
        {
            return new
            {
                requested = false,
                attempted = false,
                saved = false,
                message = "not_requested_read_only_sprite_slice_reference_verification"
            };
        }

        static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null)
                return string.Empty;

            var parts = new Stack<string>();
            Transform current = target;
            while (current != null)
            {
                parts.Push(current.name);
                if (current == root)
                    break;
                current = current.parent;
            }

            return string.Join("/", parts);
        }

        static bool IsPrefabAssetPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
                (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase));
        }

        static string NormalizeAssetPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string path = value.Trim().Replace('\\', '/');
            if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                return path;

            if (path.StartsWith("/", StringComparison.Ordinal))
                path = path.TrimStart('/');

            return "Assets/" + path;
        }

        static string NormalizeFolderPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Assets";

            string path = value.Trim().Replace('\\', '/').TrimEnd('/');
            if (path.StartsWith("Assets", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("Packages", StringComparison.OrdinalIgnoreCase))
                return path;

            return "Assets/" + path.TrimStart('/');
        }

        static string NormalizePath(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Replace('\\', '/').Trim('/');
        }

        static float Clamp01(float value)
        {
            if (float.IsNaN(value))
                return 0f;
            return Mathf.Clamp01(value);
        }

        static JToken GetToken(JObject obj, params string[] names)
        {
            if (obj == null)
                return null;

            foreach (string name in names)
            {
                if (obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token))
                    return token;
            }

            return null;
        }

        static string GetString(JObject obj, params string[] names)
        {
            JToken token = GetToken(obj, names);
            return token == null || token.Type == JTokenType.Null ? null : token.ToString();
        }

        static int GetInt(JObject obj, int fallback, params string[] names)
        {
            JToken token = GetToken(obj, names);
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<int>();
        }

        static int? GetNullableInt(JObject obj, params string[] names)
        {
            JToken token = GetToken(obj, names);
            return token == null || token.Type == JTokenType.Null ? null : token.Value<int>();
        }

        static float GetFloat(JObject obj, float fallback, params string[] names)
        {
            JToken token = GetToken(obj, names);
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<float>();
        }

        static float? GetNullableFloat(JObject obj, params string[] names)
        {
            JToken token = GetToken(obj, names);
            return token == null || token.Type == JTokenType.Null ? null : token.Value<float>();
        }

        static bool GetBool(JObject obj, bool fallback, params string[] names)
        {
            JToken token = GetToken(obj, names);
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<bool>();
        }

        static string[] GetStringArray(JObject obj, params string[] names)
        {
            JToken token = GetToken(obj, names);
            if (token is JArray array)
            {
                return array
                    .Select(item => item?.ToString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();
            }

            return Array.Empty<string>();
        }

        static JObject[] GetObjectArray(JObject obj, params string[] names)
        {
            JToken token = GetToken(obj, names);
            if (token is JArray array)
                return array.OfType<JObject>().ToArray();

            return Array.Empty<JObject>();
        }
    }
}
