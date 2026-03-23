using System;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Sound.Services.Stores.Actions;
using Unity.AI.Sound.Services.Stores.Selectors;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Sound.Components
{
    [UxmlElement]
    partial class GenerationOptions : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Sound/Components/GenerationOptions/GenerationOptions.uxml";

        readonly Toggle m_UseCustomSeed;
        readonly IntegerField m_CustomSeed;
        readonly Toggle m_Loop;

        public GenerationOptions()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            AddToClassList("generation-options");

            m_UseCustomSeed = this.Q<Toggle>(className:"generation-options-use-custom-seed");
            m_CustomSeed = this.Q<IntegerField>(className:"generation-options-custom-seed");
            m_Loop = this.Q<Toggle>(className: "generation-options-loop");

            m_UseCustomSeed.RegisterValueChangedCallback(evt =>
                this.Dispatch(GenerationSettingsActions.setUseCustomSeed, evt.newValue));
            m_CustomSeed.RegisterValueChangedCallback(evt =>
                this.Dispatch(GenerationSettingsActions.setCustomSeed, evt.newValue));
            m_Loop.RegisterValueChangedCallback(evt =>
                this.Dispatch(GenerationSettingsActions.setLoop, evt.newValue));

            this.Use(state => state.SelectGenerationOptions(this), OnGenerationOptionsChanged);
            this.Use(state => state.SelectLoopOptions(this), OnLoopOptionsChanged);
        }

        void OnGenerationOptionsChanged((bool useCustomSeed, int customSeed) arg)
        {
            var (useCustomSeed, customSeed) = arg;
            m_UseCustomSeed.value = useCustomSeed;
            m_CustomSeed.value = customSeed;
            m_CustomSeed.EnableInClassList("flex", useCustomSeed);
            m_CustomSeed.EnableInClassList("hide", !useCustomSeed);
        }

        void OnLoopOptionsChanged((bool loop, bool supportsLooping) arg)
        {
            var (loop, supportsLooping) = arg;
            m_Loop.value = loop;
            m_Loop.SetShown(supportsLooping);
        }
    }
}
