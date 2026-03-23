using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Image.Services.Utilities;
using Unity.AI.ModelSelector.Services.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class Prompt : VisualElement
    {
        const string k_DoesntSupportNegativePrompt = "gpt image";

        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/Prompt/Prompt.uxml";

        public Prompt()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            var promptText = this.Q<TextField>("prompt");
            var negativePromptGroup = this.Q<VisualElement>("negative-prompt-group");
            var negativePromptText = this.Q<TextField>("negative-prompt");
            var promptLimitIndicator = this.Q<Label>("prompt-limit-indicator");
            var negativePromptLimitIndicator = this.Q<Label>("negative-prompt-limit-indicator");

            promptText.maxLength = PromptUtilities.maxPromptLength;
            negativePromptText.maxLength = PromptUtilities.maxPromptLength;

            promptText.RegisterValueChangedCallback(evt => this.Dispatch(GenerationSettingsActions.setPrompt, (this.GetState().SelectRefinementMode(this), PromptUtilities.TruncatePrompt(evt.newValue))));
            negativePromptText.RegisterValueChangedCallback(evt => this.Dispatch(GenerationSettingsActions.setNegativePrompt, (this.GetState().SelectRefinementMode(this), PromptUtilities.TruncatePrompt(evt.newValue))));

            promptText.RegisterTabEvent();
            negativePromptText.RegisterTabEvent();

            this.Use(state => state.SelectPrompt(this), prompt =>
            {
                promptText.value = prompt;
                promptLimitIndicator.text = $"{prompt.Length}/{PromptUtilities.maxPromptLength}";
            });
            this.Use(state => state.SelectNegativePrompt(this), negativePrompt =>
            {
                negativePromptText.value = negativePrompt;
                negativePromptLimitIndicator.text = $"{negativePrompt.Length}/{PromptUtilities.maxPromptLength}";
            });
            this.Use(state => state.SelectSelectedModel(this),
                model => negativePromptGroup.SetShown(!model.IsValid() || !model.name.ToLower().StartsWith(k_DoesntSupportNegativePrompt)));

            this.UseAsset(asset =>
            {
                var promptPlaceholder = Selectors.SelectPromptPlaceholderText(asset, this.GetState());
                var negativePromptPlaceholder = Selectors.SelectNegativePromptPlaceholderText(asset);
                promptText.textEdition.placeholder = promptPlaceholder;
                negativePromptText.textEdition.placeholder = negativePromptPlaceholder;
            });

            this.Use(state => state.SelectRefinementMode(this), _ =>
            {
                if (!this.GetAsset().IsValid())
                    return;
                var promptPlaceholder = Selectors.SelectPromptPlaceholderText(this.GetAsset(), this.GetState());
                var negativePromptPlaceholder = Selectors.SelectNegativePromptPlaceholderText(this.GetAsset());
                promptText.textEdition.placeholder = promptPlaceholder;
                negativePromptText.textEdition.placeholder = negativePromptPlaceholder;
            });
        }
    }
}
