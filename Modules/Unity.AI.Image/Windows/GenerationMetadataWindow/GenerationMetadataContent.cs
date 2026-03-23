using System;
using System.Collections.Generic;
using System.IO;
using Unity.AI.Generators.Asset;
using Unity.AI.Image.Components;
using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Image.Services.Utilities;
using Unity.AI.Image.Utilities;
using Unity.AI.ModelSelector.Services.Stores.States;
using Unity.AI.ModelSelector.Services.Utilities;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Windows
{
    class GenerationMetadataContent : VisualElement
    {
        public event Action OnDismissRequested;

        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Windows/GenerationMetadataWindow/GenerationMetadataWindow.uxml";
        const string k_UxmlDoodleTemplate = "Packages/com.unity.ai.assistant/Modules/Unity.AI.Image/Windows/GenerationMetadataWindow/GenerationMetadataDoodleTemplate.uxml";

        readonly Store m_Store;
        readonly GenerationMetadata m_GenerationMetadata;

        ModelSettings m_ModelSettings;
        List<Button> m_DismissButtons;

        public GenerationMetadataContent(IStore store, GenerationMetadata generationMetadata)
        {
            m_GenerationMetadata = generationMetadata;
            m_Store = (Store)store;

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            var useAllButton = this.Q<Button>("use-all-button");
            useAllButton.clicked += UseAll;

            InitRefinementMode();
            InitModel();
            InitPrompt();
            InitNegativePrompt();
            InitCustomSeed();
            InitUpscaleFactor();
            InitDimensions();
            InitImageReferences();

            m_DismissButtons = this.Query<Button>(className: "data-button").ToList();
            foreach (var button in m_DismissButtons)
            {
                button.clicked += OnDismiss;
            }
        }

        void InitRefinementMode()
        {
            var refinementModeContainer = this.Q<VisualElement>(className: "refinement-mode-container");
            var refinementModeMetadata = this.Q<Label>("refinement-mode-metadata");
            var refinementModeUseButton = this.Q<Button>("use-refinement-mode-button");

            var refinementMode = m_GenerationMetadata?.refinementMode;
            refinementModeMetadata.text = refinementMode.AddSpaceBeforeCapitalLetters();
            refinementModeUseButton.clicked += UseRefinementMode;
            refinementModeContainer.EnableInClassList("hidden", string.IsNullOrEmpty(refinementMode));

            InitPixelate();
        }

        void InitPixelate()
        {
            var refinementMode = m_GenerationMetadata?.refinementMode;
            var pixelateContainer = this.Q<VisualElement>(className: "pixelate-container");
            var showPixelate = !string.IsNullOrEmpty(refinementMode) && refinementMode == RefinementMode.Pixelate.ToString();
            pixelateContainer.EnableInClassList("hidden", !showPixelate);
            if (!showPixelate || m_GenerationMetadata == null)
                return;

            var size = this.Q<Label>("pixelate-size-metadata");
            size.text = m_GenerationMetadata.pixelateTargetSize.ToString();

            var keepSize = this.Q<Label>("pixelate-keep-image-size-metadata");
            keepSize.text = m_GenerationMetadata.pixelateKeepImageSize.ToString();

            var samplingSize = this.Q<Label>("pixelate-sampling-size-metadata");
            samplingSize.text = m_GenerationMetadata.pixelatePixelBlockSize.ToString();

            var pixelateMode = this.Q<Label>("pixelate-pixelate-mode-metadata");
            pixelateMode.text = m_GenerationMetadata.pixelateMode.ToString();

            var outlineThickness = this.Q<Label>("pixelate-outline-thickness-metadata");
            outlineThickness.text = m_GenerationMetadata.pixelateOutlineThickness.ToString();

            var pixelateUseButton = this.Q<Button>("use-pixelate-button");
            pixelateUseButton.clicked += UsePixelate;

            var pixelateCopyButton = this.Q<Button>("copy-pixelate-button");
            pixelateCopyButton.clicked += () =>
            {
                var pixelateSettings = GetPixelateSettingsFromGenerationMetadata(m_GenerationMetadata);
                EditorGUIUtility.systemCopyBuffer = JsonUtility.ToJson(pixelateSettings);
            };
        }

        void InitModel()
        {
            m_ModelSettings = m_Store?.State?.SelectModelSettings(m_GenerationMetadata);
            var modelName = m_ModelSettings?.name;
            if (string.IsNullOrEmpty(modelName))
            {
                if(!string.IsNullOrEmpty(m_GenerationMetadata.modelName))
                    modelName = m_GenerationMetadata.modelName;
                else
                    modelName = "Invalid Model";
            }

            var modelContainer = this.Q<VisualElement>(className: "model-container");
            var modelMetadata = this.Q<Label>("model-metadata");
            var modelCopyButton = this.Q<Button>("copy-model-button");
            var modelUseButton = this.Q<Button>("use-model-button");

            modelMetadata.text = modelName;
            modelCopyButton.clicked += () =>
            {
                EditorGUIUtility.systemCopyBuffer = m_GenerationMetadata?.model;
            };
            modelUseButton.clicked += UseModel;
            modelUseButton.SetEnabled(m_ModelSettings.IsValid());
            modelContainer.EnableInClassList("hidden", string.IsNullOrEmpty(modelName));
        }

        void InitPrompt()
        {
            var promptContainer = this.Q<VisualElement>(className: "prompt-container");
            var promptMetadata = this.Q<Label>("prompt-metadata");
            var promptCopyButton = this.Q<Button>("copy-prompt-button");
            var promptUseButton = this.Q<Button>("use-prompt-button");

            var prompt = m_GenerationMetadata?.prompt;
            promptMetadata.text = prompt;
            promptCopyButton.clicked += () =>
            {
                EditorGUIUtility.systemCopyBuffer = prompt;
            };
            promptUseButton.clicked += UsePrompt;
            promptContainer.EnableInClassList("hidden", string.IsNullOrEmpty(prompt));
        }

        void InitNegativePrompt()
        {
            var negativePromptContainer = this.Q<VisualElement>(className: "negative-prompt-container");
            var negativePromptMetadata = this.Q<Label>("negative-prompt-metadata");
            var negativePromptCopyButton = this.Q<Button>("copy-negative-prompt-button");
            var negativePromptUseButton = this.Q<Button>("use-negative-prompt-button");

            var negativePrompt = m_GenerationMetadata?.negativePrompt;
            negativePromptMetadata.text = negativePrompt;
            negativePromptCopyButton.clicked += () =>
            {
                EditorGUIUtility.systemCopyBuffer = negativePrompt;
            };
            negativePromptUseButton.clicked += UseNegativePrompt;
            negativePromptContainer.EnableInClassList("hidden", string.IsNullOrEmpty(negativePrompt));
        }

        void InitCustomSeed()
        {
            var customSeedContainer = this.Q<VisualElement>(className: "custom-seed-container");
            var customSeedMetadata = this.Q<Label>("custom-seed-metadata");
            var customSeedCopyButton = this.Q<Button>("copy-custom-seed-button");
            var customSeedUseButton = this.Q<Button>("use-custom-seed-button");

            var customSeed = m_GenerationMetadata?.customSeed;
            customSeedMetadata.text = customSeed.ToString();
            customSeedCopyButton.clicked += () =>
            {
                EditorGUIUtility.systemCopyBuffer = customSeed.ToString();
            };
            customSeedUseButton.clicked += UseCustomSeed;
            customSeedContainer.EnableInClassList("hidden", customSeed == -1);
        }

        void InitUpscaleFactor()
        {
            var upscaleFactorContainer = this.Q<VisualElement>(className: "upscale-factor-container");
            var upscaleFactorMetadata = this.Q<Label>("upscale-factor-metadata");
            var upscaleFactorCopyButton = this.Q<Button>("copy-upscale-factor-button");
            var upscaleFactorUseButton = this.Q<Button>("use-upscale-factor-button");

            var upscaleFactor = m_GenerationMetadata?.upscaleFactor;
            upscaleFactorMetadata.text = upscaleFactor.ToString();
            upscaleFactorCopyButton.clicked += () =>
            {
                EditorGUIUtility.systemCopyBuffer = upscaleFactor.ToString();
            };
            upscaleFactorUseButton.clicked += UseUpscaleFactor;
            upscaleFactorContainer.EnableInClassList("hidden", upscaleFactor == 0);
        }

        void InitDimensions()
        {
            var dimensionsContainer = this.Q<VisualElement>(className: "dimensions-container");
            var dimensionsMetadata = this.Q<Label>("dimensions-metadata");

            var dimensions = m_GenerationMetadata?.dimensions;
            var hasDimensions = dimensions is { width: > 0 } or { height: > 0 };

            dimensionsContainer.EnableInClassList("hidden", !hasDimensions);

            if (hasDimensions)
            {
                dimensionsMetadata.text = $"{dimensions.width}x{dimensions.height}";
            }
        }

        void InitImageReferences()
        {
            var imageReferencesContainer = this.Q<VisualElement>(className: "image-references-container");
            var hasImageReferences = m_GenerationMetadata.doodles is { Length: > 0 };

            imageReferencesContainer.EnableInClassList("hidden", !hasImageReferences);
            if (!hasImageReferences)
                return;

            var doodleTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_UxmlDoodleTemplate);

            foreach(var doodleData in m_GenerationMetadata.doodles)
            {
                if (m_GenerationMetadata?.doodles is not { Length: > 0 }) return;
                if (!Enum.IsDefined(typeof(ImageReferenceType), doodleData.doodleReferenceType)) return;

                var doodleUI = doodleTemplate.Instantiate();

                var referenceTypeLabel = doodleUI.Q<Label>("doodle-type");
                referenceTypeLabel.text = doodleData.label;

                var doodleStrengthContainer = doodleUI.Q<VisualElement>(className: "doodle-strength-container");
                var strength = doodleUI.Q<Label>("doodle-strength-metadata");

                var displayStrength = doodleData.invertStrength ? 100 - doodleData.strength * 100.0f : doodleData.strength * 100.0f;
                strength.text = displayStrength.ToString();
                doodleStrengthContainer.EnableInClassList("hidden", Mathf.Approximately(doodleData.strength, 0f));

                var imageReferenceUseButton = doodleUI.Q<Button>("use-doodle-button");
                imageReferenceUseButton.clicked += () => { UseDoodle(doodleData); };

                var imageReferenceCopyButton = doodleUI.Q<Button>("copy-doodle-button");

                if (doodleData.doodle is { Length: > 0 }) // it's a doodle
                {
                    var doodlePad = doodleUI.Q<DoodlePad>();
                    if (doodlePad == null) return;

                    doodlePad.EnableInClassList("hidden", false);
                    doodlePad.SetDoodle(doodleData.doodle);
                    doodlePad.SetNone();
                    imageReferenceCopyButton.clicked += () =>
                    {
                        EditorGUIUtility.systemCopyBuffer = "MetadataDoodleBytes:" + Convert.ToBase64String(doodleData.doodle);
                    };
                }
                else // it's an asset reference
                {
                    var objectField = doodleUI.Q<ObjectField>(className:"metadata-object-field__input-field");
                    if (objectField == null) return;

                    objectField.EnableInClassList("hidden", false);
                    objectField.SetEnabled(false);
                    objectField.EnableInClassList("unity-disabled", false);
                    objectField.name = "metadata-object-field__input-field_" + doodleData.assetReferenceGuid;

                    var assetPath = AssetDatabase.GUIDToAssetPath(doodleData.assetReferenceGuid);
                    if (!string.IsNullOrEmpty(assetPath) && File.Exists(assetPath))
                    {
                        var assetRef = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                        objectField.SetValueWithoutNotify(assetRef);
                        imageReferenceCopyButton.clicked += () =>
                        {
                            EditorGUIUtility.systemCopyBuffer = "MetadataAssetRef:" + doodleData.assetReferenceGuid;
                        };
                    }
                    else
                    {
                        // asset reference not found, might have been deleted
                        objectField.SetValueWithoutNotify(null);
                        var objectFieldLabel = objectField.Q<Label>(className: "unity-object-field-display__label");
                        objectFieldLabel.text = "Reference not found in project";

                        imageReferenceCopyButton.SetEnabled(false);
                        imageReferenceUseButton.SetEnabled(false);
                    }
                }

                imageReferencesContainer.Add(doodleUI);
            }
        }

        void UseRefinementMode()
        {
            var refinementMode = m_GenerationMetadata.refinementMode;

            if(Enum.TryParse<RefinementMode>(refinementMode, out var mode))
                this.Dispatch(GenerationSettingsActions.setRefinementMode, mode);
        }

        void UseModel()
        {
            if (m_ModelSettings.IsValid())
                this.Dispatch(GenerationSettingsActions.setSelectedModelID, (this.GetState().SelectRefinementMode(this), m_ModelSettings.id));
        }

        void UsePrompt()
        {
            var truncatedPrompt = PromptUtilities.TruncatePrompt(m_GenerationMetadata?.prompt);
            this.Dispatch(GenerationSettingsActions.setPrompt, (this.GetState().SelectRefinementMode(this), truncatedPrompt));
        }

        void UseNegativePrompt()
        {
            var truncatedPrompt = PromptUtilities.TruncatePrompt(m_GenerationMetadata?.negativePrompt);
            this.Dispatch(GenerationSettingsActions.setNegativePrompt, (this.GetState().SelectRefinementMode(this), truncatedPrompt));
        }

        void UseCustomSeed()
        {
            if (m_GenerationMetadata.customSeed != -1)
            {
                this.Dispatch(GenerationSettingsActions.setUseCustomSeed, true);
                this.Dispatch(GenerationSettingsActions.setCustomSeed, m_GenerationMetadata.customSeed);
            }
        }

        void UseUpscaleFactor()
        {
            if (m_GenerationMetadata.upscaleFactor != 0)
            {
                this.Dispatch(GenerationSettingsActions.setUpscaleFactor, m_GenerationMetadata.upscaleFactor);
            }
        }

        void UseDoodle(GenerationDataDoodle doodleData)
        {
            if (!Enum.IsDefined(typeof(ImageReferenceType), doodleData.doodleReferenceType)) return;
            var doodlePad = this.Q<DoodlePad>();

            var objectField = this.Q<ObjectField>("metadata-object-field__input-field_" + doodleData.assetReferenceGuid);
            UseRefinementMode();
            var hasImageRef = false;

            if (doodlePad != null && doodleData.doodle is { Length: > 0 })
            {
                doodleData.doodleReferenceType.SetDoodlePadData(doodlePad, doodleData.doodle);
                hasImageRef = true;
            }
            else if(objectField != null && objectField.value != null)
            {
                var assetReference = new AssetReference { guid = doodleData.assetReferenceGuid };
                doodleData.doodleReferenceType.SetAssetReferenceObjectData(objectField, assetReference);
                hasImageRef = true;
            }

            if (hasImageRef && !Mathf.Approximately(doodleData.strength, 0f))
                this.Dispatch(GenerationSettingsActions.setImageReferenceStrength, new (doodleData.doodleReferenceType, doodleData.strength));
        }

        void UsePixelate()
        {
            if (m_GenerationMetadata?.refinementMode == RefinementMode.Pixelate.ToString())
            {
                var pixelateSettings = GetPixelateSettingsFromGenerationMetadata(m_GenerationMetadata);
                this.Dispatch(GenerationSettingsActions.setPixelateSettings, pixelateSettings);
                UseRefinementMode();
            }
        }

        PixelateSettings GetPixelateSettingsFromGenerationMetadata(GenerationMetadata generationMetadata)
        {
            var pixelateSettings = new PixelateSettings()
            {
                targetSize = generationMetadata.pixelateTargetSize,
                keepImageSize = generationMetadata.pixelateKeepImageSize,
                pixelBlockSize = generationMetadata.pixelatePixelBlockSize,
                mode = generationMetadata.pixelateMode,
                outlineThickness = generationMetadata.pixelateOutlineThickness
            };
            return pixelateSettings;
        }

        void UseAll()
        {
            UseRefinementMode();
            UseModel();
            UsePrompt();
            UseNegativePrompt();
            UseCustomSeed();
            UsePixelate();
            foreach (var doodleData in m_GenerationMetadata.doodles)
            {
                UseDoodle(doodleData);
            }
        }

        void OnDismiss()
        {
            OnDismissRequested?.Invoke();
        }
    }
}
