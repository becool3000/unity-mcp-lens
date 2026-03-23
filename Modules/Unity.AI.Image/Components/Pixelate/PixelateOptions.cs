using System;
using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class PixelateOptions : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/Pixelate/PixelateOptions.uxml";

        public PixelateOptions()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            _ = new ContextualMenuManipulator(OpenContextMenu) { target = this };
        }

        void OpenContextMenu(ContextualMenuPopulateEvent evt)
        {
            evt.menu.AppendAction("Paste", Paste);
        }

        void Paste(DropdownMenuAction menuItem)
        {
            var pixelateSettingsString = EditorGUIUtility.systemCopyBuffer;

            try
            {
                var pixelateSettings = JsonUtility.FromJson<PixelateSettings>(pixelateSettingsString);

                if (pixelateSettings != null)
                {
                    this.Dispatch(GenerationSettingsActions.setPixelateSettings, pixelateSettings);
                }
            }
            catch (Exception)
            {
                //Do nothing
            }
        }
    }
}
