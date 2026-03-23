using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Image.Services.Utilities;
using Unity.AI.Toolkit.GenerationContextMenu;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Accounts.Services;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Image.Windows
{
    static class TextureGeneratorInspectorButton
    {
        [GenerateContextMenu(nameof(GenerateImageValidation), nameof(GenerateImageHasGenerations))]
        public static void GenerateImage() => OnAssetGenerationRequest(new[] { Selection.activeObject });

        public static bool GenerateImageValidation() => OnAssetGenerationValidation(new[] { Selection.activeObject }) && Selection.objects.Length == 1;

        public static bool GenerateImageHasGenerations() => IncludesGenerationHistory(new[] { Selection.activeObject });
#if !ENHANCERS_2D_PRESENT
        [MenuItem("Assets/Create/2D/Generate Sprite", false, -1000)]
        public static void Empty2dSpriteMenu() => CreateAndNameSprite();

        [MenuItem("Assets/Create/2D/Generate Sprite", true)]
        static bool ValidateEmpty2dSpriteMenu() => Account.settings.AiGeneratorsEnabled;
#else
        [MenuItem("Assets/Create/2D/Sprites/Generate Sprite", true)]
        static bool ValidateEmpty2dSpriteEnhancersMenu() => Account.settings.AiGeneratorsEnabled;
#endif
        [MenuItem("Assets/Create/Rendering/Generate Sprite", false, -1000)] // menuitem required for Unity.AI.Generators.UI.AIDropdownIntegrations
        public static void EmptySpriteMenu() => CreateAndNameSprite();

        [MenuItem("Assets/Create/Rendering/Generate Sprite", true)]
        static bool ValidateEmptySpriteMenu() => Account.settings.AiGeneratorsEnabled;

        [MenuItem("Assets/Create/Rendering/Generate Texture 2D", false, -1000)]
        public static void EmptyTextureMenu() => CreateAndNameTexture();

        [MenuItem("Assets/Create/Rendering/Generate Texture 2D", true)]
        static bool ValidateEmptyTextureMenu() => Account.settings.AiGeneratorsEnabled;

        [MenuItem("Assets/Create/Rendering/Generate Cubemap", false, -1000)]
        public static void EmptyCubemapMenu() => CreateAndNameCubemap();

        [MenuItem("Assets/Create/Rendering/Generate Cubemap", true)]
        static bool ValidateCubemapMenu() => Account.settings.AiGeneratorsEnabled;


        public static void CreateAndNameSprite()
        {
            var icon = EditorGUIUtility.ObjectContent(null, typeof(Sprite))?.image as Texture2D;
            var doCreate = ScriptableObject.CreateInstance<DoCreateBlankAsset>();
            doCreate.action = (_, pathName, _) =>
            {
                pathName = AssetDatabase.GenerateUniqueAssetPath(pathName);
                pathName = AssetUtils.CreateBlankSprite(pathName);
                if (string.IsNullOrEmpty(pathName))
                    Debug.Log($"Failed to create sprite file for '{pathName}'.");
                AssetDatabase.ImportAsset(pathName, ImportAssetOptions.ForceUpdate);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(pathName);
                Selection.activeObject = sprite;
                GenerateImage();
            };
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(
                0,
                doCreate,
                $"{AssetUtils.defaultNewAssetNameSprite}{AssetUtils.defaultAssetExtension}",
                icon,
                null,
                true);
        }

        public static void CreateAndNameTexture()
        {
            var icon = EditorGUIUtility.ObjectContent(null, typeof(Texture))?.image as Texture2D;
            var doCreate = ScriptableObject.CreateInstance<DoCreateBlankAsset>();
            doCreate.action = (_, path, _) =>
            {
                path = AssetDatabase.GenerateUniqueAssetPath(path);
                path = AssetUtils.CreateBlankTexture(path, true);
                if (string.IsNullOrEmpty(path))
                    Debug.Log($"Failed to create texture file for '{path}'.");
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Selection.activeObject = texture;
                GenerateImage();
            };
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(
                0,
                doCreate,
                $"{AssetUtils.defaultNewAssetName}{AssetUtils.defaultAssetExtension}",
                icon,
                null,
                true);
        }

        public static void CreateAndNameCubemap()
        {
            var icon = EditorGUIUtility.ObjectContent(null, typeof(Cubemap))?.image as Texture2D;
            var doCreate = ScriptableObject.CreateInstance<DoCreateBlankAsset>();
            doCreate.action = (_, path, _) =>
            {
                path = AssetDatabase.GenerateUniqueAssetPath(path);
                path = AssetUtils.CreateBlankCubemap(path);
                if (string.IsNullOrEmpty(path))
                    Debug.Log($"Failed to create texture file for '{path}'.");
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var texture = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                Selection.activeObject = texture;
                GenerateImage();
            };
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(
                0,
                doCreate,
                $"{AssetUtils.defaultNewAssetNameCube}{AssetUtils.defaultAssetExtension}",
                icon,
                null,
                true);
        }

        [InitializeOnLoadMethod]
        static void EditorHeaderButtons() => Editor.finishedDefaultHeaderGUI += OnHeaderControlsGUI;

        static void OnHeaderControlsGUI(Editor editor)
        {
            if (!EditorUtility.IsPersistent(editor.target))
                return;

            if (!OnAssetGenerationValidation(editor.targets))
                return;

            var assetPath = AssetDatabase.GetAssetPath(editor.target);
            var textureImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;

            var isDisabledAndHasNoHistory = !Account.settings.AiGeneratorsEnabled && !IncludesGenerationHistory(editor.targets);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUI.BeginDisabledGroup(!OnAssetGenerationMultipleValidation(editor.targets) || isDisabledAndHasNoHistory);
            var generateButtonTooltip = $"Use generative AI to transform this {(textureImporter ? textureImporter.textureType : TextureImporterType.Default).ToString()} texture.";
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
                if (TryGetValidTexturePath(obj, out var validPath))
                {
                    OpenGenerationWindow(validPath);
                }
            }
        }

        static bool OnAssetGenerationValidation(IEnumerable<Object> objects)
        {
            foreach (var obj in objects)
            {
                if (AssetDatabase.IsOpenForEdit(obj) && TryGetValidTexturePath(obj, out _))
                {
                    return true;
                }
            }

            return false;
        }

        static bool TryGetValidTexturePath(Object obj, out string path)
        {
            switch (obj)
            {
                case Texture2D texture:
                    path = AssetDatabase.GetAssetPath(texture);
                    break;
                case TextureImporter importer:
                    path = importer.assetPath;
                    break;
                default:
                    path = null;
                    break;
            }

            if (string.IsNullOrEmpty(path))
                path = AssetDatabase.GetAssetPath(obj);

            if (!string.IsNullOrEmpty(path))
            {
                var extension = Path.GetExtension(path).ToLower();
                if (ImageFileUtilities.knownExtensions.Any(suffix => suffix.Equals(extension, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        static bool OnAssetGenerationMultipleValidation(IReadOnlyCollection<Object> objects) => objects.FirstOrDefault(o => TryGetValidTexturePath(o, out _));

        static bool IncludesGenerationHistory(IReadOnlyCollection<Object> objects) =>
            objects.FirstOrDefault(o => new AssetReference { guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(o)) }.HasGenerations());

        internal static void OpenGenerationWindow(string assetPath) => TextureGeneratorWindow.Display(assetPath);
    }
}
