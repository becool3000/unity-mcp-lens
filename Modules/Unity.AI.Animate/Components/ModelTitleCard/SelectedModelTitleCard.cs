using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Animate.Services.Stores.Actions;
using Unity.AI.Animate.Services.Stores.Selectors;
using Unity.AI.ModelSelector.Services.Stores.Selectors;
using Unity.AI.ModelSelector.Services.Stores.States;
using Unity.AI.ModelSelector.Services.Utilities;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Animate.Components
{
    [UxmlElement]
    partial class SelectedModelTitleCard : VisualElement, IModelTitleCard
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Animate/Components/ModelTitleCard/SelectedModelTitleCard.uxml";

        string m_ModelID = "";

        public SelectedModelTitleCard()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            this.AddManipulator(new ContextualMenuManipulator(OpenContextMenu));

            this.Use(state => state.SelectSelectedModelID(this), OnSelectedModelIDChanged);
            this.UseArray(state => state.SelectModelSettings(), OnModelSettingsChanged);
        }

        static readonly ModelSettings k_InvalidModel = new()
        {
            name = "No model selected",
            id = "invalid-model",
            description = "Please select a model",
            tags = new List<string> { "Please select a model" },
        };

        void OnModelSettingsChanged(IEnumerable<ModelSettings> modelSettings)
        {
            var selectedModel = modelSettings.FirstOrDefault(m => m.id == m_ModelID);
            this.SetModel(selectedModel.IsValid() ? selectedModel : k_InvalidModel);
        }

        void OnSelectedModelIDChanged(string selectedModelID)
        {
            m_ModelID = selectedModelID;
            var models = this.GetState().SelectModelSettings();
            var selectedModel = models.FirstOrDefault(m => m.id == m_ModelID);
            this.SetModel(selectedModel.IsValid() ? selectedModel : k_InvalidModel);
        }

        void OpenContextMenu(ContextualMenuPopulateEvent evt)
        {
            evt.menu.AppendAction("Paste", Paste);
        }

        void Paste(DropdownMenuAction menuItem)
        {
            var modelIdBuffer = EditorGUIUtility.systemCopyBuffer;
            var models = this.GetState().SelectModelSettings();
            var pastedModel = models.FirstOrDefault(m => m.id == modelIdBuffer);

            if (pastedModel.IsValid())
                this.Dispatch(GenerationSettingsActions.setSelectedModelID, (this.GetState().SelectRefinementMode(this), modelIdBuffer));
        }
    }
}
