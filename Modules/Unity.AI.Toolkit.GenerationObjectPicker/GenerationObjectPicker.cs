using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Unity.AI.Toolkit.Accounts.Services;
using Unity.AI.Toolkit;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
#if OBJECT_SELECTOR_TOOLBAR_DECORATOR
using System.Threading.Tasks;
using Unity.AI.Toolkit.Asset;
using UnityEditor.PackageManager;
using UnityEditor.UIElements;
#endif

namespace Unity.AI.Toolkit
{
    static class GenerationObjectPicker
    {
        static readonly Dictionary<string, RegisteredTemplate> k_RegisteredTemplates = new();

        /// <summary>
        /// Flags for controlling template visibility and filtering behavior.
        /// </summary>
        [Flags]
        public enum TemplateFlags
        {
            /// <summary>No special flags.</summary>
            None = 0,
            /// <summary>
            /// Template is for skybox/cubemap material generation.
            /// When set, this template is only shown when editing a skybox material field.
            /// </summary>
            SkyboxOnly = 1,
        }

        record RegisteredTemplate(Func<string, bool, string> createTemplate, string assetPath, Action<string> createAsset, Type assetType, TemplateFlags flags = TemplateFlags.None)
        {
            public Object templateObject;
        }

        /// <summary>
        /// Register an asset generation template to be used with the ObjectPicker
        /// </summary>
        /// <param name="modality">modality name</param>
        /// <param name="createTemplate">blank asset template create function</param>
        /// <param name="assetPath">generate asset path on template pick</param>
        /// <param name="createAsset">generate asset action on template pick</param>
        /// <param name="flags">optional template flags for context-specific filtering</param>
        public static void RegisterTemplate<T>(string modality, Func<string, bool, string> createTemplate, string assetPath, Action<string> createAsset, TemplateFlags flags = TemplateFlags.None) where T : Object =>
            k_RegisteredTemplates.TryAdd(modality, new RegisteredTemplate(createTemplate, assetPath, createAsset, typeof(T), flags));

        static readonly string k_OldTemplatesDirectory = Path.Combine("Assets", "AI Toolkit", "Templates");

        [InitializeOnLoadMethod]
        static void ObjectPickerBlankGenerationHook()
        {
            // This is a one-time migration to remove the old templates directory
            var assetGuid = AssetDatabase.AssetPathToGUID(k_OldTemplatesDirectory);
            if (!string.IsNullOrEmpty(assetGuid))
                AssetDatabase.DeleteAsset(k_OldTemplatesDirectory);
        }

#if OBJECT_SELECTOR_TOOLBAR_DECORATOR
        [InitializeOnLoadMethod]
        static void SetupSelector()
        {
            try
            {
                ObjectSelectorUtils.SetupShownEventHandler(OnObjectSelectorShown);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to setup ObjectSelector toolbar decorator: {e.Message}");
            }
        }

        static void OnObjectSelectorShown(EditorWindow window)
        {
            var allowedTypes = ObjectSelectorUtils.GetAllowedTypes();
            if (allowedTypes is not { Length: > 0 })
                return;

            var isSkyboxContext = ObjectSelectorUtils.IsSkyboxContext(window);

            var templates = new List<RegisteredTemplate>();
            foreach (var (modality, template) in k_RegisteredTemplates)
            {
                foreach (var allowedType in allowedTypes)
                {
                    if (allowedType == null)
                        continue;

                    if (allowedType.IsAssignableFrom(template.assetType))
                    {
                        // Filter templates based on context flags and current state
                        if ((template.flags & TemplateFlags.SkyboxOnly) != 0)
                        {
                            // SkyboxOnly templates should only appear in skybox context
                            if (!isSkyboxContext)
                                continue;
                        }
                        else if (template.assetType == typeof(Material) && isSkyboxContext)
                        {
                            // Skip non-skybox Material templates when in skybox context
                            continue;
                        }

                        templates.Add(template);
                        break;
                    }
                }
            }

            if (templates.Count <= 0)
                return;

            // add a "Generate New" button next to the last button in the window's toolbar
            var toggle = ObjectSelectorUtils.GetTargetElement(window);
            if (toggle == null)
                return;

            var generateButton = new ToolbarButton { text = "Generate New" };
            generateButton.SetEnabled(Account.settings.AiGeneratorsEnabled);
            toggle.parent.Insert(toggle.parent.IndexOf(toggle), generateButton);
            generateButton.clicked += () =>
            {
                if (templates.Count == 1)
                {
                    SetSelectionFromTemplate(templates[0]);
                    return;
                }

                var menu = new GenericMenu();
                foreach (var template in templates)
                {
                    menu.AddItem(new GUIContent(Path.GetFileNameWithoutExtension(template.assetPath)),
                        false, () => SetSelectionFromTemplate(template));
                }

                menu.DropDown(generateButton.worldBound);
            };

            return;

            async void SetSelectionFromTemplate(RegisteredTemplate template)
            {
                var assetNameAndExtension = Path.GetFileName(template.assetPath);
                var dirPath = await GetActiveDirectoryOrDefault();
                var path = Path.Combine(dirPath, assetNameAndExtension);
                path = AssetDatabase.GenerateUniqueAssetPath(path);
                path = template.createTemplate(path, true);
                var assets = AssetDatabase.LoadAllAssetsAtPath(path);

                // Find the first asset that matches the template's asset type
                Object selectedAsset = null;
                foreach (var asset in assets)
                {
                    if (asset != null && template.assetType.IsAssignableFrom(asset.GetType()))
                    {
                        selectedAsset = asset;
                        break;
                    }
                }

                // If no matching asset was found, fall back to the first asset
                if (selectedAsset == null && assets.Length > 0)
                    selectedAsset = assets[0];

                if (selectedAsset == null)
                    return;

                EditorApplication.delayCall += () => template.createAsset(path);
                ObjectSelectorUtils.SetSelection(selectedAsset.GetInstanceID());
            }

            static async Task<string> GetActiveDirectoryOrDefault()
            {
                var dirPath = string.Empty;
                var activeFolderPath = AssetUtilities.GetSelectionPath();

                if (!string.IsNullOrEmpty(activeFolderPath))
                {
                    if (!activeFolderPath.StartsWith("Assets"))
                    {
                        var req = Client.List(true);
                        while (!req.IsCompleted)
                        {
                            await EditorTask.Delay(100);
                        }
                        if (req.Status == StatusCode.Success)
                        {
                            foreach (var package in req.Result)
                            {
                                if (activeFolderPath.StartsWith(package.assetPath))
                                {
                                    if (package.source == PackageSource.Local)
                                    {
                                        dirPath = activeFolderPath;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        dirPath = activeFolderPath;
                    }
                }

                dirPath = string.IsNullOrEmpty(dirPath) ? "Assets" : dirPath;
                return dirPath;
            }
        }
#endif // OBJECT_SELECTOR_TOOLBAR_DECORATOR
    }
}
