using System;
using Unity.AI.Sound.Services.Stores.Actions.Creators;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Toolkit.Asset;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Sound.Services.Utilities
{
    interface IInputReference {}

    static class InputReferenceExtensions
    {
        public static void Bind<T>(this T element,
            AssetActionCreator<AssetReference> setInputReferenceAsset,
            Func<IState, VisualElement, AssetReference> selectInputReferenceAsset) where T: VisualElement, IInputReference
        {
            var objectField = element.Q<ObjectField>();

            objectField.AddManipulator(new ScaleToFitObjectFieldImage());
            objectField.RegisterValueChangedCallback(evt =>
                element.Dispatch(setInputReferenceAsset, Unity.AI.Generators.Asset.AssetReferenceExtensions.FromObject(evt.newValue as AudioClip)));

            element.Use(state => selectInputReferenceAsset(state, element), asset => objectField.value = asset.GetObject());
        }

        public static void BindWithStrength<T, U>(this T element,
            AssetActionCreator<float> setInputReferenceStrength,
            AssetActionCreator<U> setInputReference,
            Func<IState, VisualElement, float> selectInputReferenceStrength) where T: VisualElement, IInputReference where U : new()
        {
            var strengthSlider = element.Q<SliderInt>(classes: "input-reference-strength-slider");

            strengthSlider.RegisterValueChangedCallback(evt =>
                element.Dispatch(setInputReferenceStrength, evt.newValue / 100.0f));

            element.Use(state => selectInputReferenceStrength(state, element), strength => {
                strengthSlider.SetValueWithoutNotify(Mathf.RoundToInt(strength * 100));
            });

            var deleteInputReference = element.Q<Button>("delete-input-reference");
            deleteInputReference.clicked += () => {
                element.Dispatch(setInputReference, new U());
            };
        }
    }
}
