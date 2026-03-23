using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Mesh.Services.Utilities;
using Unity.AI.Toolkit.GenerationContextMenu;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Accounts.Services;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.UI.AIDropdownIntegrations;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Mesh.Windows
{
    static class MeshGeneratorInspectorButton
    {
        static readonly List<Component> k_ComponentsCache = new();

        [GenerateContextMenu(nameof(GenerateMeshValidation), nameof(GenerateMeshHasGenerations))]
        public static void GenerateMesh() => OnAssetGenerationRequest(new[] { Selection.activeObject });

        public static bool GenerateMeshValidation() => OnAssetGenerationValidation(new[] { Selection.activeObject }) && Selection.objects.Length == 1;

        public static bool GenerateMeshHasGenerations() => IncludesGenerationHistory(new[] { Selection.activeObject });

        [MenuItem("Assets/Create/3D/Generate 3D Object", false, -199)]
        public static void EmptyMeshMenu() => CreateAndNameMesh();

        [MenuItem("Assets/Create/3D/Generate 3D Object", true)]
        static bool ValidateEmptyMeshMenu() => Account.settings.AiGeneratorsEnabled;

        [MenuItem("GameObject/3D Object/Generate 3D Object", false, -1)]
        public static void CreateInSceneAndNameMesh()
        {
            var icon = EditorGUIUtility.ObjectContent(null, typeof(GameObject))?.image as Texture2D;
            var doCreate = ScriptableObject.CreateInstance<DoCreateBlankAsset>();
            doCreate.action = (_, path, _) =>
            {
                path = AssetDatabase.GenerateUniqueAssetPath(path);
                path = AssetUtils.CreateBlankPrefab(path);
                if (string.IsNullOrEmpty(path))
                    return;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    Debug.LogError($"Failed to load prefab at path: {path}");
                    return;
                }

                var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (instance != null)
                {
                    GameObjectUtility.SetParentAndAlign(instance, Selection.activeObject as GameObject);
                    Undo.RegisterCreatedObjectUndo(instance, $"Create {instance.name}");
                    Selection.activeObject = instance;
                }
                else
                {
                    Debug.LogError($"Failed to instantiate prefab: {prefab.name}");
                    return;
                }

                OnAssetGenerationRequest(new[] { prefab });
            };
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(
                0,
                doCreate,
                $"{AssetUtils.defaultNewAssetName}.prefab",
                icon,
                null,
                false);
        }

        [MenuItem("GameObject/3D Object/Generate 3D Object", true)]
        static bool ValidateCreateInSceneAndNameMesh() => Account.settings.AiGeneratorsEnabled;

        public static GameObject EmptyMesh()
        {
            var prefab = AssetUtils.CreateAndSelectBlankPrefab();
            Selection.activeObject = prefab;
            GenerateMesh();
            return prefab;
        }

        public static void CreateAndNameMesh()
        {
            var icon = EditorGUIUtility.ObjectContent(null, typeof(GameObject))?.image as Texture2D;
            var doCreate = ScriptableObject.CreateInstance<DoCreateBlankAsset>();
            doCreate.action = (_, path, _) =>
            {
                path = AssetDatabase.GenerateUniqueAssetPath(path);
                path = AssetUtils.CreateBlankPrefab(path);
                if (string.IsNullOrEmpty(path))
                    return;
                new AssetReference {guid = AssetDatabase.AssetPathToGUID(path)}.EnableGenerationLabel();
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Selection.activeObject = prefab;
                GenerateMesh();
            };
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(
                0,
                doCreate,
                $"New Mesh.prefab",
                icon,
                null,
                true);
        }

        static void OnAssetGenerationRequest(IEnumerable<Object> objects)
        {
            foreach (var obj in objects)
            {
                if (TryGetValidPrefabPath(obj, out var validPath))
                {
                    OpenGenerationWindow(validPath);
                }
            }
        }

        static bool OnAssetGenerationValidation(IEnumerable<Object> objects)
        {
            foreach (var obj in objects)
            {
                if (AssetDatabase.IsOpenForEdit(obj) && TryGetValidPrefabPath(obj, out _))
                {
                    if (obj.HasGenerationLabel() || obj.HasGenerations())
                        return true;

                    // Also allow empty GameObjects (prefabs with only Transform component)
                    if (obj is GameObject gameObject && IsEmptyGameObject(gameObject))
                        return true;
                }
            }

            return false;
        }

        static bool IsEmptyGameObject(GameObject gameObject)
        {
            gameObject.GetComponents(k_ComponentsCache);
            // Only Transform component means it's empty
            return k_ComponentsCache.Count == 1 && k_ComponentsCache[0] is Transform;
        }

        [InitializeOnLoadMethod]
        static void EditorHeaderButtons() => Editor.finishedDefaultHeaderGUI += OnHeaderControlsGUI;

        static void OnHeaderControlsGUI(Editor editor)
        {
            if (!EditorUtility.IsPersistent(editor.target))
                return;

            if (!OnAssetGenerationValidation(editor.targets))
                return;

            var isDisabledAndHasNoHistory = !Account.settings.AiGeneratorsEnabled && !IncludesGenerationHistory(editor.targets);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUI.BeginDisabledGroup(!OnAssetGenerationMultipleValidation(editor.targets) || isDisabledAndHasNoHistory);
            var generateButtonTooltip = "Use generative AI to transform this prefab with Mesh Filters.";
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

        static bool TryGetValidPrefabPath(Object obj, out string path)
        {
            switch (obj)
            {
                case GameObject gameObject:
                    path = AssetDatabase.GetAssetPath(gameObject);
                    break;
                default:
                    path = null;
                    break;
            }

            if (string.IsNullOrEmpty(path))
                path = AssetDatabase.GetAssetPath(obj);

            if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab"))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                return prefab != null;
            }

            return false;
        }

        static bool OnAssetGenerationMultipleValidation(IReadOnlyCollection<Object> objects) => objects.FirstOrDefault(o => TryGetValidPrefabPath(o, out _));

        static bool IncludesGenerationHistory(IReadOnlyCollection<Object> objects) =>
            objects.FirstOrDefault(o => o.HasGenerations());

        internal static void OpenGenerationWindow(string assetPath) => MeshGeneratorWindow.Display(assetPath);
    }
}
