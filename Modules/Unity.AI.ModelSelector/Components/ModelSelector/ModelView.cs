using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.ModelSelector.Services.Stores.Actions;
using Unity.AI.ModelSelector.Services.Stores.Selectors;
using Unity.AI.ModelSelector.Services.Stores.States;
using Unity.AI.ModelSelector.Services.Utilities;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.ModelSelector.Services.Stores.Actions.Payloads;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.ModelSelector.Components
{
    /// <summary>
    /// Model view for selecting the AI model
    /// </summary>
    class ModelView : VisualElement
    {
        public event Action onDismissRequested;

        string m_SelectedModelId;
        TabView m_TabView;
        VisualElement m_TagsElement;
        VisualElement m_ModelsElement;
        VisualElement m_ModelsContainer;
        VisualElement m_BaseModelsElement;
        VisualElement m_BaseModelsContainer;
        VisualElement m_TagsContainer;
        VisualElement m_ProvidersElement;
        VisualElement m_ProvidersContainer;
        VisualElement m_ModalitiesElement;
        VisualElement m_ModalitiesContainer;
        RadioButtonGroup m_SortOrder;
        VisualElement m_OperationsElement;
        VisualElement m_OperationsContainer;
        ToolbarSearchField m_SearchBar;
        GridView m_GridView;
        VisualElement m_SearchResultsText;
        List<ModelSettings> m_Models = new();
        List<ModelSettings> m_FilteredModels = new();
        IVisualElementScheduledItem m_DelayedSearch;
        DetailsModelTitleCard m_ModelDetailsCard;
        GridView m_ModelDetailsGrid;

        // Reusable collections to avoid allocations in hot paths
        readonly HashSet<string> m_TempStringHashSet = new();
        readonly HashSet<MiscModelType> m_TempMiscHashSet = new();
        readonly List<string> m_TempStringList = new();
        readonly List<MiscModelType> m_TempMiscList = new();

        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.ModelSelector/Components/ModelSelector/ModelView.uxml";

        public List<ModelSettings> models
        {
            get => m_Models;
            set
            {
                m_Models = value?.Presort().ThenBy(model => model.name).ToList() ?? new List<ModelSettings>();
                m_FilteredModels = models;
                RebuildFilters();
                OnSearch();
            }
        }

        readonly List<ModelTile> m_TilePool = new();

        public ModelView()
        {
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            InitUI();

            this.UseArray(ModelSelectorSelectors.SelectModelSettings, OnModelsChanged);
            this.UseArray(ModelSelectorSelectors.SelectSelectedModalities, OnSelectedModalitiesSelected);
            this.UseArray(ModelSelectorSelectors.SelectSelectedOperations, OnSelectedOperationsChanged);
            this.UseArray(ModelSelectorSelectors.SelectSelectedTags, OnSelectedTagsChanged);
            this.UseArray(ModelSelectorSelectors.SelectSelectedBaseModelIds, OnSelectedBaseModelIdsChanged);
            this.UseArray(ModelSelectorSelectors.SelectSelectedMiscModels, OnSelectedMiscChanged);
            this.UseArray(ModelSelectorSelectors.SelectSelectedProviders, OnSelectedProvidersChanged);
            this.Use(ModelSelectorSelectors.SelectSelectedModelID, OnModelSelected);
            this.Use(ModelSelectorSelectors.SelectSortMode, OnSortModeChanged);
            this.Use(ModelSelectorSelectors.SelectSearchQuery, OnSearchQueryChanged);
            this.UseStore(OnStoreReady);
        }

        void OnStoreReady(Store store)
        {
            if (store == null)
                return;

            OnSearch();
        }

        void OnSelectedModalitiesSelected(IEnumerable<string> selectedModalities)
        {
            RefreshModalityElements(selectedModalities);
            OnSearch();
        }

        void OnSelectedOperationsChanged(IEnumerable<string> selectedOperations)
        {
            RefreshOperationElements(selectedOperations);
            OnSearch();
        }

        void OnSelectedTagsChanged(IEnumerable<string> selectedTags)
        {
            RefreshTagElements(selectedTags);
            OnSearch();
        }

        void OnSelectedBaseModelIdsChanged(IEnumerable<string> baseModelIds)
        {
            RefreshBaseModelElements(baseModelIds);
            OnSearch();
        }

        void OnSelectedMiscChanged(IEnumerable<MiscModelType> selectedMiscModels)
        {
            RefreshMiscModelElements(selectedMiscModels);
            OnSearch();
        }

        void OnSelectedProvidersChanged(IEnumerable<string> selectedProviders)
        {
            RefreshProviderElements(selectedProviders);
            OnSearch();
        }

        void OnSortModeChanged(SortMode sortMode)
        {
            m_SortOrder.SetValueWithoutNotify((int)sortMode);
            OnSearch();
        }

        void OnSearchQueryChanged(string searchQuery)
        {
            if (m_SearchBar.value != searchQuery)
                m_SearchBar.SetValueWithoutNotify(searchQuery);

            DelayOnSearch();
        }

        void RebuildFilters()
        {
            ShowModalities();
            ShowProviders();
            ShowOperationTypes();
            ShowTags();
            ShowMisc();
            ShowBaseModels();
        }

        bool IsModelBroken(ModelSettings model) => false;

        void OnModelSelected(string modelID)
        {
            m_SelectedModelId = modelID;
            var tiles = this.Query<ModelTile>();
            tiles.ForEach(tile => tile.OnModelSelected(m_SelectedModelId));
        }

        void OnModelsChanged(List<ModelSettings> data) => models = data;

        void OnCancelButtonPressed() => onDismissRequested?.Invoke();

        void InitUI()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            EnableInClassList("light-theme", !EditorGUIUtility.isProSkin);
            EnableInClassList("dark-theme", EditorGUIUtility.isProSkin);

            m_TabView = this.Q<TabView>();
            m_TagsElement = this.Q<VisualElement>(className: "tags");
            m_TagsContainer = this.Q<VisualElement>(className: "tags-container");
            m_ModelsElement = this.Q<VisualElement>(className: "misc");
            m_ModelsContainer = this.Q<VisualElement>(className: "misc-container");
            m_BaseModelsElement = this.Q<VisualElement>(className: "base-models");
            m_BaseModelsContainer = this.Q<VisualElement>(className: "base-models-container");
            m_ProvidersElement = this.Q<VisualElement>(className: "sources");
            m_ProvidersContainer = this.Q<VisualElement>(className: "sources-container");
            m_ModalitiesElement = this.Q<VisualElement>(className: "modalities");
            m_ModalitiesContainer = this.Q<VisualElement>(className: "modalities-container");
            m_SortOrder = this.Q<RadioButtonGroup>(className: "sort-radio-group");
            m_SortOrder.RegisterValueChangedCallback(OnSortValueChanged);
            m_OperationsElement = this.Q<VisualElement>(className: "operations");
            m_OperationsContainer = this.Q<VisualElement>(className: "operations-container");
            m_SearchBar = this.Q<ToolbarSearchField>(className: "searchbar-text-field");
            m_SearchBar.RegisterValueChangedCallback(OnSearchFieldChanging);
            m_SearchResultsText = this.Q<Label>(className: "no-search-results-text");

            m_GridView = this.Q<GridView>(className: "models-section-grid");
            m_GridView.keepScrollPositionWhenHidden = true;
            m_GridView.selectedIndicesChanged += indices =>
            {
                using var enumerator = indices.GetEnumerator();
                if (!enumerator.MoveNext()) return;

                var index = enumerator.Current;
                var model = m_GridView.itemsSource[index] as ModelSettings;

                SelectModel(model);
            };

            var closeButton = this.Q<Button>(className: "close-button");
            closeButton.clickable = new Clickable(OnCancelButtonPressed);

            var backButton = this.Q<Button>(className: "back-button");
            backButton.clickable = new Clickable(OnBackButtonPressed);

            m_ModelDetailsCard = this.Q<DetailsModelTitleCard>();
            m_ModelDetailsGrid = this.Q<GridView>(className: "model-details-grid");
        }

        void OnAttachToPanel(AttachToPanelEvent _)
        {
            if (this.GetStoreApi() != null)
                this.Dispatch(ModelSelectorActions.discoverModels, new DiscoverModelsData(this.GetStoreApi().State.SelectEnvironment()));
            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        }

        void OnDetachFromPanel(DetachFromPanelEvent _) => UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

        void OnBackButtonPressed() => m_TabView.selectedTabIndex = 0;

        void ShowModelDetails(ModelSettings model)
        {
            m_ModelDetailsCard.SetModel(model);

            var thumbnails = model.thumbnails.Skip(1).ToList();
            m_ModelDetailsGrid.BindToModelDetails(thumbnails);

            var selectButton = m_ModelDetailsCard.Q<Button>(className: "model-title-card-select");
            selectButton.clickable = new Clickable(() => SelectModel(model));

            m_TabView.selectedTabIndex = 1;
        }

        void ShowModalities()
        {
            m_ModalitiesElement.Query<Toggle>().ForEach(t => t.UnregisterValueChangedCallback(OnFilterSelectionChanged));
            m_ModalitiesElement.Clear();
            var modalities = ModelConstants.Modalities.EnumerateAll();
            foreach (string modality in modalities)
            {
                var modalityElement = new Toggle
                {
                    label = modality.GetModalityName(),
                    name = modality,
                    userData = modality,
                    enabledSelf = false
                };
                modalityElement.AddToClassList("tag");
                modalityElement.RegisterValueChangedCallback(OnFilterSelectionChanged);
                m_ModalitiesElement.Add(modalityElement);
            }

            m_ModalitiesContainer.SetShown(Unsupported.IsDeveloperMode());

            RefreshModalityElements(this.GetState().SelectSelectedModalities());
        }

        void RefreshModalityElements(IEnumerable<string> selectedModalities)
        {
            m_TempStringHashSet.Clear();
            foreach (var modality in selectedModalities)
                m_TempStringHashSet.Add(modality);

            // Update the visibility of the modality toggles based on the selected modalities
            m_ModalitiesElement.Query<Toggle>().ForEach(toggle =>
            {
                var selected = m_TempStringHashSet.Contains((string) toggle.userData);
                toggle.SetValueWithoutNotify(selected);
                toggle.SetShown(selected);
            });
        }

        void ShowProviders()
        {
            // Display only sources from selected modalities
            var selectedModalities = m_ModalitiesElement.Query<Toggle>()
                .Where(t => t.value).Build().Select(t => t.name).ToList();

            // Operations should not change in the model selection window, if it happens, we will have to rebuild the providers list
            var selectedOperations = this.GetState().SelectSelectedOperations();

            var providers = m_Models.Where(m => selectedModalities.Contains(m.modality.ToString(), StringComparer.InvariantCultureIgnoreCase) &&
                (!selectedOperations.Any() || m.operations.Any(op => selectedOperations.Contains(op, StringComparer.InvariantCultureIgnoreCase))))
                .Select(model => model.provider).Distinct().OrderBy(s => s).ToList();

            m_ProvidersElement.Query<Toggle>().ForEach(t => t.UnregisterValueChangedCallback(OnFilterSelectionChanged));
            m_ProvidersElement.Clear();
            foreach (var provider in providers)
            {
                var providerElement = new Toggle
                {
                    label = provider.ToString(),
                    name = provider.ToString(),
                    userData = provider
                };
                providerElement.AddToClassList("tag");
                providerElement.RegisterValueChangedCallback(OnFilterSelectionChanged);
                m_ProvidersElement.Add(providerElement);
            }

            m_ProvidersContainer.SetShown(Unsupported.IsDeveloperMode());

            RefreshProviderElements(this.GetState().SelectSelectedProviders());
        }

        void RefreshProviderElements(IEnumerable<string> selectedProviders)
        {
            m_TempStringHashSet.Clear();
            foreach (var provider in selectedProviders)
                m_TempStringHashSet.Add(provider);

            // Update the visibility of the provider toggles based on the selected providers
            m_ProvidersElement.Query<Toggle>().ForEach(toggle =>
            {
                toggle.SetValueWithoutNotify(m_TempStringHashSet.Contains((string)toggle.userData));
            });
        }

        void ShowTags()
        {
            // Display only tags from selected modalities
            var selectedModalities = m_ModalitiesElement.Query<Toggle>().ToList()
                .Where(t => t.value).Select(t => t.name).ToList();

            var tags = m_Models.Where(m => selectedModalities.Contains(m.modality.ToString(), StringComparer.InvariantCultureIgnoreCase))
                .SelectMany(model => model.tags).Distinct().OrderBy(s => s).ToList();

            m_TagsElement.Query<Toggle>().ForEach(t => t.UnregisterValueChangedCallback(OnFilterSelectionChanged));
            m_TagsElement.Clear();
            foreach (var tag in tags)
            {
                var tagElement = new Toggle { name = tag, label = tag };
                tagElement.AddToClassList("tag");
                tagElement.RegisterValueChangedCallback(OnFilterSelectionChanged);
                m_TagsElement.Add(tagElement);
            }

            m_TagsContainer.SetShown(tags.Any());

            RefreshTagElements(this.GetState().SelectSelectedTags());
        }

        void RefreshTagElements(IEnumerable<string> selectedTags)
        {
            m_TempStringHashSet.Clear();
            foreach (var tag in selectedTags)
                m_TempStringHashSet.Add(tag);

            // Update the visibility of the tag toggles based on the selected tags
            m_TagsElement.Query<Toggle>().ForEach(toggle =>
            {
                toggle.SetValueWithoutNotify(m_TempStringHashSet.Contains(toggle.name));
            });
        }

        void ShowMisc()
        {
            m_ModelsElement.Query<Toggle>().ForEach(t => t.UnregisterValueChangedCallback(OnFilterSelectionChanged));
            m_ModelsElement.Clear();

            var miscModels = MiscModelTypeExtensions.EnumerateAll();
            foreach (var misc in miscModels)
            {
                var miscElement = new Toggle
                {
                    name = misc.ToString(),
                    label = misc.ToString().AddSpaceBeforeCapitalLetters(),
                    userData = misc
                };
                miscElement.RegisterValueChangedCallback(OnFilterSelectionChanged);
                m_ModelsElement.Add(miscElement);
            }

            RefreshMiscModelElements(this.GetState().SelectSelectedMiscModels());
        }

        void RefreshMiscModelElements(IEnumerable<MiscModelType> selectedMiscModels)
        {
            m_TempMiscHashSet.Clear();
            foreach (var misc in selectedMiscModels)
                m_TempMiscHashSet.Add(misc);

            // Update the visibility of the misc toggles based on the selected misc models
            m_ModelsElement.Query<Toggle>().ForEach(toggle =>
            {
                toggle.SetValueWithoutNotify(m_TempMiscHashSet.Contains((MiscModelType)toggle.userData));
            });
        }

        void ShowBaseModels()
        {
            var selectedModalities = m_ModalitiesElement.Query<Toggle>()
                .Where(t => t.value).Build().Select(t => t.name).ToList();

            m_BaseModelsElement.Query<Toggle>().ForEach(t => t.UnregisterValueChangedCallback(OnFilterSelectionChanged));
            m_BaseModelsElement.Clear();

            var availableBaseModels = new HashSet<string>();

            // Base Models work only for Image Modality (StyleTraining Operation)
            if (selectedModalities.Contains(ModelConstants.Modalities.Image, StringComparer.InvariantCultureIgnoreCase))
            {
                foreach (var model in m_Models)
                {
                    if (!selectedModalities.Contains(model.modality, StringComparer.InvariantCultureIgnoreCase))
                        continue;

                    if (model.operations.Contains("StyleTraining") && availableBaseModels.Add(model.id))
                    {
                        var baseModelElement = new Toggle
                        {
                            name = model.id,
                            label = model.name.AddSpaceBeforeCapitalLetters(),
                            tooltip = model.description
                        };
                        baseModelElement.RegisterValueChangedCallback(OnFilterSelectionChanged);
                        m_BaseModelsElement.Add(baseModelElement);
                    }
                }
            }

            m_BaseModelsContainer.SetShown(availableBaseModels.Any());

            RefreshBaseModelElements(this.GetState().SelectSelectedBaseModelIds());
        }

        void RefreshBaseModelElements(IEnumerable<string> baseModelIds)
        {
            m_TempStringHashSet.Clear();
            foreach (var baseModelId in baseModelIds)
                m_TempStringHashSet.Add(baseModelId);

            // Update the visibility of the base model toggles based on the selected base models
            m_BaseModelsElement.Query<Toggle>().ForEach(toggle =>
            {
                toggle.SetValueWithoutNotify(m_TempStringHashSet.Contains(toggle.name));
            });
        }

        void ShowOperationTypes()
        {
            m_OperationsElement.Query<Toggle>().ForEach(t => t.UnregisterValueChangedCallback(OnFilterSelectionChanged));
            m_OperationsElement.Clear();
            var operations = ModelConstants.Operations.EnumerateAll();
            foreach (string operation in operations)
            {
                var operationElement = new Toggle
                {
                    name = operation,
                    label = operation.AddSpaceBeforeCapitalLetters(),
                    userData = operation,
                    enabledSelf = false
                };
                operationElement.AddToClassList("operation");
                operationElement.RegisterValueChangedCallback(OnFilterSelectionChanged);
                m_OperationsElement.Add(operationElement);
            }

            m_OperationsContainer.SetShown(Unsupported.IsDeveloperMode());

            RefreshOperationElements(this.GetState().SelectSelectedOperations());
        }

        void RefreshOperationElements(IEnumerable<string> selectedOperations)
        {
            m_TempStringHashSet.Clear();
            foreach (var operation in selectedOperations)
                m_TempStringHashSet.Add(operation);

            // Update the visibility of the operation toggles based on the selected operations
            m_OperationsElement.Query<Toggle>().ForEach(toggle =>
            {
                var selected = m_TempStringHashSet.Contains((string)toggle.userData);
                toggle.SetValueWithoutNotify(selected);
                toggle.SetShown(selected);
            });
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape)
                return;
            evt.StopPropagation();
            OnCancelButtonPressed();
        }

        void OnFilterSelectionChanged(ChangeEvent<bool> _)
        {
            // build the ModelSelectorFilters object
            var filters = this.GetState().SelectModelSelectorFilters();

            // Reuse temp collections to avoid allocations
            m_TempStringList.Clear();
            var modalityToggles = m_ModalitiesElement.Query<Toggle>();
            modalityToggles.ForEach(t =>
            {
                if (t.value)
                    m_TempStringList.Add((string)t.userData);
            });
            var modalities = m_TempStringList.ToArray();

            m_TempStringList.Clear();
            var operationToggles = m_OperationsElement.Query<Toggle>();
            operationToggles.ForEach(t =>
            {
                if (t.value)
                    m_TempStringList.Add((string)t.userData);
            });
            var operations = m_TempStringList.ToArray();

            m_TempStringList.Clear();
            var providerToggles = m_ProvidersElement.Query<Toggle>();
            providerToggles.ForEach(t =>
            {
                if (t.value)
                    m_TempStringList.Add((string)t.userData);
            });
            var providers = m_TempStringList.ToArray();

            m_TempStringList.Clear();
            var tagToggles = m_TagsElement.Query<Toggle>();
            tagToggles.ForEach(t =>
            {
                if (t.value)
                    m_TempStringList.Add(t.name);
            });
            var tags = m_TempStringList.ToArray();

            m_TempStringList.Clear();
            var baseModelToggles = m_BaseModelsElement.Query<Toggle>();
            baseModelToggles.ForEach(t =>
            {
                if (t.value)
                    m_TempStringList.Add(t.name);
            });
            var baseModelIds = m_TempStringList.ToArray();

            m_TempMiscList.Clear();
            var miscToggles = m_ModelsElement.Query<Toggle>();
            miscToggles.ForEach(t =>
            {
                if (t.value)
                    m_TempMiscList.Add((MiscModelType)t.userData);
            });
            var miscModels = m_TempMiscList.ToArray();

            var searchQuery = m_SearchBar?.value ?? string.Empty;

            this.Dispatch(ModelSelectorActions.setFilters, filters with
            {
                modalities = modalities,
                operations = operations,
                providers = providers,
                tags = tags,
                baseModelIds = baseModelIds,
                misc = miscModels,
                searchQuery = searchQuery
            });
        }

        void OnSearchFieldChanging(ChangeEvent<string> evt)
        {
            this.Dispatch(ModelSelectorActions.setSearchQuery, evt.newValue);
            DelayOnSearch();
        }

        void DelayOnSearch()
        {
            m_DelayedSearch?.Pause();
            m_DelayedSearch = schedule.Execute(OnSearch);
            m_DelayedSearch.ExecuteLater(500L);
        }

        void OnSortValueChanged(ChangeEvent<int> evt) => this.Dispatch(ModelSelectorActions.setSortMode, (SortMode)evt.newValue);

        void OnSearch()
        {
            m_FilteredModels = this.GetState().SelectFilteredModelSettings();

            m_GridView.BindToModels(
                m_TilePool,
                m_FilteredModels,
                IsModelBroken,
                ShowModelDetails,
                m_SelectedModelId
            );

            m_GridView.SetShown(m_FilteredModels.Count > 0);
            m_SearchResultsText.SetShown(m_FilteredModels.Count == 0);
        }

        void SelectModel(ModelSettings model)
        {
            if (!model.IsValid())
                return;

            if (IsModelBroken(model))
                return;

            this.Dispatch(ModelSelectorActions.setLastSelectedModelID, model.id);

            // Hold the modal open for a brief duration to show the change in model selection
            schedule.Execute(OnCancelButtonPressed).ExecuteLater(250);
        }
    }
}
