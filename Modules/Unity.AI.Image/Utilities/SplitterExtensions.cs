using System;
using System.Collections.Generic;
using Unity.AI.Image.Services.Stores.Actions.Creators;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UIElements.Core;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Services.Utilities
{
    static class SplitterExtensions
    {
        public static void Bind(this Splitter splitter,
            VisualElement generatorUI,
            AssetActionCreator<float> setHistoryDrawerHeight,
            Func<IState, VisualElement, float> selectHistoryDrawerHeight,
            Func<IState, VisualElement, IEnumerable<string>> selectActiveReferences)
        {
            var topPane = generatorUI.Q<VisualElement>("top-pane");
            var bottomPane = generatorUI.Q<VisualElement>("bottom-pane");
            var paneContainer = generatorUI.Q<VisualElement>("pane-container");
            Unsubscribe referencesSubscription = null;

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

            generatorUI.UseStore(store =>
            {
                referencesSubscription?.Invoke();
                if (store != null)
                {
                    var references = selectActiveReferences(generatorUI.GetState(), generatorUI);
                    referencesSubscription = generatorUI.UseArray(state => selectActiveReferences(state, generatorUI), _ =>
                    {
                        splitter.Reset();
                    }, new UseSelectorOptions<IEnumerable<string>>
                    {
                        selectImmediately = false,
                        waitForValue = true,
                        initialValue = references
                    });
                }
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
