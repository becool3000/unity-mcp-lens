#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils;
using Becool.UnityMcpLens.Editor.Utils.Graphics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class AssetSpriteSheetVisualDiagnosticsTools
    {
        const string ToolName = "Unity.Asset.SpriteSheetVisualDiagnostics";

        sealed class Request
        {
            public string AssetPath;
            public int FrameCount;
            public int FrameWidth;
            public int FrameHeight;
            public int Columns;
            public int Rows;
            public int PaddingX;
            public int PaddingY;
            public int OffsetX;
            public int OffsetY;
            public string SpriteNamePrefix;
            public string[] ExpectedSpriteNames = Array.Empty<string>();
            public float AlphaThreshold = 0.02f;
            public float EmptyAlphaCoverageThreshold = 0.005f;
            public float OversizedPaddingRatio = 0.35f;
            public float MinUsableAreaCoverage = 0.25f;
            public float TextArtifactSensitivity = 0.75f;
            public int MaxCells = 256;
        }

        sealed class CellPlan
        {
            public int Index;
            public string Name;
            public string ExpectedName;
            public Rect Rect;
            public string Source;
        }

        sealed class BandTextMetrics
        {
            public float Score;
            public float AlphaCoverage;
            public float EdgeDensity;
            public float HighContrastInkFraction;
        }

        [McpSchema(ToolName)]
        public static object GetSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    assetPath = new { type = "string", description = "Texture asset path under Assets/ or Packages/." },
                    frameCount = new { type = "integer", description = "Optional expected frame count. If omitted, imported sprite metadata is used when available." },
                    frameWidth = new { type = "integer", description = "Optional frame/cell width in imported texture pixels." },
                    frameHeight = new { type = "integer", description = "Optional frame/cell height in imported texture pixels." },
                    columns = new { type = "integer", description = "Optional expected sprite-sheet columns. Can infer frameWidth when frameWidth is omitted." },
                    rows = new { type = "integer", description = "Optional expected sprite-sheet rows. Can infer frameHeight when frameHeight is omitted." },
                    paddingX = new { type = "integer", description = "Horizontal pixels between planned cells. Defaults to 0." },
                    paddingY = new { type = "integer", description = "Vertical pixels between planned cells. Defaults to 0." },
                    offsetX = new { type = "integer", description = "Left offset before the first planned cell. Defaults to 0." },
                    offsetY = new { type = "integer", description = "Top offset before the first planned cell. Defaults to 0." },
                    spriteNamePrefix = new { type = "string", description = "Optional prefix used to generate expected sprite names when expectedSpriteNames is omitted." },
                    expectedSpriteNames = new { type = "array", description = "Optional expected ordered sprite names.", items = new { type = "string" } },
                    alphaThreshold = new { type = "number", description = "Alpha threshold used to decide visible pixels. Defaults to 0.02." },
                    emptyAlphaCoverageThreshold = new { type = "number", description = "Cells at or below this visible-pixel coverage are reported empty. Defaults to 0.005." },
                    oversizedPaddingRatio = new { type = "number", description = "Padding ratio on any side that flags oversized padding. Defaults to 0.35." },
                    minUsableAreaCoverage = new { type = "number", description = "Minimum alpha-bounds area coverage before a non-empty cell is flagged visually tiny. Defaults to 0.25." },
                    textArtifactSensitivity = new { type = "number", description = "0..1 threshold for likely text-artifact heuristics. Defaults to 0.75." },
                    maxCells = new { type = "integer", description = "Maximum cells to analyze and return. Defaults to 256 and is capped at 2048." }
                },
                required = new[] { "assetPath" }
            };
        }

        [McpTool(ToolName,
            "Diagnoses sprite-sheet visual cells by reading texture alpha bounds, empty cells, oversized transparent padding, likely text artifacts, expected sprite names, and importer settings.",
            "Sprite Sheet Visual Diagnostics",
            Groups = new[] { "assets", "diagnostics" },
            EnabledByDefault = true)]
        public static object SpriteSheetVisualDiagnostics(JObject @params)
        {
            @params ??= new JObject();
            var timing = new ToolOperationTiming(ToolName, "sprite_sheet_visual_diagnostics", PayloadBudgeting.GetUtf8ByteCount(@params.ToString(Formatting.None)));
            bool success = true;
            string errorKind = null;
            object data;

            try
            {
                Request request;
                using (timing.Measure("normalization"))
                {
                    request = Normalize(@params);
                }

                using (timing.Measure("service"))
                {
                    data = BuildDiagnostics(request);
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
                    ? Response.Success("Sprite-sheet visual diagnostics completed.", ToolResultCompactor.ShapeStructuredPayload(
                        ToolName,
                        data,
                        BuildCompactData(data),
                        new { kind = "asset_sprite_sheet_visual_diagnostics_full_result" },
                        "asset_sprite_sheet_visual_diagnostics",
                        detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes))
                    : Response.Error("ASSET_SPRITE_SHEET_VISUAL_DIAGNOSTICS_FAILED", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, success ? null : errorKind);
            return response;
        }

        static Request Normalize(JObject parameters)
        {
            return new Request
            {
                AssetPath = NormalizeAssetPath(GetString(parameters, "assetPath", "AssetPath", "path", "Path")),
                FrameCount = Math.Max(0, GetInt(parameters, 0, "frameCount", "FrameCount")),
                FrameWidth = Math.Max(0, GetInt(parameters, 0, "frameWidth", "FrameWidth", "cellWidth", "CellWidth")),
                FrameHeight = Math.Max(0, GetInt(parameters, 0, "frameHeight", "FrameHeight", "cellHeight", "CellHeight")),
                Columns = Math.Max(0, GetInt(parameters, 0, "columns", "Columns")),
                Rows = Math.Max(0, GetInt(parameters, 0, "rows", "Rows")),
                PaddingX = Math.Max(0, GetInt(parameters, 0, "paddingX", "PaddingX")),
                PaddingY = Math.Max(0, GetInt(parameters, 0, "paddingY", "PaddingY")),
                OffsetX = Math.Max(0, GetInt(parameters, 0, "offsetX", "OffsetX")),
                OffsetY = Math.Max(0, GetInt(parameters, 0, "offsetY", "OffsetY")),
                SpriteNamePrefix = GetString(parameters, "spriteNamePrefix", "SpriteNamePrefix"),
                ExpectedSpriteNames = GetStringArray(parameters, "expectedSpriteNames", "ExpectedSpriteNames"),
                AlphaThreshold = Clamp01(GetFloat(parameters, 0.02f, "alphaThreshold", "AlphaThreshold")),
                EmptyAlphaCoverageThreshold = Clamp01(GetFloat(parameters, 0.005f, "emptyAlphaCoverageThreshold", "EmptyAlphaCoverageThreshold")),
                OversizedPaddingRatio = Clamp01(GetFloat(parameters, 0.35f, "oversizedPaddingRatio", "OversizedPaddingRatio")),
                MinUsableAreaCoverage = Clamp01(GetFloat(parameters, 0.25f, "minUsableAreaCoverage", "MinUsableAreaCoverage")),
                TextArtifactSensitivity = Clamp01(GetFloat(parameters, 0.75f, "textArtifactSensitivity", "TextArtifactSensitivity")),
                MaxCells = Math.Clamp(GetInt(parameters, 256, "maxCells", "MaxCells"), 1, 2048)
            };
        }

        static object BuildDiagnostics(Request request)
        {
            if (string.IsNullOrWhiteSpace(request.AssetPath))
                throw new InvalidOperationException("assetPath is required.");

            if (!request.AssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                !request.AssetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"assetPath must be under Assets/ or Packages/. Got '{request.AssetPath}'.");
            }

            TextureImporter importer = AssetImporter.GetAtPath(request.AssetPath) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException($"Texture importer not found for '{request.AssetPath}'.");

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(request.AssetPath);
            if (texture == null)
                throw new InvalidOperationException($"Texture asset '{request.AssetPath}' could not be loaded.");

            Sprite[] importedSprites = LoadSprites(request.AssetPath);
#pragma warning disable CS0618
            SpriteMetaData[] importerMetadata = importer.spritesheet ?? Array.Empty<SpriteMetaData>();
#pragma warning restore CS0618
            CellPlan[] plannedCells = BuildCellPlan(request, texture, importerMetadata, importedSprites, out object cellFit);
            CellPlan[] analyzedCells = plannedCells.Take(request.MaxCells).ToArray();

            Texture2D readableTexture = texture.ReadableCopy();
            Color32[] pixels = readableTexture.GetPixels32();
            try
            {
                var cellRows = analyzedCells
                    .Select(cell => AnalyzeCell(cell, request, texture.width, texture.height, pixels))
                    .ToArray();
                var rowTokens = cellRows.Select(row => JObject.FromObject(row)).ToArray();
                var warnings = BuildWarnings(request, importer, texture, importedSprites, importerMetadata, plannedCells, rowTokens);

                return new
                {
                    status = "ready",
                    assetPath = request.AssetPath,
                    textureGuid = AssetDatabase.AssetPathToGUID(request.AssetPath),
                    textureName = texture.name,
                    textureSize = new { width = texture.width, height = texture.height },
                    textureDimensions = DescribeTextureDimensions(importer, texture),
                    importer = DescribeImporter(importer, importedSprites.Length, importerMetadata.Length),
                    requested = DescribeRequest(request),
                    cellFit,
                    plannedCellCount = plannedCells.Length,
                    analyzedCellCount = analyzedCells.Length,
                    omittedCellCount = Math.Max(0, plannedCells.Length - analyzedCells.Length),
                    importedSpriteCount = importedSprites.Length,
                    importerMetadataCellCount = importerMetadata.Length,
                    expectedSpriteNameCount = request.ExpectedSpriteNames.Length,
                    emptyCellCount = rowTokens.Count(row => row["empty"]?.Value<bool>() == true),
                    oversizedPaddingCellCount = rowTokens.Count(row => row["oversizedPadding"]?.Value<bool>() == true),
                    visuallyTinyCellCount = rowTokens.Count(row => row["visuallyTiny"]?.Value<bool>() == true),
                    likelyTextArtifactCellCount = rowTokens.Count(row => row["likelyTextArtifact"]?.Value<bool>() == true),
                    nameMismatchCount = rowTokens.Count(row => row["nameMatchesExpected"]?.Type == JTokenType.Boolean && row["nameMatchesExpected"]?.Value<bool>() == false),
                    warnings,
                    cells = cellRows
                };
            }
            finally
            {
                if (!object.ReferenceEquals(readableTexture, texture))
                    UnityEngine.Object.DestroyImmediate(readableTexture);
            }
        }

        static CellPlan[] BuildCellPlan(Request request, Texture2D texture, SpriteMetaData[] importerMetadata, Sprite[] importedSprites, out object cellFit)
        {
            if (request.FrameWidth > 0 || request.FrameHeight > 0 || request.Columns > 0 || request.Rows > 0 || request.FrameCount > 0)
                return BuildRequestedGridPlan(request, texture, importedSprites, out cellFit);

            if (importerMetadata != null && importerMetadata.Length > 0)
            {
                var metadataCells = importerMetadata
                    .OrderByDescending(metadata => metadata.rect.y)
                    .ThenBy(metadata => metadata.rect.x)
                    .Select((metadata, index) => new CellPlan
                    {
                        Index = index,
                        Name = metadata.name,
                        ExpectedName = GetExpectedName(request, index, importerMetadata.Length, metadata.name),
                        Rect = metadata.rect,
                        Source = "importer_spritesheet"
                    })
                    .ToArray();
                cellFit = new
                {
                    source = "importer_spritesheet",
                    plannedCount = metadataCells.Length,
                    columns = CountDistinct(metadataCells.Select(cell => Mathf.RoundToInt(cell.Rect.x))),
                    rows = CountDistinct(metadataCells.Select(cell => Mathf.RoundToInt(cell.Rect.y)))
                };
                return metadataCells;
            }

            if (importedSprites != null && importedSprites.Length > 0)
            {
                var spriteCells = importedSprites
                    .Select((sprite, index) => new CellPlan
                    {
                        Index = index,
                        Name = sprite.name,
                        ExpectedName = GetExpectedName(request, index, importedSprites.Length, sprite.name),
                        Rect = sprite.rect,
                        Source = "imported_sprite_subassets"
                    })
                    .ToArray();
                cellFit = new
                {
                    source = "imported_sprite_subassets",
                    plannedCount = spriteCells.Length,
                    columns = CountDistinct(spriteCells.Select(cell => Mathf.RoundToInt(cell.Rect.x))),
                    rows = CountDistinct(spriteCells.Select(cell => Mathf.RoundToInt(cell.Rect.y)))
                };
                return spriteCells;
            }

            cellFit = new
            {
                source = "whole_texture_fallback",
                plannedCount = 1,
                columns = 1,
                rows = 1
            };
            return new[]
            {
                new CellPlan
                {
                    Index = 0,
                    Name = texture.name,
                    ExpectedName = GetExpectedName(request, 0, 1, texture.name),
                    Rect = new Rect(0, 0, texture.width, texture.height),
                    Source = "whole_texture_fallback"
                }
            };
        }

        static CellPlan[] BuildRequestedGridPlan(Request request, Texture2D texture, Sprite[] importedSprites, out object cellFit)
        {
            int frameWidth = request.FrameWidth;
            int frameHeight = request.FrameHeight;
            int columns = request.Columns;
            int rows = request.Rows;

            if (frameWidth <= 0 && columns > 0)
                frameWidth = Math.Max(1, (texture.width - request.OffsetX - Math.Max(0, columns - 1) * request.PaddingX) / columns);
            if (frameHeight <= 0 && rows > 0)
                frameHeight = Math.Max(1, (texture.height - request.OffsetY - Math.Max(0, rows - 1) * request.PaddingY) / rows);

            if (frameWidth <= 0 || frameHeight <= 0)
                throw new InvalidOperationException("frameWidth/frameHeight are required unless importer sprite metadata or imported sprite subassets exist.");

            int stepX = Math.Max(1, frameWidth + request.PaddingX);
            int stepY = Math.Max(1, frameHeight + request.PaddingY);
            int startX = Math.Max(0, request.OffsetX);
            int startY = texture.height - Math.Max(0, request.OffsetY) - frameHeight;
            int capacityColumns = startX + frameWidth <= texture.width
                ? ((texture.width - startX - frameWidth) / stepX) + 1
                : 0;
            int capacityRows = startY >= 0
                ? (startY / stepY) + 1
                : 0;
            int effectiveColumns = columns > 0 ? Math.Min(columns, capacityColumns) : capacityColumns;
            int effectiveRows = rows > 0 ? Math.Min(rows, capacityRows) : capacityRows;
            int capacity = Math.Max(0, effectiveColumns) * Math.Max(0, effectiveRows);
            int requestedCount = request.FrameCount > 0 ? request.FrameCount : capacity;
            int plannedCount = Math.Min(requestedCount, capacity);
            var cells = new List<CellPlan>();
            string prefix = string.IsNullOrWhiteSpace(request.SpriteNamePrefix) ? texture.name : request.SpriteNamePrefix.Trim();
            int nameDigits = Math.Max(2, requestedCount.ToString(CultureInfo.InvariantCulture).Length);

            for (int row = 0; row < effectiveRows && cells.Count < plannedCount; row++)
            {
                int y = startY - row * stepY;
                for (int column = 0; column < effectiveColumns && cells.Count < plannedCount; column++)
                {
                    int index = cells.Count;
                    int x = startX + column * stepX;
                    var rect = new Rect(x, y, frameWidth, frameHeight);
                    Sprite matchingSprite = FindSpriteByRect(importedSprites, rect);
                    string plannedName = $"{prefix}_{index.ToString($"D{nameDigits}", CultureInfo.InvariantCulture)}";
                    cells.Add(new CellPlan
                    {
                        Index = index,
                        Name = matchingSprite != null ? matchingSprite.name : plannedName,
                        ExpectedName = GetExpectedName(request, index, requestedCount, plannedName),
                        Rect = rect,
                        Source = "requested_grid"
                    });
                }
            }

            cellFit = new
            {
                source = "requested_grid",
                requestedCount,
                plannedCount = cells.Count,
                fitsRequested = cells.Count >= requestedCount,
                capacity,
                columns = effectiveColumns,
                rows = effectiveRows,
                step = new { x = stepX, y = stepY },
                start = new { x = startX, y = startY },
                frame = new { width = frameWidth, height = frameHeight }
            };
            return cells.ToArray();
        }

        static object AnalyzeCell(CellPlan cell, Request request, int textureWidth, int textureHeight, Color32[] pixels)
        {
            RectInt rect = ClampRect(cell.Rect, textureWidth, textureHeight);
            int totalPixels = Math.Max(0, rect.width * rect.height);
            int alphaPixelCount = 0;
            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;

            for (int y = 0; y < rect.height; y++)
            {
                int py = rect.y + y;
                for (int x = 0; x < rect.width; x++)
                {
                    int px = rect.x + x;
                    Color32 color = pixels[py * textureWidth + px];
                    if (color.a / 255f <= request.AlphaThreshold)
                        continue;

                    alphaPixelCount++;
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }

            float alphaCoverage = totalPixels == 0 ? 0f : (float)alphaPixelCount / totalPixels;
            bool empty = alphaPixelCount == 0 || alphaCoverage <= request.EmptyAlphaCoverageThreshold;
            int boundsWidth = empty ? 0 : maxX - minX + 1;
            int boundsHeight = empty ? 0 : maxY - minY + 1;
            int boundsArea = boundsWidth * boundsHeight;
            float usableAreaCoverage = totalPixels == 0 ? 0f : (float)boundsArea / totalPixels;
            float maxPaddingRatio = empty || rect.width <= 0 || rect.height <= 0
                ? 1f
                : new[]
                {
                    minX / (float)rect.width,
                    (rect.width - 1 - maxX) / (float)rect.width,
                    minY / (float)rect.height,
                    (rect.height - 1 - maxY) / (float)rect.height
                }.Max();
            bool visuallyTiny = !empty && usableAreaCoverage < request.MinUsableAreaCoverage;
            bool oversizedPadding = !empty && (visuallyTiny || maxPaddingRatio >= request.OversizedPaddingRatio);
            BandTextMetrics topBand = AnalyzeBand(rect, pixels, textureWidth, topBand: true, request);
            BandTextMetrics bottomBand = AnalyzeBand(rect, pixels, textureWidth, topBand: false, request);
            float textScore = Math.Max(topBand.Score, bottomBand.Score);
            bool likelyTextArtifact = !empty && textScore >= request.TextArtifactSensitivity;
            bool? nameMatchesExpected = string.IsNullOrWhiteSpace(cell.ExpectedName)
                ? null
                : string.Equals(cell.Name, cell.ExpectedName, StringComparison.Ordinal);

            return new
            {
                index = cell.Index,
                source = cell.Source,
                name = cell.Name,
                expectedName = cell.ExpectedName,
                nameMatchesExpected,
                rect = new { x = cell.Rect.x, y = cell.Rect.y, width = cell.Rect.width, height = cell.Rect.height },
                pixelRect = new { x = rect.x, y = rect.y, width = rect.width, height = rect.height },
                empty,
                alphaPixelCount,
                alphaCoverage = Round(alphaCoverage),
                alphaBounds = empty ? null : new
                {
                    relative = new { x = minX, y = minY, width = boundsWidth, height = boundsHeight },
                    absolute = new { x = rect.x + minX, y = rect.y + minY, width = boundsWidth, height = boundsHeight }
                },
                usableAreaCoverage = Round(usableAreaCoverage),
                transparentPadding = empty ? null : new
                {
                    left = minX,
                    right = rect.width - 1 - maxX,
                    bottom = minY,
                    top = rect.height - 1 - maxY,
                    maxRatio = Round(maxPaddingRatio)
                },
                visuallyTiny,
                oversizedPadding,
                likelyTextArtifact,
                textArtifact = new
                {
                    score = Round(textScore),
                    threshold = request.TextArtifactSensitivity,
                    topBand = DescribeBand(topBand),
                    bottomBand = DescribeBand(bottomBand)
                }
            };
        }

        static BandTextMetrics AnalyzeBand(RectInt rect, Color32[] pixels, int textureWidth, bool topBand, Request request)
        {
            if (rect.width <= 1 || rect.height <= 1)
                return new BandTextMetrics();

            int bandHeight = Math.Clamp(Mathf.CeilToInt(rect.height * 0.22f), 3, rect.height);
            int startY = topBand ? rect.height - bandHeight : 0;
            int area = rect.width * bandHeight;
            int alphaPixels = 0;
            int highContrastInk = 0;
            int transitions = 0;
            int possibleTransitions = Math.Max(1, (rect.width - 1) * bandHeight + rect.width * Math.Max(0, bandHeight - 1));

            for (int localY = startY; localY < startY + bandHeight; localY++)
            {
                int py = rect.y + localY;
                for (int localX = 0; localX < rect.width; localX++)
                {
                    int px = rect.x + localX;
                    Color32 color = pixels[py * textureWidth + px];
                    bool ink = color.a / 255f > request.AlphaThreshold;
                    if (ink)
                    {
                        alphaPixels++;
                        if (IsHighContrastInk(color))
                            highContrastInk++;
                    }

                    if (localX > 0)
                    {
                        Color32 left = pixels[py * textureWidth + px - 1];
                        if (IsEdge(color, left, request.AlphaThreshold))
                            transitions++;
                    }

                    if (localY > startY)
                    {
                        Color32 down = pixels[(py - 1) * textureWidth + px];
                        if (IsEdge(color, down, request.AlphaThreshold))
                            transitions++;
                    }
                }
            }

            float alphaCoverage = area == 0 ? 0f : (float)alphaPixels / area;
            float edgeDensity = transitions / (float)possibleTransitions;
            float highContrastFraction = alphaPixels == 0 ? 0f : highContrastInk / (float)alphaPixels;
            bool inkBand = alphaCoverage >= 0.01f && alphaCoverage <= 0.45f;
            float score = inkBand
                ? Clamp01((edgeDensity / 0.18f * 0.55f) + (alphaCoverage / 0.12f * 0.25f) + (highContrastFraction * 0.2f))
                : 0f;

            return new BandTextMetrics
            {
                Score = score,
                AlphaCoverage = alphaCoverage,
                EdgeDensity = edgeDensity,
                HighContrastInkFraction = highContrastFraction
            };
        }

        static object[] BuildWarnings(Request request, TextureImporter importer, Texture2D texture, Sprite[] importedSprites, SpriteMetaData[] metadata, CellPlan[] cells, JObject[] rowTokens)
        {
            var warnings = new List<object>();
            if (importer.textureType != TextureImporterType.Sprite)
                warnings.Add(new { kind = "importer_texture_type", message = $"Texture importer type is {importer.textureType}, not Sprite." });
            if (importer.spriteImportMode != SpriteImportMode.Multiple && cells.Length > 1)
                warnings.Add(new { kind = "sprite_import_mode", message = $"Texture importer mode is {importer.spriteImportMode}, not Multiple." });

            var dimensions = JObject.FromObject(DescribeTextureDimensions(importer, texture));
            if (dimensions["importedMatchesSource"]?.Type == JTokenType.Boolean && dimensions["importedMatchesSource"]?.Value<bool>() == false)
                warnings.Add(new { kind = "source_imported_size_mismatch", message = "Imported texture dimensions differ from source dimensions; diagnostics use imported pixels." });

            int emptyCount = rowTokens.Count(row => row["empty"]?.Value<bool>() == true);
            int tinyCount = rowTokens.Count(row => row["visuallyTiny"]?.Value<bool>() == true);
            int textCount = rowTokens.Count(row => row["likelyTextArtifact"]?.Value<bool>() == true);
            int mismatchCount = rowTokens.Count(row => row["nameMatchesExpected"]?.Type == JTokenType.Boolean && row["nameMatchesExpected"]?.Value<bool>() == false);
            if (emptyCount > 0)
                warnings.Add(new { kind = "empty_cells", message = $"{emptyCount} analyzed cell(s) appear empty at alpha threshold {request.AlphaThreshold:0.###}." });
            if (tinyCount > 0)
                warnings.Add(new { kind = "visually_tiny_cells", message = $"{tinyCount} analyzed cell(s) have small usable alpha bounds relative to their frame." });
            if (textCount > 0)
                warnings.Add(new { kind = "likely_text_artifacts", message = $"{textCount} analyzed cell(s) have banded high-frequency marks that may be generated text artifacts." });
            if (mismatchCount > 0)
                warnings.Add(new { kind = "sprite_name_mismatch", message = $"{mismatchCount} analyzed cell(s) do not match expected sprite names." });
            if (metadata.Length == 0 && importedSprites.Length == 0 && request.FrameWidth <= 0 && request.FrameHeight <= 0)
                warnings.Add(new { kind = "whole_texture_fallback", message = "No importer sprite metadata or sprite subassets were found; diagnosed the whole texture as one cell." });

            return warnings.ToArray();
        }

        static object DescribeRequest(Request request)
        {
            return new
            {
                frameCount = request.FrameCount,
                frameWidth = request.FrameWidth,
                frameHeight = request.FrameHeight,
                columns = request.Columns,
                rows = request.Rows,
                paddingX = request.PaddingX,
                paddingY = request.PaddingY,
                offsetX = request.OffsetX,
                offsetY = request.OffsetY,
                spriteNamePrefix = request.SpriteNamePrefix,
                expectedSpriteNames = request.ExpectedSpriteNames,
                alphaThreshold = request.AlphaThreshold,
                emptyAlphaCoverageThreshold = request.EmptyAlphaCoverageThreshold,
                oversizedPaddingRatio = request.OversizedPaddingRatio,
                minUsableAreaCoverage = request.MinUsableAreaCoverage,
                textArtifactSensitivity = request.TextArtifactSensitivity,
                maxCells = request.MaxCells
            };
        }

        static object DescribeImporter(TextureImporter importer, int importedSpriteCount, int metadataCellCount)
        {
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
                importedSpriteCount,
                metadataCellCount
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
            bool importedMatchesSource = sourceAvailable && sourceWidth.Value == texture.width && sourceHeight.Value == texture.height;
            return new
            {
                imported = new { width = texture.width, height = texture.height },
                source = sourceAvailable ? new { width = sourceWidth.Value, height = sourceHeight.Value } : null,
                sourceAvailable,
                importedMatchesSource = sourceAvailable ? importedMatchesSource : (bool?)null,
                sourceToImportedScale = sourceAvailable
                    ? new
                    {
                        x = texture.width == 0 ? (double?)null : Math.Round((double)sourceWidth.Value / texture.width, 4),
                        y = texture.height == 0 ? (double?)null : Math.Round((double)sourceHeight.Value / texture.height, 4)
                    }
                    : null
            };
        }

        static object DescribeBand(BandTextMetrics metrics)
        {
            return new
            {
                score = Round(metrics.Score),
                alphaCoverage = Round(metrics.AlphaCoverage),
                edgeDensity = Round(metrics.EdgeDensity),
                highContrastInkFraction = Round(metrics.HighContrastInkFraction)
            };
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

        static Sprite FindSpriteByRect(Sprite[] sprites, Rect rect)
        {
            if (sprites == null)
                return null;

            return sprites.FirstOrDefault(sprite => Approximately(sprite.rect, rect));
        }

        static string GetExpectedName(Request request, int index, int count, string fallback)
        {
            if (request.ExpectedSpriteNames != null && request.ExpectedSpriteNames.Length > index)
                return request.ExpectedSpriteNames[index];

            if (!string.IsNullOrWhiteSpace(request.SpriteNamePrefix))
            {
                int digits = Math.Max(2, count.ToString(CultureInfo.InvariantCulture).Length);
                return $"{request.SpriteNamePrefix.Trim()}_{index.ToString($"D{digits}", CultureInfo.InvariantCulture)}";
            }

            return fallback;
        }

        static bool IsEdge(Color32 current, Color32 previous, float alphaThreshold)
        {
            bool currentInk = current.a / 255f > alphaThreshold;
            bool previousInk = previous.a / 255f > alphaThreshold;
            if (currentInk != previousInk)
                return true;

            if (!currentInk && !previousInk)
                return false;

            return Math.Abs(Luminance(current) - Luminance(previous)) > 0.35f;
        }

        static bool IsHighContrastInk(Color32 color)
        {
            float luminance = Luminance(color);
            float max = Math.Max(color.r, Math.Max(color.g, color.b)) / 255f;
            float min = Math.Min(color.r, Math.Min(color.g, color.b)) / 255f;
            float saturation = max <= 0.0001f ? 0f : (max - min) / max;
            return (luminance < 0.22f || luminance > 0.82f) && saturation < 0.45f;
        }

        static float Luminance(Color32 color)
        {
            return (0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b) / 255f;
        }

        static int CountDistinct(IEnumerable<int> values)
        {
            return values.Distinct().Count();
        }

        static bool Approximately(Rect left, Rect right)
        {
            return Mathf.Abs(left.x - right.x) < 0.001f &&
                Mathf.Abs(left.y - right.y) < 0.001f &&
                Mathf.Abs(left.width - right.width) < 0.001f &&
                Mathf.Abs(left.height - right.height) < 0.001f;
        }

        static float Clamp01(float value)
        {
            if (float.IsNaN(value))
                return 0f;
            return Mathf.Clamp01(value);
        }

        static double Round(float value)
        {
            return Math.Round(value, 4);
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

        static float GetFloat(JObject obj, float fallback, params string[] names)
        {
            JToken token = GetToken(obj, names);
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<float>();
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

        static object BuildCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            JArray cells = root["cells"] as JArray ?? new JArray();
            return new
            {
                status = root["status"],
                assetPath = root["assetPath"],
                textureGuid = root["textureGuid"],
                textureName = root["textureName"],
                textureSize = root["textureSize"],
                textureDimensions = root["textureDimensions"],
                importer = root["importer"],
                requested = root["requested"],
                cellFit = root["cellFit"],
                plannedCellCount = root["plannedCellCount"],
                analyzedCellCount = root["analyzedCellCount"],
                omittedCellCount = root["omittedCellCount"],
                importedSpriteCount = root["importedSpriteCount"],
                importerMetadataCellCount = root["importerMetadataCellCount"],
                expectedSpriteNameCount = root["expectedSpriteNameCount"],
                emptyCellCount = root["emptyCellCount"],
                oversizedPaddingCellCount = root["oversizedPaddingCellCount"],
                visuallyTinyCellCount = root["visuallyTinyCellCount"],
                likelyTextArtifactCellCount = root["likelyTextArtifactCellCount"],
                nameMismatchCount = root["nameMismatchCount"],
                warnings = root["warnings"],
                cells = cells.Take(25).ToArray(),
                compactOmittedCellCount = Math.Max(0, cells.Count - 25)
            };
        }
    }
}
