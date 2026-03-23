using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Animate.Services.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Accounts.Services;
using Unity.AI.Toolkit.Asset;
using Unity.AI.Toolkit.GenerationContextMenu;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Animate.Windows
{
    static class AnimateGeneratorInspectorButton
    {
        [GenerateContextMenu(nameof(GenerateAnimateValidation), nameof(GenerateAnimateHasGenerations))]
        public static void GenerateAnimate() => OnAssetGenerationRequest(new[] { Selection.activeObject });

        public static bool GenerateAnimateValidation() => OnAssetGenerationValidation(new[] { Selection.activeObject }) && Selection.objects.Length == 1;

        public static bool GenerateAnimateHasGenerations() => IncludesGenerationHistory(new[] { Selection.activeObject });

        [MenuItem("Assets/Create/Animation/Generate Animation Clip", false, -1000)]
        public static void EmptyAnimateMenu() => CreateAndNameAnimate();

        [MenuItem("Assets/Create/Animation/Generate Animation Clip", true)]
        static bool ValidateEmptyAnimateMenu() => Account.settings.AiGeneratorsEnabled;

        public static AnimationClip EmptyAnimate()
        {
            var animate = AssetUtils.CreateAndSelectBlankAnimation();
            Selection.activeObject = animate;
            GenerateAnimate();
            return animate;
        }

        public static void CreateAndNameAnimate()
        {
            var icon = EditorGUIUtility.ObjectContent(null, typeof(AnimationClip))?.image as Texture2D;
            var doCreate = ScriptableObject.CreateInstance<DoCreateBlankAsset>();
            doCreate.action = (_, pathName, _) =>
            {
                pathName = AssetUtils.CreateBlankAnimation(pathName);
                if (string.IsNullOrEmpty(pathName))
                    Debug.Log($"Failed to create animation clip for '{pathName}'.");
                AssetDatabase.ImportAsset(pathName, ImportAssetOptions.ForceUpdate);
                var animate = AssetDatabase.LoadAssetAtPath<AnimationClip>(pathName);
                Selection.activeObject = animate;
                GenerateAnimate();
            };
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(
                0,
                doCreate,
                $"{AssetUtils.defaultNewAssetName}{AssetUtils.defaultAssetExtension}",
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

            var isDisabledAndHasNoHistory = !Account.settings.AiGeneratorsEnabled && !IncludesGenerationHistory(editor.targets); /* IncludesGenerationHistory is costly, check only when needed */

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUI.BeginDisabledGroup(!OnAssetGenerationMultipleValidation(editor.targets) || isDisabledAndHasNoHistory);
            var generateButtonTooltip = "Use generative AI to transform this animation clip.";
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
                if (TryGetValidAnimatePath(obj, out var validPath))
                {
                    OpenGenerationWindow(validPath);
                }
            }
        }

        static bool OnAssetGenerationValidation(IEnumerable<Object> objects)
        {
            foreach (var obj in objects)
            {
                if (AssetDatabase.IsOpenForEdit(obj) && TryGetValidAnimatePath(obj, out _) && !IsEmbeddedInReadOnlyAsset(obj))
                {
                    return true;
                }
            }

            return false;
        }

        static bool TryGetValidAnimatePath(Object obj, out string path)
        {
            path = obj switch
            {
                AnimationClip animate => AssetDatabase.GetAssetPath(animate),
                _ => null
            };

            if (string.IsNullOrEmpty(path))
                path = AssetDatabase.GetAssetPath(obj);

            return obj is AnimationClip && !string.IsNullOrEmpty(path);
        }

        static bool OnAssetGenerationMultipleValidation(IReadOnlyCollection<Object> objects) =>
            objects.FirstOrDefault(o => TryGetValidAnimatePath(o, out _) && !IsEmbeddedInReadOnlyAsset(o));

        static bool IncludesGenerationHistory(IReadOnlyCollection<Object> objects) =>
            objects.FirstOrDefault(o => new AssetReference { guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(o)) }.HasGenerations());

        public static void OpenGenerationWindow(string assetPath) => AnimateGeneratorWindow.Display(assetPath);

        /// <summary>
        /// Checks if the animation clip is embedded in a non-editable asset like an imported FBX file.
        /// </summary>
        static bool IsEmbeddedInReadOnlyAsset(Object obj)
        {
            if (obj is not AnimationClip)
                return false;

            var assetPath = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(assetPath))
                return false;

            // Check if this is a common imported model format
            var extension = System.IO.Path.GetExtension(assetPath).ToLower();
            var isImportedModelFormat = extension is AssetUtils.fbxAssetExtension or ".dae" or ".obj" or ".blend" or ".3ds" or ".max";

            if (!isImportedModelFormat)
                return false;

            // Check if this clip is a sub-asset (not the main asset)
            var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            return mainAsset != obj;
        }
    }
}
