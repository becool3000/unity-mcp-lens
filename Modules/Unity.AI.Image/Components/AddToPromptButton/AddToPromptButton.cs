using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.Actions.Payloads;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Image.Utilities;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Image.Services.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class AddToPromptButton : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/AddToPromptButton/AddToPromptButton.uxml";

        readonly Button m_AddToPrompt;

        readonly Dictionary<ImageReferenceType, bool> m_TypesValidationResults = new();

        public AddToPromptButton()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            m_AddToPrompt = this.Q<Button>("add-to-prompt-button");
            m_AddToPrompt.SetEnabled(false); // Start disabled

            m_AddToPrompt.clickable = new Clickable(() => {
                var asset = this.GetAsset();
                if (asset == null)
                    return;
                this.GetStoreApi().Dispatch(GenerationSettingsActions.openAddToPromptWindow, new AddToPromptWindowArgs(asset, this, m_TypesValidationResults));
            });

            this.UseAsset(_ => OnModelChanged());
            this.Use(state => state.SelectSelectedModel(this)?.id, _ => OnModelChanged());
            this.UseArray(state => state.SelectActiveReferencesTypes(this), _ => OnModelChanged());

            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
        }

        void OnAttachToPanel(AttachToPanelEvent _) => OnModelChanged();

        void OnModelChanged()
        {
            if (!this.GetAsset().IsValid())
            {
                m_AddToPrompt.SetEnabled(false);
                m_AddToPrompt.tooltip = "No asset to validate.";
                return;
            }

            var typesToValidate = GetTypesToValidate(this.GetState().SelectRefinementMode(this));
            if (typesToValidate.Length == 0)
            {
                // No types to validate - just rebuild with empty results
                Rebuild(typesToValidate, Array.Empty<bool>());
                return;
            }

            // Use synchronous validation
            var results = new bool[typesToValidate.Length];
            var payload = new AddImageReferenceTypeData(this.GetAsset(), typesToValidate);

            for (var i = 0; i < typesToValidate.Length; i++)
            {
                results[i] = this.GetState().SelectCanAddReferencesToPrompt(payload, typesToValidate[i]);
            }

            Rebuild(typesToValidate, results);
        }

        void Rebuild(ImageReferenceType[] typesToValidate, bool[] results)
        {
            var hasItems = false;

            if (results != null && typesToValidate.Length == results.Length) // Ensure results are valid
            {
                m_TypesValidationResults.Clear();
                for (var i = 0; i < typesToValidate.Length; i++)
                {
                    if (m_TypesValidationResults.TryAdd(typesToValidate[i], results[i]))
                    {
                        hasItems = hasItems || results[i];
                    }
                }
            }

            m_AddToPrompt.SetEnabled(hasItems);
            m_AddToPrompt.SetShown(hasItems);
            m_AddToPrompt.tooltip = !hasItems ? "No controls available to add for this model." : "Add additional controls to guide generation using images as references.";
        }

        static ImageReferenceType[] GetTypesToValidate(RefinementMode mode)
        {
            var typesToValidate = new List<ImageReferenceType>();
            foreach (var type in Enum.GetValues(typeof(ImageReferenceType)).Cast<ImageReferenceType>().OrderBy(t => t.GetDisplayOrder()))
            {
                if (type.GetRefinementModeForType().Contains(mode))
                    typesToValidate.Add(type);
            }
            return typesToValidate.ToArray();
        }
    }
}
