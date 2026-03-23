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
    static class MaterialGeneratorInspectorButton
    {
        [GenerateContextMenu(nameof(GenerateMaterialValidation), nameof(GenerateMaterialHasGenerations))]
        public static void GenerateMaterial() => OnAssetGenerationRequest(new[] { Selection.activeObject });

        public static bool GenerateMaterialValidation() => OnAssetGenerationValidation(new[] { Selection.activeObject }) && Selection.objects.Length == 1;

        public static bool GenerateMaterialHasGenerations() => IncludesGenerationHistory(new[] { Selection.activeObject });

        [MenuItem("Assets/Create/Rendering/Generate Material", false, -1000)]
        public static void EmptyMaterialMenu() => CreateAndNameMaterial();

        [MenuItem("Assets/Create/Rendering/Generate Material", true)]
        static bool ValidateEmptyMaterialMenu() => Account.settings.AiGeneratorsEnabled;

        public static UnityEngine.Material EmptyMaterial()
        {
            var material = AssetUtils.CreateAndSelectBlankMaterial();
            Selection.activeObject = material;
            OnAssetGenerationRequest(new[] { Selection.activeObject });
            return material;
        }

        public static void CreateAndNameMaterial()
        {
            var icon = EditorGUIUtility.ObjectContent(null, typeof(UnityEngine.Material))?.image as Texture2D;
            var doCreate = ScriptableObject.CreateInstance<DoCreateBlankAsset>();
            doCreate.action = (_, pathName, _) =>
            {
                pathName = AssetUtils.CreateBlankMaterial(pathName);
                if (string.IsNullOrEmpty(pathName))
                    Debug.Log($"Failed to create material file for '{pathName}'.");
                AssetDatabase.ImportAsset(pathName, ImportAssetOptions.ForceUpdate);
                var material = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(pathName);
                Selection.activeObject = material;
                GenerateMaterial();
            };
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(
                0,
                doCreate,
                $"{AssetUtils.defaultNewAssetName}{AssetUtils.materialExtension}",
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
            var generateButtonTooltip = "Use generative AI to transform this material.";
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
                if (AssetUtils.IsShaderGraph(obj))
                {
                    var shaderPath = AssetDatabase.GetAssetPath(obj);
                    var newMaterial = AssetUtils.CreateMaterialFromShaderGraph(shaderPath);
                    if (newMaterial != null)
                    {
                        OpenGenerationWindow(AssetDatabase.GetAssetPath(newMaterial));
                    }
                }
                else if (TryGetValidMaterialPath(obj, out var validPath))
                {
                    OpenGenerationWindow(validPath);
                }
            }
        }

        static bool OnAssetGenerationValidation(IEnumerable<Object> objects)
        {
            foreach (var obj in objects)
            {
                if (AssetDatabase.IsOpenForEdit(obj) && TryGetValidMaterialPath(obj, out _))
                {
                    return true;
                }
            }

            return false;
        }

        static bool TryGetValidMaterialPath(Object obj, out string path)
        {
            path = obj switch
            {
                UnityEngine.Material material => AssetDatabase.GetAssetPath(material),
                _ => null
            };

            if (string.IsNullOrEmpty(path))
                path = AssetDatabase.GetAssetPath(obj);

            return obj is UnityEngine.Material && !string.IsNullOrEmpty(path);
        }

        static bool OnAssetGenerationMultipleValidation(IReadOnlyCollection<Object> objects) => objects.Any(o => TryGetValidMaterialPath(o, out _));

        static bool IncludesGenerationHistory(IReadOnlyCollection<Object> objects) =>
            objects.FirstOrDefault(o => new AssetReference { guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(o)) }.HasGenerations());

        public static void OpenGenerationWindow(string assetPath) => MaterialGeneratorWindow.Display(assetPath);
    }
}
