using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry;
using Unity.AI.MCP.Editor.Tools.Parameters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Tilemaps;
using UnityEditor.U2D;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace Unity.AI.MCP.Editor.Tools
{
    public static class TileTools
    {
        const string BuildSetDescription = @"Build headless tile assets or Unity 6 .tileset assets from sprites.

Returns a compact summary by default.";

        const string SetupDescription = @"Create or update a standard Grid + Tilemap stack in a scene.

Creates Ground, Walls, and Overhead layers and optional wall collision.";

        const string PaintDescription = @"Apply batched paint or clear commands to one tilemap layer.

Supports set_many, fill_rect, clear_many, and clear_rect.";

        enum BuildOutputMode
        {
            TileAssets,
            TileSet,
            Both
        }

        enum PaintCommandType
        {
            SetMany,
            FillRect,
            ClearMany,
            ClearRect
        }

        [McpTool("Unity.Tile.BuildSet", BuildSetDescription, Groups = new[] { "assets", "editor" }, EnabledByDefault = true)]
        public static object BuildSet(TileBuildSetParams parameters)
        {
            parameters ??= new TileBuildSetParams();
            if (!TryParseBuildOutputMode(parameters.OutputMode, out BuildOutputMode outputMode))
                return Response.Error($"INVALID_OUTPUT_MODE: Unsupported OutputMode '{parameters.OutputMode}'.");

            string sourceTexturePath = SanitizeAssetPath(parameters.SourceTexturePath);
            bool hasSpritePaths = parameters.SourceSpritePaths != null && parameters.SourceSpritePaths.Count > 0;
            bool needsTexture = outputMode != BuildOutputMode.TileAssets || !hasSpritePaths;
            if (needsTexture && string.IsNullOrWhiteSpace(sourceTexturePath))
                return Response.Error("SOURCE_TEXTURE_REQUIRED: SourceTexturePath is required for TileSet, Both, or texture-driven TileAssets.");

            bool hasSliceWidth = parameters.SliceCellWidth.HasValue;
            bool hasSliceHeight = parameters.SliceCellHeight.HasValue;
            if (hasSliceWidth != hasSliceHeight)
                return Response.Error("INVALID_SLICE: SliceCellWidth and SliceCellHeight must be provided together.");

            try
            {
                string defaultStem = GetDefaultStem(sourceTexturePath, parameters.SourceSpritePaths);
                string tileOutputFolder = NormalizeFolderPath(parameters.TileOutputFolder) ?? BuildDefaultTileOutputFolder(sourceTexturePath, defaultStem);
                string tileSetAssetPath = SanitizeTileSetPath(parameters.TileSetAssetPath) ?? BuildDefaultTileSetPath(sourceTexturePath, defaultStem);
                string paletteFolder = NormalizeFolderPath(parameters.PaletteFolder) ?? tileOutputFolder;
                string paletteName = string.IsNullOrWhiteSpace(parameters.PaletteName) ? $"{defaultStem}_Palette" : SanitizeFileName(parameters.PaletteName);

                if (!string.IsNullOrWhiteSpace(sourceTexturePath))
                {
                    var importResult = PrepareSourceTexture(sourceTexturePath, parameters);
                    if (!importResult.success)
                        return importResult.error;
                }

                List<Sprite> sourceSprites = ResolveSourceSprites(parameters, sourceTexturePath);
                if (sourceSprites.Count == 0)
                    return Response.Error("NO_SOURCE_SPRITES: No sprites were resolved for tile creation.");

                Tile.ColliderType colliderType = ParseColliderType(parameters.TileColliderType);
                Vector3 cellSize = new(parameters.CellSizeX, parameters.CellSizeY, parameters.CellSizeZ);
                Vector3 sortAxis = new(parameters.SortAxisX, parameters.SortAxisY, parameters.SortAxisZ);
                GridLayout.CellLayout cellLayout = ParseCellLayout(parameters.CellLayout, out bool pointTopHex);
                GridPalette.CellSizing cellSizing = ParseCellSizing(parameters.CellSizing);
                TransparencySortMode sortMode = ParseSortMode(parameters.SortMode);

                List<Tile> classicTiles = null;
                List<string> classicTilePaths = null;
                List<string> generatedTileNames = null;
                string paletteAssetPath = null;
                bool atlasCreated = false;
                int tileCount = 0;

                if (outputMode == BuildOutputMode.TileAssets || outputMode == BuildOutputMode.Both)
                {
                    EnsureAssetFolder(tileOutputFolder);
                    (classicTiles, classicTilePaths) = CreateOrUpdateClassicTiles(sourceSprites, tileOutputFolder, colliderType);
                    tileCount = classicTiles.Count;

                    if (parameters.CreatePalette)
                    {
                        EnsureAssetFolder(paletteFolder);
                        paletteAssetPath = CreateOrUpdatePalette(
                            classicTiles.Cast<TileBase>().ToList(),
                            paletteFolder,
                            paletteName,
                            cellLayout,
                            pointTopHex,
                            cellSizing,
                            cellSize,
                            sortMode,
                            sortAxis);
                    }
                }

                if (outputMode == BuildOutputMode.TileSet || outputMode == BuildOutputMode.Both)
                {
                    EnsureAssetFolder(Path.GetDirectoryName(tileSetAssetPath));
                    (generatedTileNames, atlasCreated) = CreateOrUpdateTileSet(
                        sourceTexturePath,
                        tileSetAssetPath,
                        cellLayout,
                        pointTopHex,
                        cellSizing,
                        cellSize,
                        sortMode,
                        sortAxis);

                    tileCount = Math.Max(tileCount, generatedTileNames.Count);
                    if (outputMode == BuildOutputMode.TileSet || string.IsNullOrWhiteSpace(paletteAssetPath))
                        paletteAssetPath = tileSetAssetPath;
                }

                var summary = new Dictionary<string, object>
                {
                    ["sourcePath"] = sourceTexturePath ?? parameters.SourceSpritePaths?.FirstOrDefault(),
                    ["outputMode"] = outputMode.ToString(),
                    ["spriteCount"] = sourceSprites.Count,
                    ["tileCount"] = tileCount,
                    ["tileOutputFolder"] = outputMode == BuildOutputMode.TileSet ? null : tileOutputFolder,
                    ["tileSetAssetPath"] = outputMode == BuildOutputMode.TileAssets ? null : tileSetAssetPath,
                    ["paletteAssetPath"] = paletteAssetPath,
                    ["atlasCreated"] = atlasCreated,
                    ["firstTileName"] = sourceSprites[0].name,
                    ["lastTileName"] = sourceSprites[sourceSprites.Count - 1].name
                };

                if (parameters.IncludeCreatedAssetPaths && classicTilePaths != null)
                    summary["createdTileAssetPaths"] = classicTilePaths;
                if (parameters.IncludeCreatedAssetPaths && generatedTileNames != null && generatedTileNames.Count > 0)
                    summary["generatedTileNames"] = generatedTileNames;

                return Response.Success("Tile set build completed.", summary);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TileTools] BuildSet failed: {exception}");
                return Response.Error($"TILE_BUILD_FAILED: {exception.Message}");
            }
        }

        [McpTool("Unity.Tilemap.Setup", SetupDescription, Groups = new[] { "scene", "editor" }, EnabledByDefault = true)]
        public static object SetupTilemap(TilemapSetupParams parameters)
        {
            parameters ??= new TilemapSetupParams();
            try
            {
                Scene scene = GetTargetScene(parameters.ScenePath);
                if (!scene.IsValid())
                    return Response.Error("SCENE_NOT_FOUND: Could not resolve the requested scene.");

                string gridName = string.IsNullOrWhiteSpace(parameters.GridName) ? "LevelGrid" : parameters.GridName.Trim();
                GameObject gridObject = FindRootObjectByName(scene, gridName);
                bool createdGrid = false;
                if (gridObject == null)
                {
                    gridObject = new GameObject(gridName);
                    SceneManager.MoveGameObjectToScene(gridObject, scene);
                    createdGrid = true;
                }

                Grid grid = EnsureComponent<Grid>(gridObject);
                grid.cellLayout = GridLayout.CellLayout.Rectangle;
                grid.cellSize = new Vector3(parameters.CellSizeX, parameters.CellSizeY, parameters.CellSizeZ);

                var createdLayers = new List<string>();
                TilemapLayerSetup groundLayer = EnsureTilemapLayer(gridObject.transform, parameters.GroundLayerName, parameters.SortingLayerName, parameters.GroundOrder, createdLayers);
                TilemapLayerSetup wallsLayer = EnsureTilemapLayer(gridObject.transform, parameters.WallsLayerName, parameters.SortingLayerName, parameters.WallsOrder, createdLayers);
                TilemapLayerSetup overheadLayer = EnsureTilemapLayer(gridObject.transform, parameters.OverheadLayerName, parameters.SortingLayerName, parameters.OverheadOrder, createdLayers);

                ConfigureWallCollision(wallsLayer.gameObject, parameters.AddWallCollider, parameters.UseCompositeCollider);

                EditorSceneManager.MarkSceneDirty(scene);
                bool saved = false;
                if (parameters.SaveScene)
                {
                    if (string.IsNullOrWhiteSpace(scene.path))
                        return Response.Error("SCENE_PATH_REQUIRED: Cannot save an untitled scene. Provide ScenePath or save the scene in Unity first.");
                    saved = EditorSceneManager.SaveScene(scene);
                }

                var summary = new Dictionary<string, object>
                {
                    ["scenePath"] = scene.path,
                    ["gridName"] = gridObject.name,
                    ["createdGrid"] = createdGrid,
                    ["createdLayers"] = createdLayers,
                    ["wallColliderEnabled"] = parameters.AddWallCollider,
                    ["compositeEnabled"] = parameters.AddWallCollider && parameters.UseCompositeCollider,
                    ["saved"] = saved
                };

                if (parameters.IncludeObjectPaths)
                {
                    summary["objectPaths"] = new[]
                    {
                        GetHierarchyPath(gridObject.transform),
                        GetHierarchyPath(groundLayer.gameObject.transform),
                        GetHierarchyPath(wallsLayer.gameObject.transform),
                        GetHierarchyPath(overheadLayer.gameObject.transform)
                    };
                }

                return Response.Success("Tilemap stack ready.", summary);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TileTools] SetupTilemap failed: {exception}");
                return Response.Error($"TILEMAP_SETUP_FAILED: {exception.Message}");
            }
        }

        [McpTool("Unity.Tilemap.Paint", PaintDescription, Groups = new[] { "scene", "editor" }, EnabledByDefault = true)]
        public static object PaintTilemap(TilemapPaintParams parameters)
        {
            parameters ??= new TilemapPaintParams();
            if (string.IsNullOrWhiteSpace(parameters.LayerName))
                return Response.Error("LAYER_NAME_REQUIRED: LayerName is required.");
            if (parameters.Commands == null || parameters.Commands.Count == 0)
                return Response.Error("COMMANDS_REQUIRED: At least one paint command is required.");

            try
            {
                Scene scene = GetTargetScene(parameters.ScenePath);
                if (!scene.IsValid())
                    return Response.Error("SCENE_NOT_FOUND: Could not resolve the requested scene.");

                string gridName = string.IsNullOrWhiteSpace(parameters.GridName) ? "LevelGrid" : parameters.GridName.Trim();
                GameObject gridObject = FindRootObjectByName(scene, gridName);
                if (gridObject == null)
                    return Response.Error($"GRID_NOT_FOUND: Grid '{gridName}' was not found in scene '{scene.path}'.");

                Transform layerTransform = gridObject.transform.Find(parameters.LayerName);
                if (layerTransform == null)
                    return Response.Error($"LAYER_NOT_FOUND: Layer '{parameters.LayerName}' was not found under grid '{gridObject.name}'.");

                Tilemap tilemap = layerTransform.GetComponent<Tilemap>();
                if (tilemap == null)
                    return Response.Error($"TILEMAP_MISSING: Layer '{parameters.LayerName}' does not have a Tilemap component.");

                var changedCells = parameters.IncludeChangedCells ? new List<int[]>() : null;
                int commandsApplied = 0;
                int changedCellCount = 0;

                foreach (TilemapPaintCommandParams command in parameters.Commands)
                {
                    PaintCommandType commandType = ParsePaintCommandType(command.Type);
                    switch (commandType)
                    {
                        case PaintCommandType.SetMany:
                        {
                            TileBase tile = ResolveTileReference(command);
                            List<Vector3Int> cells = ParseCells(command.Cells, commandType);
                            Vector3Int[] positions = cells.ToArray();
                            tilemap.SetTiles(positions, Enumerable.Repeat(tile, positions.Length).ToArray());
                            changedCellCount += positions.Length;
                            AppendChangedCells(changedCells, cells);
                            commandsApplied++;
                            break;
                        }
                        case PaintCommandType.ClearMany:
                        {
                            List<Vector3Int> cells = ParseCells(command.Cells, commandType);
                            Vector3Int[] positions = cells.ToArray();
                            tilemap.SetTiles(positions, new TileBase[positions.Length]);
                            changedCellCount += positions.Length;
                            AppendChangedCells(changedCells, cells);
                            commandsApplied++;
                            break;
                        }
                        case PaintCommandType.FillRect:
                        {
                            TileBase tile = ResolveTileReference(command);
                            BoundsInt bounds = ParseRect(command, commandType);
                            int size = bounds.size.x * bounds.size.y * bounds.size.z;
                            tilemap.SetTilesBlock(bounds, Enumerable.Repeat(tile, size).ToArray());
                            changedCellCount += size;
                            AppendChangedRect(changedCells, bounds);
                            commandsApplied++;
                            break;
                        }
                        case PaintCommandType.ClearRect:
                        {
                            BoundsInt bounds = ParseRect(command, commandType);
                            int size = bounds.size.x * bounds.size.y * bounds.size.z;
                            tilemap.SetTilesBlock(bounds, new TileBase[size]);
                            changedCellCount += size;
                            AppendChangedRect(changedCells, bounds);
                            commandsApplied++;
                            break;
                        }
                    }
                }

                EditorSceneManager.MarkSceneDirty(scene);
                bool saved = false;
                if (parameters.SaveScene)
                {
                    if (string.IsNullOrWhiteSpace(scene.path))
                        return Response.Error("SCENE_PATH_REQUIRED: Cannot save an untitled scene. Provide ScenePath or save the scene in Unity first.");
                    saved = EditorSceneManager.SaveScene(scene);
                }

                var summary = new Dictionary<string, object>
                {
                    ["scenePath"] = scene.path,
                    ["layerName"] = parameters.LayerName,
                    ["commandsApplied"] = commandsApplied,
                    ["changedCellCount"] = changedCellCount,
                    ["saved"] = saved
                };

                if (parameters.IncludeChangedCells)
                    summary["changedCells"] = changedCells;

                return Response.Success("Tilemap paint completed.", summary);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TileTools] PaintTilemap failed: {exception}");
                return Response.Error($"TILEMAP_PAINT_FAILED: {exception.Message}");
            }
        }

        static (bool success, object error) PrepareSourceTexture(string sourceTexturePath, TileBuildSetParams parameters)
        {
            string assetPath = SanitizeAssetPath(sourceTexturePath);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                return (false, Response.Error($"TEXTURE_IMPORTER_NOT_FOUND: Texture importer not found for '{assetPath}'."));

            importer.textureType = TextureImporterType.Sprite;
            bool shouldSlice = parameters.SliceCellWidth.HasValue && parameters.SliceCellHeight.HasValue;
            if (shouldSlice)
                importer.spriteImportMode = SpriteImportMode.Multiple;
            if (parameters.AlphaIsTransparency.HasValue)
                importer.alphaIsTransparency = parameters.AlphaIsTransparency.Value;
            if (parameters.PixelsPerUnit.HasValue && parameters.PixelsPerUnit.Value > 0.0001f)
                importer.spritePixelsPerUnit = parameters.PixelsPerUnit.Value;
            if (TryParseFilterMode(parameters.FilterMode, out FilterMode filterMode))
                importer.filterMode = filterMode;
            if (TryParseCompression(parameters.Compression, out TextureImporterCompression compression))
                importer.textureCompression = compression;

            importer.SaveAndReimport();
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            if (!shouldSlice)
                return (true, null);

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture == null)
                return (false, Response.Error($"TEXTURE_NOT_FOUND: Could not load texture '{assetPath}' after import."));

