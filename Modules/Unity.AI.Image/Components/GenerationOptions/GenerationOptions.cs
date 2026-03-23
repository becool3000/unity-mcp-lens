using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class GenerationOptions : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/GenerationOptions/GenerationOptions.uxml";

        readonly Toggle m_RemoveBackground;
        readonly Toggle m_UseCustomSeed;
        readonly IntegerField m_CustomSeed;

        public GenerationOptions()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            AddToClassList("generation-options");

            m_UseCustomSeed = this.Q<Toggle>(className:"generation-options-use-custom-seed");
            m_CustomSeed = this.Q<IntegerField>(className:"generation-options-custom-seed");

            m_UseCustomSeed.RegisterValueChangedCallback(evt =>
                this.Dispatch(GenerationSettingsActions.setUseCustomSeed, evt.newValue));
            m_CustomSeed.RegisterValueChangedCallback(evt =>
                this.Dispatch(GenerationSettingsActions.setCustomSeed, evt.newValue));

            this.Use(state => state.SelectGenerationOptions(this), OnGenerationOptionsChanged);
        }

        void OnGenerationOptionsChanged((bool useCustomSeed, int customSeed) arg)
        {
            var (useCustomSeed, customSeed) = arg;
            m_UseCustomSeed.value = useCustomSeed;
            m_CustomSeed.value = customSeed;
            m_CustomSeed.EnableInClassList("flex", useCustomSeed);
            m_CustomSeed.EnableInClassList("hide", !useCustomSeed);
        }
    }
}
