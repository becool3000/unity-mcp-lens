using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Pbr.Services.Utilities;
using Unity.AI.Toolkit.Accounts.Services;
using Unity.AI.Toolkit.Asset;
using Unity.AI.Toolkit.GenerationContextMenu;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Pbr.Windows
{
    static class TerrainLayerGeneratorInspectorButton
    {
        [GenerateContextMenu(nameof(GenerateTerrainLayerValidation), nameof(GenerateTerrainLayerHasGenerations))]
        public static void GenerateTerrainLayer() => OnAssetGenerationRequest(new[] { Selection.activeObject });

        public static bool GenerateTerrainLayerValidation() => OnAssetGenerationValidation(new[] { Selection.activeObject }) && Selection.objects.Length == 1;

        public static bool GenerateTerrainLayerHasGenerations() => IncludesGenerationHistory(new[] { Selection.activeObject });

        [MenuItem("Assets/Create/Terrain/Generate Terrain Layer", false, -1000)]
        public static void EmptyTerrainLayerMenu() => CreateAndNameTerrainLayer();

        [MenuItem("Assets/Create/Terrain/Generate Terrain Layer", true, -1000)]
        public static bool ValidateEmptyTerrainLayerMenu() => Account.settings.AiGeneratorsEnabled;

        public static UnityEngine.TerrainLayer EmptyTerrainLayer()
        {
            var terrainLayer = AssetUtils.CreateAndSelectBlankTerrainLayer();
            OnAssetGenerationRequest(new[] { terrainLayer });
            return terrainLayer;
        }

        public static void CreateAndNameTerrainLayer()
        {
            var icon = EditorGUIUtility.ObjectContent(null, typeof(UnityEngine.TerrainLayer))?.image as Texture2D;
            var doCreate = ScriptableObject.CreateInstance<DoCreateBlankAsset>();
            doCreate.action = (_, path, _) =>
            {
                path = AssetUtils.CreateBlankTerrainLayer(path);
                if (string.IsNullOrEmpty(path))
                    Debug.Log($"Failed to create terrain layer for '{path}'.");
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var terrainLayer = AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>(path);
                Selection.activeObject = terrainLayer;
                GenerateTerrainLayer();
            };
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(
                 0,
                 doCreate,
                 $"{AssetUtils.defaultTerrainLayerName}{AssetUtils.terrainLayerExtension}",
                 icon,
                 null,
                 true);
        }

        [InitializeOnLoadMethod]
        static void EditorHeaderButtons() => Editor.finishedDefaultHeaderGUI += OnHeaderControlsGUI;

        static void OnHeaderControlsGUI(Editor editor)
        {
            if (!OnAssetGenerationValidation(editor.targets))
                return;

            var isDisabledAndHasNoHistory = !Account.settings.AiGeneratorsEnabled && !IncludesGenerationHistory(editor.targets);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUI.BeginDisabledGroup(!OnAssetGenerationMultipleValidation(editor.targets) || isDisabledAndHasNoHistory);
            var generateButtonTooltip = "Use generative AI to transform this terrain layer.";
            if (!Account.settings.AiGeneratorsEnabled)
            {
                generateButtonTooltip = Generators.UI.AIDropdownIntegrations.GenerativeMenuRoot.generatorsIsDisabledTooltip;
                if (!isDisabledAndHasNoHistory)
                    generateButtonTooltip += " " + Generators.UI.AIDropdownIntegrations.GenerativeMenuRoot.generatorsHaveHistoryTooltip;
            }
            if (GUILayout.Button(new GUIContent("Generate", generateButtonTooltip)))
                OnAssetGenerationRequest(editor.targets);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        static void OnAssetGenerationRequest(IEnumerable<Object> objects)
        {
            foreach (var obj in objects)
            {
                if (TryGetValidPath(obj, out var validPath))
                {
                    OpenGenerationWindow(obj, validPath);
                }
            }
        }

        static bool OnAssetGenerationValidation(IEnumerable<Object> objects)
        {
            foreach (var obj in objects)
            {
                if (AssetDatabase.IsOpenForEdit(obj) && TryGetValidPath(obj, out _))
                {
                    return true;
                }
            }

            return false;
        }

        static bool TryGetValidPath(Object obj, out string path)
        {
            path = obj switch
            {
                UnityEngine.TerrainLayer terrainLayer => AssetDatabase.GetAssetPath(terrainLayer),
                Terrain terrain => AssetDatabase.GetAssetPath(terrain),
                _ => null
            };

            if (string.IsNullOrEmpty(path))
                path = AssetDatabase.GetAssetPath(obj);

            if (obj is GameObject gameObject && gameObject && gameObject.TryGetComponent<Terrain>(out _))
                return true;

            return obj is UnityEngine.TerrainLayer && !string.IsNullOrEmpty(path) || obj is Terrain;
        }

        static bool OnAssetGenerationMultipleValidation(IReadOnlyCollection<Object> objects) =>
            objects.Any(o => TryGetValidPath(o, out _));

        static bool IncludesGenerationHistory(IReadOnlyCollection<Object> objects) =>
            objects.FirstOrDefault(o => new AssetReference { guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(o)) }.HasGenerations());

        public static void OpenGenerationWindow(string assetPath) => OpenGenerationWindow(null, assetPath);

        public static void OpenGenerationWindow(Object obj, string assetPath)
        {
            if (obj is GameObject gameObject && gameObject && gameObject.TryGetComponent<Terrain>(out var terrain))
            {
                var terrainData = terrain.terrainData;
                if (terrainData == null)
                    terrainData = AssetUtils.CreateTerrainDataForTerrain(terrain);

                if (terrainData.terrainLayers.Length > 0)
                {
                    var terrainLayers = terrainData.terrainLayers;
                    for (var index = 0; index < terrainLayers.Length; index++)
                    {
                        var terrainLayer = terrainLayers[index];
                        var layerPath = AssetDatabase.GetAssetPath(terrainLayer);
                        if (string.IsNullOrEmpty(layerPath))
                        {
                            terrainLayer = terrainLayers[index] = AssetUtils.CreateBlankTerrainLayer();
                            terrainData.terrainLayers = terrainLayers;
                            layerPath = AssetDatabase.GetAssetPath(terrainLayer);
                        }
                        MaterialGeneratorWindow.Display(layerPath);
                    }
                }
                else
                {
                    var newTerrainLayer = AssetUtils.CreateBlankTerrainLayer();
                    terrainData.terrainLayers = new[] { newTerrainLayer };

                    var newLayerPath = AssetDatabase.GetAssetPath(newTerrainLayer);
                    MaterialGeneratorWindow.Display(newLayerPath);
                }

                return;
            }

            MaterialGeneratorWindow.Display(assetPath);
        }
    }
}
