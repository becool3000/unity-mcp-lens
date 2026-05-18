#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Becool.UnityMcpLens.Editor.Adapters.Unity;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils;
using Becool.UnityMcpLens.Editor.Utils.Scene;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class GridBoardLayoutTools
    {
        const string PreviewToolName = "Unity.Scene.PreviewGridBoardLayout";
        const string ApplyToolName = "Unity.Scene.ApplyGridBoardLayout";

        sealed class GridBoardLayoutRequest
        {
            public bool PreviewOnly = true;
            public string ScenePath;
            public int BoardWidth;
            public int BoardHeight;
            public string TileComponentType;
            public string GridFieldName = "GridPosition";
            public string GridFieldComponentType;
            public int GridFieldComponentIndex;
            public string Root;
            public string RootSearchMethod = "by_id_or_name_or_path";
            public bool IncludeInactive;
            public string ProjectionType = "orthogonal";
            public Vector3 TileSize = Vector3.one;
            public Vector3 Origin = Vector3.zero;
            public bool UseLocalPosition;
            public string ClassificationFieldName;
            public string ObstacleFieldName;
            public string FloorValue = "floor";
            public string ObstacleValue = "obstacle";
            public int SortingBase;
            public int? FloorSortingBase;
            public int? ObstacleSortingBase;
            public int SortingRowStride = 10;
            public int SortingColumnStride = 10;
            public int SortingZStride = 1;
            public string SortingLayerName;
            public bool ApplySorting = true;
            public string CameraFitMode = "report";
            public string CameraTarget;
            public string CameraSearchMethod = "by_id_or_name_or_path";
            public float DesiredCoverageMin = 0.45f;
            public float DesiredCoverageMax = 0.75f;
            public float AspectRatio = 16f / 9f;
            public int ViewportWidth = 1280;
            public int ViewportHeight = 720;
            public bool SaveScene;
            public int MaxRows = 100;
            public int MaxOverlapSamples = 50;
        }

        sealed class TileLayoutRow
        {
            public GameObject GameObject;
            public Component TileComponent;
            public Vector3Int Cell;
            public object RawGridValue;
            public string GridSource;
            public string Classification;
            public string ClassificationSource;
            public Vector3 CurrentPosition;
            public Vector3 TargetPosition;
            public Bounds? CurrentVisualBounds;
            public Bounds EstimatedVisualBounds;
            public SpriteRenderer[] SpriteRenderers = Array.Empty<SpriteRenderer>();
            public string Error;
        }

        sealed class LayoutAnalysis
        {
            public TileLayoutRow[] Rows = Array.Empty<TileLayoutRow>();
            public TileLayoutRow[] ValidRows = Array.Empty<TileLayoutRow>();
            public string[] MissingTypes = Array.Empty<string>();
            public Bounds BoardBounds;
            public Bounds VisualBounds;
            public bool HasVisualBounds;
            public Vector3 XStep;
            public Vector3 YStep;
            public int BoardWidth;
            public int BoardHeight;
            public int MissingGridCount;
            public int OutOfBoardCount;
            public int MissingSpriteCount;
            public int FloorCount;
            public int ObstacleCount;
            public int UnknownCount;
            public object[] MissingSprites = Array.Empty<object>();
            public object[] SortingSamples = Array.Empty<object>();
            public object[] OverlapSamples = Array.Empty<object>();
            public int OverlapPairCount;
            public int DuplicateCellCount;
            public object[] DuplicateCells = Array.Empty<object>();
        }

        sealed class CameraFit
        {
            public Camera Camera;
            public string CameraPath;
            public float Coverage;
            public float WidthCoverage;
            public float HeightCoverage;
            public bool InDesiredRange;
            public Rect ScreenRect;
            public bool HasScreenRect;
            public float SuggestedOrthographicSize;
            public Vector3 SuggestedPosition;
        }

        [McpSchema(PreviewToolName)]
        public static object GetPreviewSchema() => BuildSchema(includeSaveScene: false);

        [McpSchema(ApplyToolName)]
        public static object GetApplySchema() => BuildSchema(includeSaveScene: true);

        [McpTool(PreviewToolName,
            "Previews a board/isometric layout for scene tile objects found by component query, including bounds, camera coverage, sprite diagnostics, and sorting samples.",
            "Preview Grid Board Layout",
            Groups = new[] { "scene", "diagnostics" },
            EnabledByDefault = true)]
        public static object PreviewGridBoardLayout(JObject @params)
        {
            return Run(@params, previewOnly: true);
        }

        [McpTool(ApplyToolName,
            "Applies a board/isometric layout to scene tile objects found by component query, setting transforms and sprite sorting from grid coordinates.",
            "Apply Grid Board Layout",
            Groups = new[] { "scene", "diagnostics" },
            EnabledByDefault = true)]
        public static object ApplyGridBoardLayout(JObject @params)
        {
            return Run(@params, previewOnly: false);
        }

        static object Run(JObject @params, bool previewOnly)
        {
            string toolName = previewOnly ? PreviewToolName : ApplyToolName;
            @params ??= new JObject();
            var timing = new ToolOperationTiming(toolName, "grid_board_layout", PayloadBudgeting.GetUtf8ByteCount(@params.ToString(Formatting.None)));
            bool success = true;
            string errorKind = null;
            object data;

            try
            {
                GridBoardLayoutRequest request;
                using (timing.Measure("normalization"))
                {
                    request = Normalize(@params, previewOnly);
                }

                using (timing.Measure("service"))
                {
                    data = BuildLayoutData(request);
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
                    ? Response.Success(previewOnly ? "Grid board layout preview completed." : "Grid board layout applied.", ToolResultCompactor.ShapeStructuredPayload(
                        toolName,
                        data,
                        BuildCompactData(data),
                        new { kind = "grid_board_layout_full_result" },
                        "grid_board_layout",
                        detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes))
                    : Response.Error(previewOnly ? "GRID_BOARD_LAYOUT_PREVIEW_FAILED" : "GRID_BOARD_LAYOUT_APPLY_FAILED", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, success ? null : errorKind);
            return response;
        }

        static object BuildSchema(bool includeSaveScene)
        {
            var properties = new Dictionary<string, object>
            {
                ["scenePath"] = new { type = "string", description = "Optional loaded scene name or Assets-relative .unity path. Defaults to the active scene." },
                ["boardWidth"] = new { type = "integer", description = "Board width in grid cells. If omitted or <= 0, inferred from matched tile coordinates." },
                ["boardHeight"] = new { type = "integer", description = "Board height in grid cells. If omitted or <= 0, inferred from matched tile coordinates." },
                ["boardSize"] = new { description = "Optional board size as [width,height] or {width,height}." },
                ["tileComponentType"] = new { type = "string", description = "Component type used to find board tile objects." },
                ["gridFieldName"] = new { type = "string", description = "Serialized field/property path on the tile component that stores grid coordinates. Defaults to GridPosition." },
                ["gridFieldComponentType"] = new { type = "string", description = "Optional component type to read gridFieldName from when it differs from tileComponentType." },
                ["gridFieldComponentIndex"] = new { type = "integer", description = "0-based component index for grid field reads. Defaults to 0." },
                ["root"] = new { type = "string", description = "Optional root GameObject name, path, or id used to scope tile lookup." },
                ["rootSearchMethod"] = new { type = "string", description = "How to find root: by_name, by_path, by_id, or by_id_or_name_or_path. Defaults to by_id_or_name_or_path." },
                ["includeInactive"] = new { type = "boolean", description = "Include inactive tile objects. Defaults to false." },
                ["projectionType"] = new { type = "string", description = "orthogonal, isometric, isometric_z_as_y, or staggered_isometric. Defaults to orthogonal." },
                ["tileSize"] = new { description = "Tile size as a number, [x,y,z], or {x,y,z}. Defaults to 1." },
                ["tileSizeX"] = new { type = "number", description = "Tile width override." },
                ["tileSizeY"] = new { type = "number", description = "Tile height override." },
                ["tileSizeZ"] = new { type = "number", description = "Tile z/depth spacing override." },
                ["origin"] = new { description = "Layout origin as [x,y,z] or {x,y,z}." },
                ["originX"] = new { type = "number", description = "Layout origin X override." },
                ["originY"] = new { type = "number", description = "Layout origin Y override." },
                ["originZ"] = new { type = "number", description = "Layout origin Z override." },
                ["useLocalPosition"] = new { type = "boolean", description = "Write transform.localPosition instead of world position in apply mode. Defaults to false." },
                ["classificationFieldName"] = new { type = "string", description = "Optional field/property used to classify floor versus obstacle tiles." },
                ["obstacleFieldName"] = new { type = "string", description = "Optional boolean field/property where true means obstacle." },
                ["floorValue"] = new { type = "string", description = "Classification value treated as floor. Defaults to floor." },
                ["obstacleValue"] = new { type = "string", description = "Classification value treated as obstacle. Defaults to obstacle." },
                ["sortingBase"] = new { type = "integer", description = "Base SpriteRenderer sorting order. Defaults to 0." },
                ["floorSortingBase"] = new { type = "integer", description = "Optional base sorting order for floor tiles." },
                ["obstacleSortingBase"] = new { type = "integer", description = "Optional base sorting order for obstacle tiles." },
                ["sortingRowStride"] = new { type = "integer", description = "Sorting order added per grid Y row. Defaults to 10." },
                ["sortingColumnStride"] = new { type = "integer", description = "Sorting order added per grid X column. Defaults to 10." },
                ["sortingZStride"] = new { type = "integer", description = "Sorting order added per grid Z layer. Defaults to 1." },
                ["sortingLayerName"] = new { type = "string", description = "Optional SpriteRenderer sorting layer name to assign in apply mode." },
                ["applySorting"] = new { type = "boolean", description = "Set SpriteRenderer sorting order in apply mode. Defaults to true." },
                ["cameraFitMode"] = new { type = "string", description = "none, report, center, or fit_orthographic. Defaults to report." },
                ["cameraTarget"] = new { type = "string", description = "Optional camera GameObject name, path, or id." },
                ["cameraSearchMethod"] = new { type = "string", description = "How to find cameraTarget. Defaults to by_id_or_name_or_path." },
                ["desiredCoverageMin"] = new { type = "number", description = "Minimum acceptable limiting camera coverage as a 0..1 fraction. Defaults to 0.45." },
                ["desiredCoverageMax"] = new { type = "number", description = "Maximum acceptable limiting camera coverage as a 0..1 fraction. Defaults to 0.75." },
                ["aspectRatio"] = new { type = "number", description = "Aspect ratio used for camera coverage. Defaults to 16:9." },
                ["viewportWidth"] = new { type = "integer", description = "Virtual viewport width for coverage reporting. Defaults to 1280." },
                ["viewportHeight"] = new { type = "integer", description = "Virtual viewport height for coverage reporting. Defaults to 720." },
                ["maxRows"] = new { type = "integer", description = "Maximum tile rows returned. Defaults to 100 and is capped at 500." },
                ["maxOverlapSamples"] = new { type = "integer", description = "Maximum overlap pair samples returned. Defaults to 50." }
            };

            if (includeSaveScene)
                properties["saveScene"] = new { type = "boolean", description = "Save the target scene after applying changes. Defaults to false." };

            return new
            {
                type = "object",
                properties,
                required = new[] { "tileComponentType" }
            };
        }

        static GridBoardLayoutRequest Normalize(JObject parameters, bool previewOnly)
        {
            var request = new GridBoardLayoutRequest
            {
                PreviewOnly = previewOnly,
                ScenePath = GetString(parameters, "scenePath", "ScenePath", "scene", "Scene"),
                TileComponentType = GetString(parameters, "tileComponentType", "TileComponentType", "componentType", "ComponentType"),
                GridFieldName = GetString(parameters, "gridFieldName", "GridFieldName") ?? "GridPosition",
                GridFieldComponentType = GetString(parameters, "gridFieldComponentType", "GridFieldComponentType"),
                GridFieldComponentIndex = Math.Max(0, GetInt(parameters, 0, "gridFieldComponentIndex", "GridFieldComponentIndex")),
                Root = GetString(parameters, "root", "Root"),
                RootSearchMethod = GetString(parameters, "rootSearchMethod", "RootSearchMethod") ?? "by_id_or_name_or_path",
                IncludeInactive = GetBool(parameters, false, "includeInactive", "IncludeInactive"),
                ProjectionType = (GetString(parameters, "projectionType", "ProjectionType") ?? "orthogonal").Trim().ToLowerInvariant(),
                UseLocalPosition = GetBool(parameters, false, "useLocalPosition", "UseLocalPosition"),
                ClassificationFieldName = GetString(parameters, "classificationFieldName", "ClassificationFieldName"),
                ObstacleFieldName = GetString(parameters, "obstacleFieldName", "ObstacleFieldName"),
                FloorValue = GetString(parameters, "floorValue", "FloorValue") ?? "floor",
                ObstacleValue = GetString(parameters, "obstacleValue", "ObstacleValue") ?? "obstacle",
                SortingBase = GetInt(parameters, 0, "sortingBase", "SortingBase", "sortingBaseOrder", "SortingBaseOrder"),
                FloorSortingBase = GetNullableInt(parameters, "floorSortingBase", "FloorSortingBase"),
                ObstacleSortingBase = GetNullableInt(parameters, "obstacleSortingBase", "ObstacleSortingBase"),
                SortingRowStride = GetInt(parameters, 10, "sortingRowStride", "SortingRowStride"),
                SortingColumnStride = GetInt(parameters, 10, "sortingColumnStride", "SortingColumnStride"),
                SortingZStride = GetInt(parameters, 1, "sortingZStride", "SortingZStride"),
                SortingLayerName = GetString(parameters, "sortingLayerName", "SortingLayerName"),
                ApplySorting = GetBool(parameters, true, "applySorting", "ApplySorting"),
                CameraFitMode = (GetString(parameters, "cameraFitMode", "CameraFitMode") ?? "report").Trim().ToLowerInvariant(),
                CameraTarget = GetString(parameters, "cameraTarget", "CameraTarget"),
                CameraSearchMethod = GetString(parameters, "cameraSearchMethod", "CameraSearchMethod") ?? "by_id_or_name_or_path",
                DesiredCoverageMin = Clamp01(GetFloat(parameters, 0.45f, "desiredCoverageMin", "DesiredCoverageMin")),
                DesiredCoverageMax = Clamp01(GetFloat(parameters, 0.75f, "desiredCoverageMax", "DesiredCoverageMax")),
                AspectRatio = GetFloat(parameters, 16f / 9f, "aspectRatio", "AspectRatio"),
                ViewportWidth = Math.Clamp(GetInt(parameters, 1280, "viewportWidth", "ViewportWidth"), 64, 8192),
                ViewportHeight = Math.Clamp(GetInt(parameters, 720, "viewportHeight", "ViewportHeight"), 64, 8192),
                SaveScene = !previewOnly && GetBool(parameters, false, "saveScene", "SaveScene"),
                MaxRows = Math.Clamp(GetInt(parameters, 100, "maxRows", "MaxRows"), 1, 500),
                MaxOverlapSamples = Math.Clamp(GetInt(parameters, 50, "maxOverlapSamples", "MaxOverlapSamples"), 0, 500)
            };

            if (string.IsNullOrWhiteSpace(request.TileComponentType))
                throw new InvalidOperationException("tileComponentType is required.");

            (request.BoardWidth, request.BoardHeight) = ReadBoardSize(parameters);
            request.TileSize = ReadVector(parameters, "tileSize", "TileSize", Vector3.one);
            request.TileSize.x = GetFloat(parameters, request.TileSize.x, "tileSizeX", "TileSizeX");
            request.TileSize.y = GetFloat(parameters, request.TileSize.y, "tileSizeY", "TileSizeY");
            request.TileSize.z = GetFloat(parameters, request.TileSize.z, "tileSizeZ", "TileSizeZ");
            if (Mathf.Abs(request.TileSize.x) < 0.0001f)
                request.TileSize.x = 1f;
            if (Mathf.Abs(request.TileSize.y) < 0.0001f)
                request.TileSize.y = 1f;

            request.Origin = ReadVector(parameters, "origin", "Origin", Vector3.zero);
            request.Origin.x = GetFloat(parameters, request.Origin.x, "originX", "OriginX");
            request.Origin.y = GetFloat(parameters, request.Origin.y, "originY", "OriginY");
            request.Origin.z = GetFloat(parameters, request.Origin.z, "originZ", "OriginZ");
            request.AspectRatio = Mathf.Clamp(request.AspectRatio <= 0f ? request.ViewportWidth / (float)request.ViewportHeight : request.AspectRatio, 0.1f, 10f);

            if (request.DesiredCoverageMax < request.DesiredCoverageMin)
                (request.DesiredCoverageMin, request.DesiredCoverageMax) = (request.DesiredCoverageMax, request.DesiredCoverageMin);

            return request;
        }

        static object BuildLayoutData(GridBoardLayoutRequest request)
        {
            object dirtyStateBefore = SceneDirtyStateUtility.CaptureLoadedScenes();
            Scene scene = ResolveLoadedScene(request.ScenePath);
            GameObject rootObject = ResolveRoot(request, scene);
            LayoutAnalysis analysis = AnalyzeLayout(request, scene, rootObject);
            CameraFit cameraFit = BuildCameraFit(request, analysis);
            object apply = request.PreviewOnly ? null : ApplyLayout(request, scene, analysis, cameraFit);

            return new
            {
                status = analysis.ValidRows.Length == 0 ? "incomplete" : "ready",
                previewOnly = request.PreviewOnly,
                scene = new
                {
                    name = scene.name,
                    path = scene.path,
                    isDirty = scene.isDirty
                },
                query = new
                {
                    root = rootObject == null ? null : ToObjectRow(rootObject),
                    tileComponentType = request.TileComponentType,
                    gridFieldName = request.GridFieldName,
                    gridFieldComponentType = string.IsNullOrWhiteSpace(request.GridFieldComponentType) ? request.TileComponentType : request.GridFieldComponentType,
                    includeInactive = request.IncludeInactive,
                    missingTypes = analysis.MissingTypes
                },
                layout = new
                {
                    projectionType = request.ProjectionType,
                    tileSize = ToVector3Object(request.TileSize),
                    tileSpacing = new
                    {
                        xStep = ToVector3Object(analysis.XStep),
                        yStep = ToVector3Object(analysis.YStep),
                        xDistance = analysis.XStep.magnitude,
                        yDistance = analysis.YStep.magnitude
                    },
                    origin = ToVector3Object(request.Origin),
                    useLocalPosition = request.UseLocalPosition
                },
                board = new
                {
                    width = analysis.BoardWidth,
                    height = analysis.BoardHeight,
                    expectedCellCount = analysis.BoardWidth > 0 && analysis.BoardHeight > 0 ? analysis.BoardWidth * analysis.BoardHeight : 0,
                    tileCount = analysis.Rows.Length,
                    validTileCount = analysis.ValidRows.Length,
                    missingGridCount = analysis.MissingGridCount,
                    outOfBoardCount = analysis.OutOfBoardCount,
                    duplicateCellCount = analysis.DuplicateCellCount,
                    duplicateCells = analysis.DuplicateCells,
                    floorCount = analysis.FloorCount,
                    obstacleCount = analysis.ObstacleCount,
                    unknownCount = analysis.UnknownCount,
                    boardBounds = ToBoundsObject(analysis.BoardBounds),
                    visualBounds = analysis.HasVisualBounds ? ToBoundsObject(analysis.VisualBounds) : null
                },
                diagnostics = new
                {
                    missingSpriteCount = analysis.MissingSpriteCount,
                    missingSprites = analysis.MissingSprites,
                    overlappingSpritePairCount = analysis.OverlapPairCount,
                    overlappingSprites = analysis.OverlapSamples,
                    sortingOrderSamples = analysis.SortingSamples
                },
                camera = cameraFit == null ? null : new
                {
                    path = cameraFit.CameraPath,
                    mode = request.CameraFitMode,
                    currentCoveragePercent = cameraFit.Coverage * 100f,
                    widthCoveragePercent = cameraFit.WidthCoverage * 100f,
                    heightCoveragePercent = cameraFit.HeightCoverage * 100f,
                    desiredCoverageRangePercent = new { min = request.DesiredCoverageMin * 100f, max = request.DesiredCoverageMax * 100f },
                    inDesiredRange = cameraFit.InDesiredRange,
                    screenRect = cameraFit.HasScreenRect ? ToRectObject(cameraFit.ScreenRect) : null,
                    suggestedOrthographicSize = cameraFit.SuggestedOrthographicSize,
                    suggestedCameraPosition = ToVector3Object(cameraFit.SuggestedPosition)
                },
                rows = analysis.Rows.Take(request.MaxRows).Select(row => BuildTileRow(request, row)).ToArray(),
                omittedRowCount = Math.Max(0, analysis.Rows.Length - request.MaxRows),
                apply,
                dirtyStateBefore,
                dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                saveState = request.PreviewOnly ? SceneDirtyStateUtility.BuildSaveState() : (apply == null ? SceneDirtyStateUtility.BuildSaveState() : JObject.FromObject(apply)["saveState"])
            };
        }

        static object BuildCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            CompactArray(root, "rows", 50, "compactOmittedRowCount");
            CompactArray(root["diagnostics"] as JObject, "overlappingSprites", 25, "compactOmittedOverlapCount");
            CompactArray(root["diagnostics"] as JObject, "sortingOrderSamples", 25, "compactOmittedSortingSampleCount");
            CompactArray(root["diagnostics"] as JObject, "missingSprites", 25, "compactOmittedMissingSpriteCount");
            return root;
        }

        static void CompactArray(JObject root, string propertyName, int max, string omittedPropertyName)
        {
            if (root == null || root[propertyName] is not JArray array || array.Count <= max)
                return;

            root[propertyName] = new JArray(array.Take(max));
            root[omittedPropertyName] = array.Count - max;
        }

        static LayoutAnalysis AnalyzeLayout(GridBoardLayoutRequest request, Scene scene, GameObject rootObject)
        {
            Type tileType = ResolveRequiredComponentType(request.TileComponentType);
            Type gridType = string.IsNullOrWhiteSpace(request.GridFieldComponentType)
                ? tileType
                : ResolveRequiredComponentType(request.GridFieldComponentType);

            GameObject[] allObjects = UnityApiAdapter.FindObjectsByType<GameObject>(request.IncludeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
            var rows = allObjects
                .Where(go => go != null)
                .Where(go => go.scene == scene)
                .Where(go => request.IncludeInactive || go.activeInHierarchy)
                .Where(go => rootObject == null || go.transform == rootObject.transform || go.transform.IsChildOf(rootObject.transform))
                .Select(go => BuildTileLayoutRow(request, go, tileType, gridType))
                .Where(row => row != null)
                .OrderBy(row => row.Error == null ? 0 : 1)
                .ThenBy(row => UiDiagnosticsHelper.GetHierarchyPath(row.GameObject.transform), StringComparer.Ordinal)
                .ToArray();

            var validRows = rows.Where(row => string.IsNullOrWhiteSpace(row.Error)).ToArray();
            var analysis = new LayoutAnalysis
            {
                Rows = rows,
                ValidRows = validRows,
                XStep = ProjectCell(request, new Vector3Int(1, 0, 0)) - ProjectCell(request, Vector3Int.zero),
                YStep = ProjectCell(request, new Vector3Int(0, 1, 0)) - ProjectCell(request, Vector3Int.zero),
                MissingGridCount = rows.Count(row => !string.IsNullOrWhiteSpace(row.Error))
            };

            analysis.BoardWidth = request.BoardWidth > 0 ? request.BoardWidth : InferBoardExtent(validRows, axis: 0);
            analysis.BoardHeight = request.BoardHeight > 0 ? request.BoardHeight : InferBoardExtent(validRows, axis: 1);
            analysis.OutOfBoardCount = validRows.Count(row => IsOutOfBoard(row.Cell, analysis.BoardWidth, analysis.BoardHeight));
            analysis.FloorCount = validRows.Count(row => row.Classification == "floor");
            analysis.ObstacleCount = validRows.Count(row => row.Classification == "obstacle");
            analysis.UnknownCount = validRows.Count(row => row.Classification != "floor" && row.Classification != "obstacle");

            BuildBounds(analysis);
            BuildSpriteDiagnostics(request, analysis);
            BuildSortingSamples(request, analysis);
            BuildDuplicateCells(analysis);
            BuildOverlapSamples(request, analysis);
            return analysis;
        }

        static TileLayoutRow BuildTileLayoutRow(GridBoardLayoutRequest request, GameObject gameObject, Type tileType, Type gridType)
        {
            Component tileComponent = gameObject.GetComponent(tileType);
            if (tileComponent == null)
                return null;

            Component gridComponent = GetComponentByIndex(gameObject, gridType, request.GridFieldComponentIndex);
            var row = new TileLayoutRow
            {
                GameObject = gameObject,
                TileComponent = tileComponent,
                CurrentPosition = request.UseLocalPosition ? gameObject.transform.localPosition : gameObject.transform.position,
                SpriteRenderers = gameObject.GetComponentsInChildren<SpriteRenderer>(true)
            };

            if (gridComponent == null)
            {
                row.Error = $"Component '{gridType.FullName}' with index {request.GridFieldComponentIndex} was not found.";
                return row;
            }

            if (!TryReadGridPosition(gridComponent, request.GridFieldName, out Vector3Int cell, out object rawValue, out string source, out string error))
            {
                row.Error = error;
                return row;
            }

            row.Cell = cell;
            row.RawGridValue = rawValue;
            row.GridSource = source;
            row.TargetPosition = ProjectCell(request, cell);
            row.CurrentVisualBounds = TryGetRendererBounds(gameObject, out Bounds currentBounds) ? currentBounds : null;
            row.EstimatedVisualBounds = EstimateBoundsAt(row.CurrentVisualBounds, row.CurrentPosition, row.TargetPosition);
            (row.Classification, row.ClassificationSource) = ClassifyTile(request, tileComponent);
            return row;
        }

        static object ApplyLayout(GridBoardLayoutRequest request, Scene scene, LayoutAnalysis analysis, CameraFit cameraFit)
        {
            int movedCount = 0;
            int sortingChangedCount = 0;
            var changedRows = new List<object>();

            foreach (TileLayoutRow row in analysis.ValidRows)
            {
                bool changed = false;
                Transform transform = row.GameObject.transform;
                Vector3 beforePosition = request.UseLocalPosition ? transform.localPosition : transform.position;
                if ((beforePosition - row.TargetPosition).sqrMagnitude > 0.0000001f)
                {
                    Undo.RecordObject(transform, "Apply Grid Board Layout");
                    if (request.UseLocalPosition)
                        transform.localPosition = row.TargetPosition;
                    else
                        transform.position = row.TargetPosition;
                    EditorUtility.SetDirty(transform);
                    movedCount++;
                    changed = true;
                }

                if (request.ApplySorting)
                {
                    foreach (SpriteRenderer renderer in row.SpriteRenderers)
                    {
                        if (renderer == null)
                            continue;

                        int targetSortingOrder = ComputeSortingOrder(request, row);
                        bool rendererChanged = renderer.sortingOrder != targetSortingOrder ||
                            (!string.IsNullOrWhiteSpace(request.SortingLayerName) && renderer.sortingLayerName != request.SortingLayerName);
                        if (!rendererChanged)
                            continue;

                        Undo.RecordObject(renderer, "Apply Grid Board Sorting");
                        renderer.sortingOrder = targetSortingOrder;
                        if (!string.IsNullOrWhiteSpace(request.SortingLayerName))
                            renderer.sortingLayerName = request.SortingLayerName;
                        EditorUtility.SetDirty(renderer);
                        sortingChangedCount++;
                        changed = true;
                    }
                }

                if (changed && changedRows.Count < 100)
                    changedRows.Add(BuildTileRow(request, row));
            }

            object cameraApply = ApplyCameraFit(request, cameraFit);
            bool cameraChanged = JObject.FromObject(cameraApply ?? new { })["changed"]?.Value<bool>() == true;
            bool anyChanged = movedCount > 0 || sortingChangedCount > 0 || cameraChanged;
            if (anyChanged)
                EditorSceneManager.MarkSceneDirty(scene);

            bool saveAttempted = false;
            bool saved = false;
            string saveError = null;
            if (request.SaveScene)
            {
                saveAttempted = true;
                if (string.IsNullOrWhiteSpace(scene.path))
                {
                    saveError = "Cannot save an untitled scene. Save it in Unity first or provide a loaded scenePath.";
                }
                else
                {
                    saved = EditorSceneManager.SaveScene(scene);
                    if (!saved)
                        saveError = $"Unity failed to save scene '{scene.path}'.";
                }
            }

            return new
            {
                movedCount,
                sortingChangedCount,
                changedRowCount = changedRows.Count,
                changedRows,
                camera = cameraApply,
                saveState = SceneDirtyStateUtility.BuildSaveState(
                    requested: request.SaveScene,
                    attempted: saveAttempted,
                    saved: saved,
                    message: saveError ?? (request.SaveScene ? "saved" : anyChanged ? "scene_marked_dirty" : "no_changes"),
                    error: saveError)
            };
        }

        static object ApplyCameraFit(GridBoardLayoutRequest request, CameraFit cameraFit)
        {
            if (cameraFit?.Camera == null || string.Equals(request.CameraFitMode, "none", StringComparison.OrdinalIgnoreCase) || string.Equals(request.CameraFitMode, "report", StringComparison.OrdinalIgnoreCase))
            {
                return new { changed = false, mode = request.CameraFitMode };
            }

            Camera camera = cameraFit.Camera;
            bool changed = false;
            Undo.RecordObject(camera.transform, "Apply Grid Board Camera Fit");
            Undo.RecordObject(camera, "Apply Grid Board Camera Fit");

            if (request.CameraFitMode is "center" or "fit" or "fit_orthographic" or "orthographic")
            {
                if ((camera.transform.position - cameraFit.SuggestedPosition).sqrMagnitude > 0.0000001f)
                {
                    camera.transform.position = cameraFit.SuggestedPosition;
                    changed = true;
                }
            }

            if (request.CameraFitMode is "fit" or "fit_orthographic" or "orthographic")
            {
                if (!camera.orthographic)
                {
                    camera.orthographic = true;
                    changed = true;
                }

                if (Mathf.Abs(camera.orthographicSize - cameraFit.SuggestedOrthographicSize) > 0.0001f)
                {
                    camera.orthographicSize = cameraFit.SuggestedOrthographicSize;
                    changed = true;
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(camera);
                EditorUtility.SetDirty(camera.transform);
            }

            return new
            {
                changed,
                mode = request.CameraFitMode,
                cameraPath = cameraFit.CameraPath,
                orthographic = camera.orthographic,
                orthographicSize = camera.orthographicSize,
                position = ToVector3Object(camera.transform.position)
            };
        }

        static CameraFit BuildCameraFit(GridBoardLayoutRequest request, LayoutAnalysis analysis)
        {
            if (analysis.ValidRows.Length == 0 || string.Equals(request.CameraFitMode, "none", StringComparison.OrdinalIgnoreCase))
                return null;

            Camera camera = ResolveCamera(request, out string cameraPath);
            if (camera == null)
                return null;

            Bounds bounds = analysis.HasVisualBounds ? analysis.VisualBounds : analysis.BoardBounds;
            CameraPlaneProjection plane = ProjectToCameraPlane(camera, bounds);
            bool hasScreenRect = camera.orthographic
                ? TryProjectOrthographic(camera, plane, request.AspectRatio, request.ViewportWidth, request.ViewportHeight, out Rect screenRect)
                : TryProjectPerspective(camera, bounds, request.AspectRatio, request.ViewportWidth, request.ViewportHeight, out screenRect);

            float widthCoverage = hasScreenRect ? screenRect.width / request.ViewportWidth : 0f;
            float heightCoverage = hasScreenRect ? screenRect.height / request.ViewportHeight : 0f;
            float coverage = Mathf.Max(widthCoverage, heightCoverage);
            float desiredCoverage = Mathf.Clamp((request.DesiredCoverageMin + request.DesiredCoverageMax) * 0.5f, 0.05f, 0.95f);
            float planeWidth = Mathf.Max(0.001f, plane.Width);
            float planeHeight = Mathf.Max(0.001f, plane.Height);
            float orthographicSize = Mathf.Max(
                planeHeight / (2f * desiredCoverage),
                planeWidth / (2f * desiredCoverage * request.AspectRatio));
            float depth = Vector3.Dot(bounds.center - camera.transform.position, camera.transform.forward);
            if (depth <= camera.nearClipPlane)
                depth = Mathf.Max(camera.nearClipPlane + plane.DepthSpan + 1f, Mathf.Max(bounds.extents.magnitude * 2f, 1f));

            return new CameraFit
            {
                Camera = camera,
                CameraPath = cameraPath,
                Coverage = coverage,
                WidthCoverage = widthCoverage,
                HeightCoverage = heightCoverage,
                InDesiredRange = coverage >= request.DesiredCoverageMin && coverage <= request.DesiredCoverageMax,
                ScreenRect = screenRect,
                HasScreenRect = hasScreenRect,
                SuggestedOrthographicSize = orthographicSize,
                SuggestedPosition = bounds.center - camera.transform.forward * depth
            };
        }

        static Type ResolveRequiredComponentType(string componentTypeName)
        {
            if (!UnityComponentResolver.TryResolve(componentTypeName, out Type type, out string error))
                throw new InvalidOperationException(error);
            return type;
        }

        static Scene ResolveLoadedScene(string scenePath)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                if (!activeScene.IsValid() || !activeScene.isLoaded)
                    throw new InvalidOperationException("No active loaded scene is available.");
                return activeScene;
            }

            string normalized = NormalizeScenePath(scenePath);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                if (string.Equals(scene.path, normalized, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(scene.name, scenePath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(scene.name, Path.GetFileNameWithoutExtension(normalized), StringComparison.OrdinalIgnoreCase))
                {
                    return scene;
                }
            }

            throw new InvalidOperationException($"Scene '{scenePath}' is not loaded. Open or load the scene before running grid board layout.");
        }

        static GameObject ResolveRoot(GridBoardLayoutRequest request, Scene scene)
        {
            if (string.IsNullOrWhiteSpace(request.Root))
                return null;

            var findParams = new JObject
            {
                ["search_inactive"] = request.IncludeInactive
            };
            GameObject root = ObjectsHelper.FindObject(new JValue(request.Root), request.RootSearchMethod, findParams);
            if (root == null)
                throw new InvalidOperationException($"Root '{request.Root}' could not be resolved.");
            if (root.scene != scene)
                throw new InvalidOperationException($"Root '{request.Root}' belongs to scene '{root.scene.path}', not '{scene.path}'.");
            return root;
        }

        static Camera ResolveCamera(GridBoardLayoutRequest request, out string cameraPath)
        {
            cameraPath = null;
            if (!string.IsNullOrWhiteSpace(request.CameraTarget))
            {
                var findParams = new JObject
                {
                    ["search_inactive"] = request.IncludeInactive
                };
                GameObject cameraObject = ObjectsHelper.FindObject(new JValue(request.CameraTarget), request.CameraSearchMethod, findParams);
                Camera camera = cameraObject != null ? cameraObject.GetComponentInChildren<Camera>(true) : null;
                if (camera != null)
                {
                    cameraPath = UiDiagnosticsHelper.GetHierarchyPath(camera.transform);
                    return camera;
                }
            }

            Camera resolved = Camera.main ?? UnityApiAdapter.FindObjectsByType<Camera>(FindObjectsInactive.Include).FirstOrDefault(camera => camera != null);
            if (resolved != null)
                cameraPath = UiDiagnosticsHelper.GetHierarchyPath(resolved.transform);
            return resolved;
        }

        static Component GetComponentByIndex(GameObject gameObject, Type type, int index)
        {
            Component[] components = gameObject.GetComponents(type);
            return components != null && components.Length > index ? components[index] : null;
        }

        static bool TryReadGridPosition(Component component, string gridFieldName, out Vector3Int cell, out object rawValue, out string source, out string error)
        {
            cell = default;
            rawValue = null;
            source = null;
            error = null;
            if (component == null)
            {
                error = "Grid field component was null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(gridFieldName))
            {
                error = "gridFieldName is required.";
                return false;
            }

            var serializedObject = new SerializedObject(component);
            SerializedProperty property = serializedObject.FindProperty(gridFieldName);
            if (property != null && TryReadGridPosition(property, out cell, out rawValue))
            {
                source = $"SerializedObject.{gridFieldName}";
                return true;
            }

            if (TryReadMemberPath(component, gridFieldName, out object memberValue) && TryCoerceGridPosition(memberValue, out cell, out rawValue))
            {
                source = $"reflection.{gridFieldName}";
                return true;
            }

            error = $"Grid field '{gridFieldName}' could not be read as Vector2Int, Vector3Int, Vector2, Vector3, [x,y], or object with x/y fields.";
            return false;
        }

        static bool TryReadGridPosition(SerializedProperty property, out Vector3Int cell, out object rawValue)
        {
            cell = default;
            rawValue = null;
            switch (property.propertyType)
            {
                case SerializedPropertyType.Vector2Int:
                    Vector2Int vector2Int = property.vector2IntValue;
                    cell = new Vector3Int(vector2Int.x, vector2Int.y, 0);
                    rawValue = new { x = vector2Int.x, y = vector2Int.y };
                    return true;
                case SerializedPropertyType.Vector3Int:
                    Vector3Int vector3Int = property.vector3IntValue;
                    cell = vector3Int;
                    rawValue = new { x = vector3Int.x, y = vector3Int.y, z = vector3Int.z };
                    return true;
                case SerializedPropertyType.Vector2:
                    Vector2 vector2 = property.vector2Value;
                    cell = new Vector3Int(Mathf.RoundToInt(vector2.x), Mathf.RoundToInt(vector2.y), 0);
                    rawValue = ToVector2Object(vector2);
                    return true;
                case SerializedPropertyType.Vector3:
                    Vector3 vector3 = property.vector3Value;
                    cell = new Vector3Int(Mathf.RoundToInt(vector3.x), Mathf.RoundToInt(vector3.y), Mathf.RoundToInt(vector3.z));
                    rawValue = ToVector3Object(vector3);
                    return true;
                case SerializedPropertyType.String:
                    if (TryParseVectorString(property.stringValue, out cell))
                    {
                        rawValue = property.stringValue;
                        return true;
                    }
                    return false;
                case SerializedPropertyType.Generic:
                    if (TryReadRelativeInt(property, out int x, "x", "X", "m_X") && TryReadRelativeInt(property, out int y, "y", "Y", "m_Y"))
                    {
                        int z = TryReadRelativeInt(property, out int zValue, "z", "Z", "m_Z") ? zValue : 0;
                        cell = new Vector3Int(x, y, z);
                        rawValue = new { x, y, z };
                        return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        static bool TryReadRelativeInt(SerializedProperty property, out int value, params string[] names)
        {
            foreach (string name in names)
            {
                SerializedProperty child = property.FindPropertyRelative(name);
                if (child == null)
                    continue;

                if (child.propertyType == SerializedPropertyType.Integer)
                {
                    value = child.intValue;
                    return true;
                }

                if (child.propertyType == SerializedPropertyType.Float)
                {
                    value = Mathf.RoundToInt(child.floatValue);
                    return true;
                }
            }

            value = 0;
            return false;
        }

        static bool TryReadMemberPath(object target, string memberPath, out object value)
        {
            value = target;
            foreach (string part in memberPath.Split('.').Where(part => !string.IsNullOrWhiteSpace(part)))
            {
                if (value == null)
                    return false;

                Type type = value.GetType();
                FieldInfo field = type.GetField(part, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (field != null)
                {
                    value = field.GetValue(value);
                    continue;
                }

                PropertyInfo property = type.GetProperty(part, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    value = property.GetValue(value);
                    continue;
                }

                return false;
            }

            return true;
        }

        static bool TryCoerceGridPosition(object value, out Vector3Int cell, out object rawValue)
        {
            rawValue = value;
            switch (value)
            {
                case Vector2Int vector2Int:
                    cell = new Vector3Int(vector2Int.x, vector2Int.y, 0);
                    rawValue = new { x = vector2Int.x, y = vector2Int.y };
                    return true;
                case Vector3Int vector3Int:
                    cell = vector3Int;
                    rawValue = new { x = vector3Int.x, y = vector3Int.y, z = vector3Int.z };
                    return true;
                case Vector2 vector2:
                    cell = new Vector3Int(Mathf.RoundToInt(vector2.x), Mathf.RoundToInt(vector2.y), 0);
                    rawValue = ToVector2Object(vector2);
                    return true;
                case Vector3 vector3:
                    cell = new Vector3Int(Mathf.RoundToInt(vector3.x), Mathf.RoundToInt(vector3.y), Mathf.RoundToInt(vector3.z));
                    rawValue = ToVector3Object(vector3);
                    return true;
                case string text:
                    if (TryParseVectorString(text, out cell))
                    {
                        rawValue = text;
                        return true;
                    }
                    break;
            }

            if (value is System.Collections.IList list && list.Count >= 2)
            {
                cell = new Vector3Int(ToInt(list[0]), ToInt(list[1]), list.Count >= 3 ? ToInt(list[2]) : 0);
                rawValue = new { x = cell.x, y = cell.y, z = cell.z };
                return true;
            }

            if (value != null && TryReadMemberPath(value, "x", out object xObj) && TryReadMemberPath(value, "y", out object yObj))
            {
                int z = TryReadMemberPath(value, "z", out object zObj) ? ToInt(zObj) : 0;
                cell = new Vector3Int(ToInt(xObj), ToInt(yObj), z);
                rawValue = new { x = cell.x, y = cell.y, z = cell.z };
                return true;
            }

            cell = default;
            return false;
        }

        static (string classification, string source) ClassifyTile(GridBoardLayoutRequest request, Component component)
        {
            if (!string.IsNullOrWhiteSpace(request.ObstacleFieldName) &&
                TryReadMemberOrSerializedValue(component, request.ObstacleFieldName, out object obstacleValue))
            {
                bool obstacle = ToBool(obstacleValue);
                return (obstacle ? "obstacle" : "floor", request.ObstacleFieldName);
            }

            if (!string.IsNullOrWhiteSpace(request.ClassificationFieldName) &&
                TryReadMemberOrSerializedValue(component, request.ClassificationFieldName, out object classificationValue))
            {
                return (NormalizeClassification(classificationValue, request), request.ClassificationFieldName);
            }

            foreach (string obstacleName in new[] { "isObstacle", "obstacle", "isWall", "isBlocked", "blocked", "solid" })
            {
                if (TryReadMemberOrSerializedValue(component, obstacleName, out object value))
                    return (ToBool(value) ? "obstacle" : "floor", obstacleName);
            }

            if (TryReadMemberOrSerializedValue(component, "walkable", out object walkableValue))
                return (ToBool(walkableValue) ? "floor" : "obstacle", "walkable");

            foreach (string typeName in new[] { "tileType", "type", "kind", "category" })
            {
                if (TryReadMemberOrSerializedValue(component, typeName, out object value))
                    return (NormalizeClassification(value, request), typeName);
            }

            return ("floor", "default_all_floor");
        }

        static bool TryReadMemberOrSerializedValue(Component component, string name, out object value)
        {
            value = null;
            SerializedProperty property = new SerializedObject(component).FindProperty(name);
            if (property != null)
            {
                value = DescribeSerializedValue(property);
                return true;
            }

            return TryReadMemberPath(component, name, out value);
        }

        static object DescribeSerializedValue(SerializedProperty property)
        {
            return property.propertyType switch
            {
                SerializedPropertyType.Boolean => property.boolValue,
                SerializedPropertyType.Integer => property.intValue,
                SerializedPropertyType.Float => property.floatValue,
                SerializedPropertyType.String => property.stringValue,
                SerializedPropertyType.Enum => property.enumDisplayNames != null && property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length
                    ? property.enumDisplayNames[property.enumValueIndex]
                    : property.enumValueIndex.ToString(CultureInfo.InvariantCulture),
                _ => property.displayName
            };
        }

        static string NormalizeClassification(object value, GridBoardLayoutRequest request)
        {
            string text = value?.ToString()?.Trim() ?? string.Empty;
            if (string.Equals(text, request.ObstacleValue, StringComparison.OrdinalIgnoreCase) ||
                text.IndexOf("obstacle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("blocked", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("solid", StringComparison.OrdinalIgnoreCase) >= 0)
                return "obstacle";

            if (string.Equals(text, request.FloorValue, StringComparison.OrdinalIgnoreCase) ||
                text.IndexOf("floor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("ground", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("walkable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0)
                return "floor";

            return "unknown";
        }

        static Vector3 ProjectCell(GridBoardLayoutRequest request, Vector3Int cell)
        {
            float x = cell.x;
            float y = cell.y;
            float z = cell.z;
            Vector3 size = request.TileSize;
            return request.ProjectionType switch
            {
                "isometric" or "iso" => request.Origin + new Vector3((x - y) * size.x * 0.5f, (x + y) * size.y * 0.5f, z * size.z),
                "isometric_z_as_y" or "isometriczasy" or "iso_z_as_y" => request.Origin + new Vector3((x - y) * size.x * 0.5f, (x + y) * size.y * 0.5f + z * size.z, 0f),
                "staggered_isometric" or "isometric_staggered" => request.Origin + new Vector3((x - y) * size.x * 0.5f, (x + y) * size.y * 0.25f, z * size.z),
                _ => request.Origin + new Vector3(x * size.x, y * size.y, z * size.z)
            };
        }

        static void BuildBounds(LayoutAnalysis analysis)
        {
            if (analysis.ValidRows.Length == 0)
            {
                analysis.BoardBounds = new Bounds(Vector3.zero, Vector3.zero);
                analysis.VisualBounds = analysis.BoardBounds;
                return;
            }

            Bounds boardBounds = new(analysis.ValidRows[0].TargetPosition, Vector3.zero);
            bool hasVisualBounds = false;
            Bounds visualBounds = default;
            foreach (TileLayoutRow row in analysis.ValidRows)
            {
                boardBounds.Encapsulate(row.TargetPosition);
                if (!hasVisualBounds)
                {
                    visualBounds = row.EstimatedVisualBounds;
                    hasVisualBounds = true;
                }
                else
                {
                    visualBounds.Encapsulate(row.EstimatedVisualBounds);
                }
            }

            analysis.BoardBounds = boardBounds;
            analysis.VisualBounds = visualBounds;
            analysis.HasVisualBounds = hasVisualBounds;
        }

        static Bounds EstimateBoundsAt(Bounds? currentBounds, Vector3 currentPosition, Vector3 targetPosition)
        {
            if (!currentBounds.HasValue)
                return new Bounds(targetPosition, Vector3.one * 0.01f);

            Bounds value = currentBounds.Value;
            Vector3 offset = value.center - currentPosition;
            return new Bounds(targetPosition + offset, value.size);
        }

        static bool TryGetRendererBounds(GameObject gameObject, out Bounds bounds)
        {
            Renderer[] renderers = gameObject.GetComponentsInChildren<Renderer>(true);
            bool found = false;
            bounds = default;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return found;
        }

        static void BuildSpriteDiagnostics(GridBoardLayoutRequest request, LayoutAnalysis analysis)
        {
            var missing = new List<object>();
            foreach (TileLayoutRow row in analysis.ValidRows)
            {
                foreach (SpriteRenderer renderer in row.SpriteRenderers)
                {
                    if (renderer == null || renderer.sprite != null)
                        continue;

                    analysis.MissingSpriteCount++;
                    if (missing.Count < request.MaxRows)
                    {
                        missing.Add(new
                        {
                            path = UiDiagnosticsHelper.GetHierarchyPath(renderer.transform),
                            cell = ToVector3IntObject(row.Cell),
                            enabled = renderer.enabled,
                            activeInHierarchy = renderer.gameObject.activeInHierarchy
                        });
                    }
                }
            }

            analysis.MissingSprites = missing.ToArray();
        }

        static void BuildSortingSamples(GridBoardLayoutRequest request, LayoutAnalysis analysis)
        {
            var samples = new List<object>();
            foreach (TileLayoutRow row in analysis.ValidRows)
            {
                foreach (SpriteRenderer renderer in row.SpriteRenderers)
                {
                    if (renderer == null)
                        continue;

                    int targetOrder = ComputeSortingOrder(request, row);
                    if (samples.Count < request.MaxRows)
                    {
                        samples.Add(new
                        {
                            path = UiDiagnosticsHelper.GetHierarchyPath(renderer.transform),
                            cell = ToVector3IntObject(row.Cell),
                            classification = row.Classification,
                            currentSortingLayerName = renderer.sortingLayerName,
                            currentSortingOrder = renderer.sortingOrder,
                            targetSortingLayerName = string.IsNullOrWhiteSpace(request.SortingLayerName) ? renderer.sortingLayerName : request.SortingLayerName,
                            targetSortingOrder = targetOrder,
                            spriteName = renderer.sprite != null ? renderer.sprite.name : null
                        });
                    }
                }
            }

            analysis.SortingSamples = samples.ToArray();
        }

        static int ComputeSortingOrder(GridBoardLayoutRequest request, TileLayoutRow row)
        {
            int baseOrder = row.Classification == "obstacle" && request.ObstacleSortingBase.HasValue
                ? request.ObstacleSortingBase.Value
                : row.Classification == "floor" && request.FloorSortingBase.HasValue
                    ? request.FloorSortingBase.Value
                    : request.SortingBase;

            return baseOrder +
                row.Cell.y * request.SortingRowStride +
                row.Cell.x * request.SortingColumnStride +
                row.Cell.z * request.SortingZStride;
        }

        static void BuildDuplicateCells(LayoutAnalysis analysis)
        {
            var duplicates = analysis.ValidRows
                .GroupBy(row => row.Cell)
                .Where(group => group.Count() > 1)
                .Select(group => new
                {
                    cell = ToVector3IntObject(group.Key),
                    count = group.Count(),
                    paths = group.Take(10).Select(row => UiDiagnosticsHelper.GetHierarchyPath(row.GameObject.transform)).ToArray()
                })
                .ToArray();

            analysis.DuplicateCellCount = duplicates.Length;
            analysis.DuplicateCells = duplicates.Cast<object>().ToArray();
        }

        static void BuildOverlapSamples(GridBoardLayoutRequest request, LayoutAnalysis analysis)
        {
            var samples = new List<object>();
            int pairCount = 0;
            TileLayoutRow[] rows = analysis.ValidRows.Where(row => row.SpriteRenderers.Length > 0).ToArray();
            for (int i = 0; i < rows.Length; i++)
            {
                Rect a = ToRectXY(rows[i].EstimatedVisualBounds);
                for (int j = i + 1; j < rows.Length; j++)
                {
                    Rect b = ToRectXY(rows[j].EstimatedVisualBounds);
                    if (!a.Overlaps(b))
                        continue;

                    Rect intersection = Intersect(a, b);
                    if (intersection.width <= 0.0001f || intersection.height <= 0.0001f)
                        continue;

                    pairCount++;
                    if (samples.Count < request.MaxOverlapSamples)
                    {
                        samples.Add(new
                        {
                            a = new { path = UiDiagnosticsHelper.GetHierarchyPath(rows[i].GameObject.transform), cell = ToVector3IntObject(rows[i].Cell) },
                            b = new { path = UiDiagnosticsHelper.GetHierarchyPath(rows[j].GameObject.transform), cell = ToVector3IntObject(rows[j].Cell) },
                            intersection = ToRectObject(intersection)
                        });
                    }
                }
            }

            analysis.OverlapPairCount = pairCount;
            analysis.OverlapSamples = samples.ToArray();
        }

        static bool IsOutOfBoard(Vector3Int cell, int width, int height)
        {
            return width > 0 && height > 0 &&
                (cell.x < 0 || cell.y < 0 || cell.x >= width || cell.y >= height);
        }

        static int InferBoardExtent(TileLayoutRow[] rows, int axis)
        {
            if (rows.Length == 0)
                return 0;

            int max = axis == 0 ? rows.Max(row => row.Cell.x) : rows.Max(row => row.Cell.y);
            return Math.Max(0, max + 1);
        }

        static object BuildTileRow(GridBoardLayoutRequest request, TileLayoutRow row)
        {
            return new
            {
                path = UiDiagnosticsHelper.GetHierarchyPath(row.GameObject.transform),
                name = row.GameObject.name,
                objectId = UnityApiAdapter.GetObjectIdOrZero(row.GameObject),
                activeSelf = row.GameObject.activeSelf,
                activeInHierarchy = row.GameObject.activeInHierarchy,
                cell = string.IsNullOrWhiteSpace(row.Error) ? ToVector3IntObject(row.Cell) : null,
                rawGridValue = row.RawGridValue,
                gridSource = row.GridSource,
                classification = row.Classification,
                classificationSource = row.ClassificationSource,
                currentPosition = ToVector3Object(row.CurrentPosition),
                targetPosition = string.IsNullOrWhiteSpace(row.Error) ? ToVector3Object(row.TargetPosition) : null,
                delta = string.IsNullOrWhiteSpace(row.Error) ? ToVector3Object(row.TargetPosition - row.CurrentPosition) : null,
                sortingOrder = string.IsNullOrWhiteSpace(row.Error) ? ComputeSortingOrder(request, row) : (int?)null,
                spriteRendererCount = row.SpriteRenderers.Length,
                error = row.Error
            };
        }

        static object ToObjectRow(GameObject gameObject)
        {
            return new
            {
                path = UiDiagnosticsHelper.GetHierarchyPath(gameObject.transform),
                name = gameObject.name,
                objectId = UnityApiAdapter.GetObjectIdOrZero(gameObject),
                sceneName = gameObject.scene.name,
                scenePath = gameObject.scene.path,
                activeSelf = gameObject.activeSelf,
                activeInHierarchy = gameObject.activeInHierarchy
            };
        }

        sealed class CameraPlaneProjection
        {
            public float XMin;
            public float XMax;
            public float YMin;
            public float YMax;
            public float Width;
            public float Height;
            public float DepthSpan;
        }

        static CameraPlaneProjection ProjectToCameraPlane(Camera camera, Bounds bounds)
        {
            Vector3[] corners = GetBoundsCorners(bounds);
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float minZ = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            float maxZ = float.MinValue;

            foreach (Vector3 corner in corners)
            {
                Vector3 local = camera.transform.InverseTransformPoint(corner);
                minX = Mathf.Min(minX, local.x);
                maxX = Mathf.Max(maxX, local.x);
                minY = Mathf.Min(minY, local.y);
                maxY = Mathf.Max(maxY, local.y);
                minZ = Mathf.Min(minZ, local.z);
                maxZ = Mathf.Max(maxZ, local.z);
            }

            return new CameraPlaneProjection
            {
                XMin = minX,
                XMax = maxX,
                YMin = minY,
                YMax = maxY,
                Width = Mathf.Max(0f, maxX - minX),
                Height = Mathf.Max(0f, maxY - minY),
                DepthSpan = Mathf.Max(0f, maxZ - minZ)
            };
        }

        static bool TryProjectOrthographic(Camera camera, CameraPlaneProjection plane, float aspect, int viewportWidth, int viewportHeight, out Rect rect)
        {
            float halfHeight = Mathf.Max(0.001f, camera.orthographicSize);
            float halfWidth = halfHeight * aspect;
            float xMin = ((plane.XMin / halfWidth) * 0.5f + 0.5f) * viewportWidth;
            float xMax = ((plane.XMax / halfWidth) * 0.5f + 0.5f) * viewportWidth;
            float yMin = ((plane.YMin / halfHeight) * 0.5f + 0.5f) * viewportHeight;
            float yMax = ((plane.YMax / halfHeight) * 0.5f + 0.5f) * viewportHeight;
            rect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            return true;
        }

        static bool TryProjectPerspective(Camera camera, Bounds bounds, float aspect, int viewportWidth, int viewportHeight, out Rect rect)
        {
            float verticalTan = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
            bool valid = false;
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;

            foreach (Vector3 corner in GetBoundsCorners(bounds))
            {
                Vector3 local = camera.transform.InverseTransformPoint(corner);
                if (local.z <= Mathf.Max(0.001f, camera.nearClipPlane * 0.1f))
                    continue;

                float xNdc = local.x / (local.z * verticalTan * aspect);
                float yNdc = local.y / (local.z * verticalTan);
                float x = (xNdc * 0.5f + 0.5f) * viewportWidth;
                float y = (yNdc * 0.5f + 0.5f) * viewportHeight;
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
                valid = true;
            }

            rect = valid ? Rect.MinMaxRect(minX, minY, maxX, maxY) : default;
            return valid;
        }

        static Vector3[] GetBoundsCorners(Bounds bounds)
        {
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            return new[]
            {
                center + new Vector3(-extents.x, -extents.y, -extents.z),
                center + new Vector3(-extents.x, -extents.y, extents.z),
                center + new Vector3(-extents.x, extents.y, -extents.z),
                center + new Vector3(-extents.x, extents.y, extents.z),
                center + new Vector3(extents.x, -extents.y, -extents.z),
                center + new Vector3(extents.x, -extents.y, extents.z),
                center + new Vector3(extents.x, extents.y, -extents.z),
                center + new Vector3(extents.x, extents.y, extents.z)
            };
        }

        static (int width, int height) ReadBoardSize(JObject parameters)
        {
            int width = GetInt(parameters, 0, "boardWidth", "BoardWidth", "width", "Width");
            int height = GetInt(parameters, 0, "boardHeight", "BoardHeight", "height", "Height");
            JToken token = GetToken(parameters, "boardSize", "BoardSize");
            if (token is JArray array && array.Count >= 2)
            {
                width = array[0].Value<int>();
                height = array[1].Value<int>();
            }
            else if (token is JObject obj)
            {
                width = GetInt(obj, width, "width", "Width", "x", "X");
                height = GetInt(obj, height, "height", "Height", "y", "Y");
            }

            return (Math.Max(0, width), Math.Max(0, height));
        }

        static Vector3 ReadVector(JObject parameters, string camelName, string pascalName, Vector3 fallback)
        {
            JToken token = GetToken(parameters, camelName, pascalName);
            if (token is JArray array)
            {
                float x = array.Count > 0 ? array[0].Value<float>() : fallback.x;
                float y = array.Count > 1 ? array[1].Value<float>() : fallback.y;
                float z = array.Count > 2 ? array[2].Value<float>() : fallback.z;
                return new Vector3(x, y, z);
            }

            if (token is JObject obj)
            {
                return new Vector3(
                    GetFloat(obj, fallback.x, "x", "X"),
                    GetFloat(obj, fallback.y, "y", "Y"),
                    GetFloat(obj, fallback.z, "z", "Z"));
            }

            if (token != null && token.Type != JTokenType.Null && float.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float scalar))
                return new Vector3(scalar, scalar, fallback.z);

            return fallback;
        }

        static bool TryParseVectorString(string value, out Vector3Int cell)
        {
            cell = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string[] parts = value.Split(new[] { ',', ';', ' ', 'x' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return false;

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
                return false;

            int z = parts.Length >= 3 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedZ) ? parsedZ : 0;
            cell = new Vector3Int(x, y, z);
            return true;
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

        static string GetString(JObject parameters, params string[] names) => GetToken(parameters, names)?.Value<string>();

        static int GetInt(JObject parameters, int fallback, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<int>();
        }

        static int? GetNullableInt(JObject parameters, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            return token == null || token.Type == JTokenType.Null ? null : token.Value<int>();
        }

        static float GetFloat(JObject parameters, float fallback, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<float>();
        }

        static bool GetBool(JObject parameters, bool fallback, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<bool>();
        }

        static string NormalizeScenePath(string scenePath)
        {
            string normalized = scenePath.Replace('\\', '/');
            if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) && !string.Equals(normalized, "Assets", StringComparison.OrdinalIgnoreCase))
                normalized = "Assets/" + normalized.TrimStart('/');
            return normalized.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ? normalized : $"{normalized}.unity";
        }

        static int ToInt(object value) => Convert.ToInt32(value, CultureInfo.InvariantCulture);

        static bool ToBool(object value)
        {
            if (value is bool boolean)
                return boolean;
            string text = value?.ToString()?.Trim() ?? string.Empty;
            return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "obstacle", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "blocked", StringComparison.OrdinalIgnoreCase);
        }

        static float Clamp01(float value) => Mathf.Clamp(value, 0f, 1f);

        static Rect ToRectXY(Bounds bounds) => Rect.MinMaxRect(bounds.min.x, bounds.min.y, bounds.max.x, bounds.max.y);

        static Rect Intersect(Rect a, Rect b)
        {
            float xMin = Mathf.Max(a.xMin, b.xMin);
            float xMax = Mathf.Min(a.xMax, b.xMax);
            float yMin = Mathf.Max(a.yMin, b.yMin);
            float yMax = Mathf.Min(a.yMax, b.yMax);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        static object ToVector2Object(Vector2 value) => new { x = value.x, y = value.y };

        static object ToVector3Object(Vector3 value) => new { x = value.x, y = value.y, z = value.z };

        static object ToVector3IntObject(Vector3Int value) => new { x = value.x, y = value.y, z = value.z };

        static object ToBoundsObject(Bounds bounds)
        {
            return new
            {
                center = ToVector3Object(bounds.center),
                size = ToVector3Object(bounds.size),
                extents = ToVector3Object(bounds.extents),
                min = ToVector3Object(bounds.min),
                max = ToVector3Object(bounds.max)
            };
        }

        static object ToRectObject(Rect rect)
        {
            return new
            {
                x = rect.x,
                y = rect.y,
                width = rect.width,
                height = rect.height,
                xMin = rect.xMin,
                xMax = rect.xMax,
                yMin = rect.yMin,
                yMax = rect.yMax
            };
        }
    }
}