#pragma warning disable CS0618
            importer.spritesheet = BuildSpritesheet(
                texture.width,
                texture.height,
                parameters.SliceCellWidth.Value,
                parameters.SliceCellHeight.Value,
                parameters.SlicePaddingX,
                parameters.SlicePaddingY,
                parameters.SliceOffsetX,
                parameters.SliceOffsetY,
                texture.name);
#pragma warning restore CS0618
            importer.SaveAndReimport();
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            return (true, null);
        }

        static SpriteMetaData[] BuildSpritesheet(int textureWidth, int textureHeight, int cellWidth, int cellHeight, int paddingX, int paddingY, int offsetX, int offsetY, string baseName)
        {
            var metadata = new List<SpriteMetaData>();
            int index = 0;
            for (int y = textureHeight - offsetY - cellHeight; y >= 0; y -= cellHeight + paddingY)
            {
                for (int x = offsetX; x + cellWidth <= textureWidth; x += cellWidth + paddingX)
                {
                    metadata.Add(new SpriteMetaData
                    {
                        alignment = (int)SpriteAlignment.Center,
                        border = Vector4.zero,
                        name = $"{baseName}_{index:00}",
                        pivot = new Vector2(0.5f, 0.5f),
                        rect = new Rect(x, y, cellWidth, cellHeight)
                    });
                    index++;
                }
            }

            return metadata.ToArray();
        }

        static List<Sprite> ResolveSourceSprites(TileBuildSetParams parameters, string sourceTexturePath)
        {
            if (parameters.SourceSpritePaths != null && parameters.SourceSpritePaths.Count > 0 && string.IsNullOrWhiteSpace(sourceTexturePath))
            {
                return parameters.SourceSpritePaths
                    .Select(SanitizeAssetPath)
                    .Select(path => AssetDatabase.LoadAssetAtPath<Sprite>(path))
                    .Where(sprite => sprite != null)
                    .OrderBy(sprite => sprite.name, StringComparer.Ordinal)
                    .ToList();
            }

            if (string.IsNullOrWhiteSpace(sourceTexturePath))
                return new List<Sprite>();

            return AssetDatabase.LoadAllAssetsAtPath(sourceTexturePath)
                .OfType<Sprite>()
                .OrderByDescending(sprite => sprite.rect.y)
                .ThenBy(sprite => sprite.rect.x)
                .ThenBy(sprite => sprite.name, StringComparer.Ordinal)
                .ToList();
        }

        static (List<Tile> tiles, List<string> assetPaths) CreateOrUpdateClassicTiles(List<Sprite> sourceSprites, string outputFolder, Tile.ColliderType colliderType)
        {
            var createdTiles = new List<Tile>(sourceSprites.Count);
            var createdPaths = new List<string>(sourceSprites.Count);
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (Sprite sprite in sourceSprites)
                {
                    string assetPath = $"{outputFolder}/{SanitizeFileName(sprite.name)}.asset";
                    Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(assetPath);
                    if (tile == null)
                    {
                        tile = ScriptableObject.CreateInstance<Tile>();
                        tile.name = sprite.name;
                        tile.sprite = sprite;
                        tile.colliderType = colliderType;
                        AssetDatabase.CreateAsset(tile, assetPath);
                    }
                    else
                    {
                        tile.name = sprite.name;
                        tile.sprite = sprite;
                        tile.colliderType = colliderType;
                        EditorUtility.SetDirty(tile);
                    }

                    createdTiles.Add(tile);
                    createdPaths.Add(assetPath);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return (createdTiles, createdPaths);
        }

        static string CreateOrUpdatePalette(List<TileBase> tiles, string paletteFolder, string paletteName, GridLayout.CellLayout cellLayout, bool pointTopHex, GridPalette.CellSizing cellSizing, Vector3 cellSize, TransparencySortMode sortMode, Vector3 sortAxis)
        {
            string assetPath = $"{paletteFolder}/{paletteName}.prefab";
            GameObject palettePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (palettePrefab == null)
            {
                palettePrefab = GridPaletteUtility.CreateNewPalette(
                    paletteFolder,
                    paletteName,
                    cellLayout,
                    cellSizing,
                    cellSize,
                    GetCellSwizzle(cellLayout, pointTopHex),
                    sortMode,
                    sortAxis);
                assetPath = AssetDatabase.GetAssetPath(palettePrefab);
            }

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                Grid grid = EnsureComponent<Grid>(prefabRoot);
                grid.cellLayout = cellLayout;
                grid.cellSize = cellSize;
                grid.cellSwizzle = GetCellSwizzle(cellLayout, pointTopHex);

                Tilemap tilemap = prefabRoot.GetComponentInChildren<Tilemap>(true);
                if (tilemap == null)
                {
                    GameObject layer = new("Layer1");
                    layer.transform.SetParent(prefabRoot.transform, false);
                    tilemap = layer.AddComponent<Tilemap>();
                    layer.AddComponent<TilemapRenderer>();
                }

                tilemap.ClearAllTiles();
                int width = Mathf.CeilToInt(Mathf.Sqrt(tiles.Count));
                var positions = new Vector3Int[tiles.Count];
                TileBase[] tileArray = tiles.ToArray();
                for (int index = 0; index < tiles.Count; index++)
                    positions[index] = new Vector3Int(index % width, -(index / width), 0);
                tilemap.SetTiles(positions, tileArray);
                tilemap.CompressBounds();
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, assetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            Object paletteAsset = AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .FirstOrDefault(asset => asset is GridPalette);
            if (paletteAsset != null)
            {
                var serializedPalette = new SerializedObject(paletteAsset);
                serializedPalette.Update();
                SerializedProperty cellSizingProperty = serializedPalette.FindProperty("cellSizing");
                if (cellSizingProperty != null)
                    cellSizingProperty.enumValueIndex = (int)cellSizing;

                SerializedProperty sortModeProperty = serializedPalette.FindProperty("m_TransparencySortMode");
                if (sortModeProperty != null)
                    sortModeProperty.enumValueIndex = (int)sortMode;

                SerializedProperty sortAxisProperty = serializedPalette.FindProperty("m_TransparencySortAxis");
                if (sortAxisProperty != null)
                    sortAxisProperty.vector3Value = sortAxis;

                serializedPalette.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(paletteAsset);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return assetPath;
        }

        static (List<string> generatedTileNames, bool atlasCreated) CreateOrUpdateTileSet(string sourceTexturePath, string tileSetAssetPath, GridLayout.CellLayout cellLayout, bool pointTopHex, GridPalette.CellSizing cellSizing, Vector3 cellSize, TransparencySortMode sortMode, Vector3 sortAxis)
        {
            TileSet tileSet = LoadExistingTileSet(tileSetAssetPath);
            if (tileSet == null)
                tileSet = ScriptableObject.CreateInstance<TileSet>();
            SpriteAtlas spriteAtlas = LoadExistingSpriteAtlas(tileSetAssetPath);
            bool atlasCreated = spriteAtlas == null;
            if (spriteAtlas == null)
                spriteAtlas = CreateDefaultSpriteAtlas();

            var textureSource = new TileSet.TextureSource
            {
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(sourceTexturePath),
                tileTemplate = null
            };

            if (tileSet.textureSources == null)
                SetTileSetTextureSources(tileSet, new List<TileSet.TextureSource>());

            tileSet.textureSources.Clear();
            tileSet.textureSources.Add(textureSource);
            tileSet.cellLayout = cellLayout;
            tileSet.hexagonLayout = pointTopHex ? TileSet.HexagonLayout.PointTop : TileSet.HexagonLayout.FlatTop;
            tileSet.cellSizing = cellSizing;
            tileSet.cellSize = cellSize;
            tileSet.sortMode = sortMode;
            tileSet.sortAxis = sortAxis;
            tileSet.createAtlas = true;

            SaveTileSet(tileSetAssetPath, tileSet, spriteAtlas);
            AssetDatabase.ImportAsset(tileSetAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            List<string> tileNames = AssetDatabase.LoadAllAssetsAtPath(tileSetAssetPath)
                .OfType<TileBase>()
                .Select(tile => tile.name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            return (tileNames, atlasCreated);
        }

        static TileSet LoadExistingTileSet(string tileSetAssetPath)
        {
            if (!File.Exists(GetProjectAbsolutePath(tileSetAssetPath)))
                return null;

            foreach (Object loaded in InternalEditorUtility.LoadSerializedFileAndForget(tileSetAssetPath))
            {
                if (loaded is TileSet tileSet)
                    return tileSet;
            }

            return null;
        }

        static SpriteAtlas LoadExistingSpriteAtlas(string tileSetAssetPath)
        {
            if (!File.Exists(GetProjectAbsolutePath(tileSetAssetPath)))
                return null;

            foreach (Object loaded in InternalEditorUtility.LoadSerializedFileAndForget(tileSetAssetPath))
            {
                if (loaded is SpriteAtlas spriteAtlas)
                    return spriteAtlas;
            }

            return null;
        }

        static SpriteAtlas CreateDefaultSpriteAtlas()
        {
            var spriteAtlas = new SpriteAtlas();
            MarkSpriteAtlasV2IfSupported(spriteAtlas);
            spriteAtlas.SetTextureSettings(new SpriteAtlasTextureSettings
            {
                filterMode = FilterMode.Point,
                anisoLevel = 0,
                generateMipMaps = false,
                sRGB = true
            });
            return spriteAtlas;
        }

        static void MarkSpriteAtlasV2IfSupported(SpriteAtlas spriteAtlas)
        {
            MethodInfo setV2Method = typeof(SpriteAtlas).GetMethod("SetV2", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            setV2Method?.Invoke(spriteAtlas, null);
        }

        static void SaveTileSet(string tileSetAssetPath, TileSet tileSet, SpriteAtlas spriteAtlas)
        {
            bool originalVerifySavingAssets = EditorPrefs.GetBool("VerifySavingAssets", false);
            EditorPrefs.SetBool("VerifySavingAssets", false);
            try
            {
                InternalEditorUtility.SaveToSerializedFileAndForget(
                    new Object[] { tileSet, spriteAtlas },
                    tileSetAssetPath,
                    EditorSettings.serializationMode != SerializationMode.ForceBinary);
            }
            finally
            {
                EditorPrefs.SetBool("VerifySavingAssets", originalVerifySavingAssets);
            }
        }

        static void SetTileSetTextureSources(TileSet tileSet, List<TileSet.TextureSource> textureSources)
        {
            FieldInfo field = typeof(TileSet).GetField("m_TextureSources", BindingFlags.Instance | BindingFlags.NonPublic);
            field?.SetValue(tileSet, textureSources);
        }

        static Scene GetTargetScene(string scenePath)
        {
            string sanitizedScenePath = SanitizeScenePath(scenePath);
            Scene activeScene = SceneManager.GetActiveScene();
            if (string.IsNullOrWhiteSpace(sanitizedScenePath))
                return activeScene;

            if (!File.Exists(GetProjectAbsolutePath(sanitizedScenePath)))
                return default;

            if (activeScene.IsValid() && !string.Equals(activeScene.path, sanitizedScenePath, StringComparison.OrdinalIgnoreCase))
            {
                if (activeScene.isDirty)
                {
                    if (string.IsNullOrWhiteSpace(activeScene.path))
                        throw new InvalidOperationException("Active scene has unsaved changes and no path. Save it in Unity before opening a different scene through MCP.");

                    EditorSceneManager.SaveScene(activeScene);
                }

                activeScene = EditorSceneManager.OpenScene(sanitizedScenePath, OpenSceneMode.Single);
            }
            else if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                activeScene = EditorSceneManager.OpenScene(sanitizedScenePath, OpenSceneMode.Single);
            }

            return activeScene;
        }

        static TilemapLayerSetup EnsureTilemapLayer(Transform parent, string layerName, string sortingLayerName, int sortingOrder, List<string> createdLayers)
        {
            string resolvedName = string.IsNullOrWhiteSpace(layerName) ? "Tilemap" : layerName.Trim();
            Transform child = parent.Find(resolvedName);
            GameObject layerObject;
            if (child == null)
            {
                layerObject = new GameObject(resolvedName);
                layerObject.transform.SetParent(parent, false);
                createdLayers.Add(resolvedName);
            }
            else
            {
                layerObject = child.gameObject;
            }

            Tilemap tilemap = EnsureComponent<Tilemap>(layerObject);
            TilemapRenderer renderer = EnsureComponent<TilemapRenderer>(layerObject);
            renderer.sortingLayerName = string.IsNullOrWhiteSpace(sortingLayerName) ? "Default" : sortingLayerName;
            renderer.sortingOrder = sortingOrder;
            return new TilemapLayerSetup(layerObject, tilemap, renderer);
        }

        static void ConfigureWallCollision(GameObject wallLayer, bool addWallCollider, bool useCompositeCollider)
        {
            TilemapCollider2D tilemapCollider = wallLayer.GetComponent<TilemapCollider2D>();
            CompositeCollider2D compositeCollider = wallLayer.GetComponent<CompositeCollider2D>();
            Rigidbody2D rigidbody2D = wallLayer.GetComponent<Rigidbody2D>();

            if (!addWallCollider)
            {
                if (tilemapCollider != null)
                {
                    tilemapCollider.enabled = false;
                    SetCompositeUsage(tilemapCollider, false);
                }

                if (compositeCollider != null)
                    compositeCollider.enabled = false;
                return;
            }

            tilemapCollider = EnsureComponent<TilemapCollider2D>(wallLayer);
            tilemapCollider.enabled = true;

            if (!useCompositeCollider)
            {
                SetCompositeUsage(tilemapCollider, false);
                if (compositeCollider != null)
                    compositeCollider.enabled = false;
                return;
            }

            rigidbody2D = EnsureComponent<Rigidbody2D>(wallLayer);
            rigidbody2D.bodyType = RigidbodyType2D.Static;
            rigidbody2D.simulated = true;

            compositeCollider = EnsureComponent<CompositeCollider2D>(wallLayer);
            compositeCollider.enabled = true;
            SetCompositeUsage(tilemapCollider, true);
        }

        static T EnsureComponent<T>(GameObject gameObject) where T : Component
        {
            if (!gameObject.TryGetComponent(out T component) || component == null)
                component = gameObject.AddComponent<T>();
            return component;
        }

        static void SetCompositeUsage(Collider2D collider, bool enabled)
        {
            PropertyInfo usedByComposite = collider.GetType().GetProperty("usedByComposite", BindingFlags.Instance | BindingFlags.Public);
            if (usedByComposite != null && usedByComposite.CanWrite)
            {
                usedByComposite.SetValue(collider, enabled);
                return;
            }

            PropertyInfo compositeOperation = collider.GetType().GetProperty("compositeOperation", BindingFlags.Instance | BindingFlags.Public);
            if (compositeOperation != null && compositeOperation.CanWrite)
            {
                object enumValue = Enum.Parse(compositeOperation.PropertyType, enabled ? "Merge" : "None");
                compositeOperation.SetValue(collider, enumValue);
            }
        }

        static TileBase ResolveTileReference(TilemapPaintCommandParams command)
        {
            if (!string.IsNullOrWhiteSpace(command.TileAssetPath))
            {
                TileBase tile = AssetDatabase.LoadAssetAtPath<TileBase>(SanitizeAssetPath(command.TileAssetPath));
                if (tile == null)
                    throw new InvalidOperationException($"Tile asset '{command.TileAssetPath}' could not be loaded.");
                return tile;
            }

            if (string.IsNullOrWhiteSpace(command.TileSetAssetPath) || string.IsNullOrWhiteSpace(command.TileName))
                throw new InvalidOperationException("Set commands require either TileAssetPath or TileSetAssetPath + TileName.");

            string tileSetAssetPath = SanitizeTileSetPath(command.TileSetAssetPath);
            TileBase resolvedTile = AssetDatabase.LoadAllAssetsAtPath(tileSetAssetPath)
                .OfType<TileBase>()
                .FirstOrDefault(tile => string.Equals(tile.name, command.TileName, StringComparison.Ordinal));

            if (resolvedTile == null)
                throw new InvalidOperationException($"Tile '{command.TileName}' was not found in '{tileSetAssetPath}'.");

            return resolvedTile;
        }

        static List<Vector3Int> ParseCells(List<int[]> cells, PaintCommandType commandType)
        {
            if (cells == null || cells.Count == 0)
                throw new InvalidOperationException($"'{commandType}' requires at least one cell coordinate.");

            var result = new List<Vector3Int>(cells.Count);
            foreach (int[] cell in cells)
            {
                if (cell == null || cell.Length < 2)
                    throw new InvalidOperationException("Cell coordinates must be arrays of [x, y].");
                result.Add(new Vector3Int(cell[0], cell[1], 0));
            }

            return result;
        }

        static BoundsInt ParseRect(TilemapPaintCommandParams command, PaintCommandType commandType)
        {
            if (command.Width <= 0 || command.Height <= 0)
                throw new InvalidOperationException($"'{commandType}' requires Width and Height greater than zero.");
            return new BoundsInt(command.X, command.Y, 0, command.Width, command.Height, 1);
        }

        static void AppendChangedCells(List<int[]> changedCells, List<Vector3Int> cells)
        {
            if (changedCells == null)
                return;

            foreach (Vector3Int cell in cells)
                changedCells.Add(new[] { cell.x, cell.y });
        }

        static void AppendChangedRect(List<int[]> changedCells, BoundsInt bounds)
        {
            if (changedCells == null)
                return;

            foreach (Vector3Int position in bounds.allPositionsWithin)
                changedCells.Add(new[] { position.x, position.y });
        }

        static bool TryParseBuildOutputMode(string value, out BuildOutputMode outputMode)
        {
            return Enum.TryParse(value?.Trim(), true, out outputMode);
        }

        static PaintCommandType ParsePaintCommandType(string value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "set_many" => PaintCommandType.SetMany,
                "fill_rect" => PaintCommandType.FillRect,
                "clear_many" => PaintCommandType.ClearMany,
                "clear_rect" => PaintCommandType.ClearRect,
                _ => throw new InvalidOperationException($"Unsupported paint command '{value}'.")
            };
        }

        static Tile.ColliderType ParseColliderType(string value)
        {
            return Enum.TryParse(value?.Trim(), true, out Tile.ColliderType colliderType)
                ? colliderType
                : Tile.ColliderType.None;
        }

        static GridLayout.CellLayout ParseCellLayout(string value, out bool pointTopHex)
        {
            pointTopHex = true;
            switch (value?.Trim())
            {
                case "HexagonalPointTop":
                    return GridLayout.CellLayout.Hexagon;
                case "HexagonalFlatTop":
                    pointTopHex = false;
                    return GridLayout.CellLayout.Hexagon;
                case "Isometric":
                    return GridLayout.CellLayout.Isometric;
                case "IsometricZAsY":
                    return GridLayout.CellLayout.IsometricZAsY;
                default:
                    return GridLayout.CellLayout.Rectangle;
            }
        }

        static GridLayout.CellSwizzle GetCellSwizzle(GridLayout.CellLayout cellLayout, bool pointTopHex)
        {
            return cellLayout == GridLayout.CellLayout.Hexagon && !pointTopHex
                ? GridLayout.CellSwizzle.YXZ
                : GridLayout.CellSwizzle.XYZ;
        }

        static GridPalette.CellSizing ParseCellSizing(string value)
        {
            return Enum.TryParse(value?.Trim(), true, out GridPalette.CellSizing cellSizing)
                ? cellSizing
                : GridPalette.CellSizing.Automatic;
        }

        static TransparencySortMode ParseSortMode(string value)
        {
            return Enum.TryParse(value?.Trim(), true, out TransparencySortMode sortMode)
                ? sortMode
                : TransparencySortMode.Default;
        }

        static bool TryParseFilterMode(string value, out FilterMode mode)
        {
            mode = FilterMode.Point;
            return !string.IsNullOrWhiteSpace(value) && Enum.TryParse(value.Trim(), true, out mode);
        }

        static bool TryParseCompression(string value, out TextureImporterCompression compression)
        {
            compression = TextureImporterCompression.Uncompressed;
            return !string.IsNullOrWhiteSpace(value) && Enum.TryParse(value.Trim(), true, out compression);
        }

        static string GetDefaultStem(string sourceTexturePath, List<string> sourceSpritePaths)
        {
            if (!string.IsNullOrWhiteSpace(sourceTexturePath))
                return Path.GetFileNameWithoutExtension(sourceTexturePath);
            if (sourceSpritePaths != null && sourceSpritePaths.Count > 0)
                return Path.GetFileNameWithoutExtension(sourceSpritePaths[0]);
            return "TileSet";
        }

        static string BuildDefaultTileOutputFolder(string sourceTexturePath, string defaultStem)
        {
            if (string.IsNullOrWhiteSpace(sourceTexturePath))
                return "Assets/Tiles";
            string directory = Path.GetDirectoryName(sourceTexturePath)?.Replace('\\', '/');
            return $"{directory}/{SanitizeFileName(defaultStem)}_Tiles";
        }

        static string BuildDefaultTileSetPath(string sourceTexturePath, string defaultStem)
        {
            if (string.IsNullOrWhiteSpace(sourceTexturePath))
                return $"Assets/{SanitizeFileName(defaultStem)}.tileset";
            string directory = Path.GetDirectoryName(sourceTexturePath)?.Replace('\\', '/');
            return $"{directory}/{SanitizeFileName(defaultStem)}.tileset";
        }

        static void EnsureAssetFolder(string assetFolderPath)
        {
            string sanitized = NormalizeFolderPath(assetFolderPath);
            if (string.IsNullOrWhiteSpace(sanitized))
                return;
            Directory.CreateDirectory(GetProjectAbsolutePath(sanitized));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        static string NormalizeFolderPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            return SanitizeAssetPath(path).TrimEnd('/');
        }

        static string SanitizeScenePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            string sanitized = SanitizeAssetPath(path);
            return sanitized.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ? sanitized : $"{sanitized}.unity";
        }

        static string SanitizeTileSetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            string sanitized = SanitizeAssetPath(path);
            return sanitized.EndsWith(".tileset", StringComparison.OrdinalIgnoreCase) ? sanitized : $"{sanitized}.tileset";
        }

        static string SanitizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            string normalized = path.Replace('\\', '/');
            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || string.Equals(normalized, "Assets", StringComparison.OrdinalIgnoreCase))
                return normalized;
            return "Assets/" + normalized.TrimStart('/');
        }

        static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Tile";

            char[] invalidChars = Path.GetInvalidFileNameChars();
            return new string(name.Select(character =>
                invalidChars.Contains(character) || character == '/' || character == '\\' ? '_' : character).ToArray()).Trim();
        }

        static string GetProjectAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string sanitized = assetPath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(projectRoot, sanitized);
        }

        static GameObject FindRootObjectByName(Scene scene, string name)
        {
            return scene.GetRootGameObjects().FirstOrDefault(gameObject => string.Equals(gameObject.name, name, StringComparison.Ordinal));
        }

        static string GetHierarchyPath(Transform transform)
        {
            var parts = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                parts.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", parts);
        }

        readonly struct TilemapLayerSetup
        {
            public TilemapLayerSetup(GameObject gameObject, Tilemap tilemap, TilemapRenderer renderer)
            {
                this.gameObject = gameObject;
                this.tilemap = tilemap;
                this.renderer = renderer;
            }

            public readonly GameObject gameObject;
            public readonly Tilemap tilemap;
            public readonly TilemapRenderer renderer;
        }
    }
}
