using System;
using System.Collections.Generic;
using Unity.AI.Animate.Services.Stores.Actions;
using Unity.AI.Animate.Services.Stores.Selectors;
using Unity.AI.Animate.Services.Utilities;
using Unity.AI.ModelSelector.Services.Stores.States;
using Unity.AI.ModelSelector.Services.Utilities;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Animate.Windows.GenerationMetadataWindow
{
    class GenerationMetadataContent : VisualElement
    {
        public event Action OnDismissRequested;

        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Animate/Windows/GenerationMetadataWindow/GenerationMetadataWindow.uxml";

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

            InitModel();
            InitPrompt();
            InitNegativePrompt();
            InitCustomSeed();

            m_DismissButtons = this.Query<Button>(className: "data-button").ToList();
            foreach (var button in m_DismissButtons)
            {
                button.clicked += OnDismiss;
            }
        }

        void InitModel()
        {
            m_ModelSettings = m_Store.State.SelectModelSettings(m_GenerationMetadata);
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

        void UseModel()
        {
            if (m_ModelSettings.IsValid())
                this.Dispatch(GenerationSettingsActions.setSelectedModelID, (this.GetState().SelectRefinementMode(this), m_ModelSettings.id));
        }

        void UsePrompt()
        {
            var truncatedPrompt = PromptUtilities.TruncatePrompt(m_GenerationMetadata?.prompt);
            this.Dispatch(GenerationSettingsActions.setPrompt, truncatedPrompt);
        }

        void UseNegativePrompt()
        {
            var truncatedPrompt = PromptUtilities.TruncatePrompt(m_GenerationMetadata?.negativePrompt);
            this.Dispatch(GenerationSettingsActions.setNegativePrompt, truncatedPrompt);
        }

        void UseCustomSeed()
        {
            if (m_GenerationMetadata.customSeed != -1)
            {
                this.Dispatch(GenerationSettingsActions.setUseCustomSeed, true);
                this.Dispatch(GenerationSettingsActions.setCustomSeed, m_GenerationMetadata.customSeed);
            }
        }

        void UseAll()
        {
            UseModel();
            UsePrompt();
            UseNegativePrompt();
            UseCustomSeed();
        }

        void OnDismiss()
        {
            OnDismissRequested?.Invoke();
        }
    }
}
