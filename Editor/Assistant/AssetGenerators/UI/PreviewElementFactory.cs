#define AI_GENERATORS_SHOW_DISCLAIMER
using System;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using Unity.AI.Toolkit;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.UI.Editor
{
    static class PreviewElementFactory
    {
        public static VisualElement Create(Action<VisualElement, Object> setContextAction, Object assetObject, string uxmlAssetPath)
        {
            var thisElement = new VisualElement();
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlAssetPath);
            if (tree == null)
            {
                Debug.LogError($"Could not load UXML for preview at path: {uxmlAssetPath}");
                return thisElement; // Return an empty element to prevent a crash
            }
            tree.CloneTree(thisElement);

            thisElement.RegisterCallback<ClickEvent>(_ =>
            {
                // Defer ping after import to avoid conflicts with rapid asset switching
                EditorTask.delayCall += () =>
                {
                    if (assetObject != null)
                        EditorGUIUtility.PingObject(assetObject);
                };
            });

#if AI_GENERATORS_SHOW_DISCLAIMER
            var bottomBar = thisElement.Query<VisualElement>(className: "generations-disclaimer-and-slider");
            bottomBar.ForEach(ve => ve.style.paddingBottom = 2);
#else
            var bottomBar = thisElement.Query<VisualElement>(className: "generations-disclaimer-and-slider");
            bottomBar.ForEach(ve => ve.style.display = DisplayStyle.None);
#endif
            var openGeneratorsButtons = thisElement.Query<Button>("open-generator-window").ToList();
            foreach(var button in openGeneratorsButtons)
            {
                SetupOpenGeneratorButton(button);
            }

            setContextAction?.Invoke(thisElement, assetObject);
            return thisElement;
        }

        static void SetupOpenGeneratorButton(Button button)
        {
            if(button == null)
                return;

            button.RemoveFromClassList("hide");
            button.AddToClassList("mui-action-button");
            button.Q<Label>()?.AddToClassList("mui-action-button-label");

            var buttonImage = button.Q<Image>();
            if (buttonImage != null)
            {
                button.SetupImage("open-generator-window-image", "arrow-square-in");
                buttonImage.AddToClassList("mui-action-button-image-large");
            }
        }
    }
}
