using System;
using Unity.AI.Animate.Services.Stores.Actions.Creators;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace Unity.AI.Animate.Services.Utilities
{
    interface IInputReference {}

    static class InputReferenceExtensions
    {
        public static void Bind<T>(this T element,
            AssetActionCreator<AssetReference> setInputReferenceAsset,
            Func<IState, VisualElement, AssetReference> selectInputReferenceAsset) where T: VisualElement, IInputReference
        {
            var objectField = element.Q<ObjectField>();
            objectField.RegisterValueChangedCallback(evt =>
                element.Dispatch(setInputReferenceAsset, Unity.AI.Generators.Asset.AssetReferenceExtensions.FromObject(evt.newValue as VideoClip)));

            var settingsButton = element.Q<Button>("input-reference-settings-button");
            settingsButton.clicked += () => ShowMenu();
            objectField.RegisterCallback<ContextClickEvent>(_ => ShowMenu(true));

            element.Use(state => selectInputReferenceAsset(state, element), asset => objectField.value = asset.GetObject());

            return;

            void ShowMenu(bool isContextClick = false)
            {
                var menu = new GenericMenu();
                if (objectField.value)
                    menu.AddItem(new GUIContent("Clear"), false, Clear);
                else
                    menu.AddDisabledItem(new GUIContent("Clear"));

                if (isContextClick)
                    menu.ShowAsContext();
                else
                    menu.DropDown(settingsButton.worldBound);
            }

            void Clear()
            {
                objectField.value = null;
            }
        }
    }
}
