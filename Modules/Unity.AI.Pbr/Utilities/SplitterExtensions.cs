using System;
using Unity.AI.Pbr.Services.Stores.Actions.Creators;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Pbr.Services.Utilities
{
    static class SplitterExtensions
    {
        public static void Bind(this Splitter splitter,
            VisualElement generatorUI,
            AssetActionCreator<float> setHistoryDrawerHeight,
            Func<IState, VisualElement, float> selectHistoryDrawerHeight)
        {
            var topPane = generatorUI.Q<VisualElement>("top-pane");
            var bottomPane = generatorUI.Q<VisualElement>("bottom-pane");
            var paneContainer = generatorUI.Q<VisualElement>("pane-container");

            splitter.firstPane = topPane;
            splitter.secondPane = bottomPane;
            splitter.container = paneContainer;
            splitter.RegisterValueChangedCallback(evt =>
                generatorUI.Dispatch(setHistoryDrawerHeight, evt.newValue));
            paneContainer.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                var height = selectHistoryDrawerHeight(generatorUI.GetState(), generatorUI);
                splitter.SetValueWithoutNotify(height);
            });
        }

        public static void BindHorizontal(this Splitter splitter, VisualElement generatorUI,
            AssetActionCreator<float> setGenerationPaneWidth,
            Func<IState, VisualElement, float> selectGenerationPaneWidth)
        {
            var paneContainer = generatorUI.Q<VisualElement>("pane-container");
            var parent = paneContainer.parent;
            var rightSection = parent.Q<VisualElement>("right-section");

            splitter.isFirstPaneFixed = true;
            splitter.vertical = false;
            splitter.firstPane = paneContainer;
            splitter.secondPane = rightSection;
            splitter.container = parent;

            splitter.RegisterValueChangedCallback(evt =>
                generatorUI.Dispatch(setGenerationPaneWidth, evt.newValue));

            rightSection.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                var width = selectGenerationPaneWidth(generatorUI.GetState(), generatorUI);
                splitter.SetValueWithoutNotify(width);
            });
        }
    }
}
