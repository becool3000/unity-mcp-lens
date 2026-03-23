using System;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Image.Services.Stores.States;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    class SpritesheetSettings : VisualElement
    {
        public event Action OnDismissRequested;
        public event Action OnSettingsApplied;

        const string k_Uxml = "Packages/com.unity.ai.assistant/Modules/Unity.AI.Image/Components/SpritesheetSettings/SpritesheetSettings.uxml";

        readonly IntegerField m_TileColumnsField;
        readonly IntegerField m_TileRowsField;
        readonly IntegerField m_OutputWidthField;
        readonly IntegerField m_OutputHeightField;
        readonly Button m_CancelButton;
        readonly Button m_OkButton;

        SpritesheetSettingsState m_OriginalSettings;

        public SpritesheetSettings()
        {
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            m_TileColumnsField = this.Q<IntegerField>("tile-columns-field");
            m_TileRowsField = this.Q<IntegerField>("tile-rows-field");
            m_OutputWidthField = this.Q<IntegerField>("output-width-field");
            m_OutputHeightField = this.Q<IntegerField>("output-height-field");
            m_CancelButton = this.Q<Button>("cancel-button");
            m_OkButton = this.Q<Button>("ok-button");
        }

        void OnAttachToPanel(AttachToPanelEvent _)
        {
            LoadCurrentSettings();

            m_CancelButton.clicked += OnCancelButtonPressed;
            m_OkButton.clicked += OnOkButtonPressed;

            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        }

        void OnDetachFromPanel(DetachFromPanelEvent _)
        {
            m_CancelButton.clicked -= OnCancelButtonPressed;
            m_OkButton.clicked -= OnOkButtonPressed;

            UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        }

        void LoadCurrentSettings()
        {
            var settings = this.GetState()?.SelectSpritesheetSettings(this);
            if (settings == null)
                settings = new SpritesheetSettingsState();

            m_OriginalSettings = settings with { };

            m_TileColumnsField.SetValueWithoutNotify(settings.tileColumns);
            m_TileRowsField.SetValueWithoutNotify(settings.tileRows);
            m_OutputWidthField.SetValueWithoutNotify(settings.outputWidth);
            m_OutputHeightField.SetValueWithoutNotify(settings.outputHeight);
        }

        void OnCancelButtonPressed()
        {
            OnDismissRequested?.Invoke();
        }

        void OnOkButtonPressed()
        {
            var newSettings = new SpritesheetSettingsState
            {
                tileColumns = Mathf.Clamp(m_TileColumnsField.value, SpritesheetSettingsState.minTileCount, SpritesheetSettingsState.maxTileCount),
                tileRows = Mathf.Clamp(m_TileRowsField.value, SpritesheetSettingsState.minTileCount, SpritesheetSettingsState.maxTileCount),
                outputWidth = Mathf.Clamp(m_OutputWidthField.value, SpritesheetSettingsState.minResolution, SpritesheetSettingsState.maxResolution),
                outputHeight = Mathf.Clamp(m_OutputHeightField.value, SpritesheetSettingsState.minResolution, SpritesheetSettingsState.maxResolution)
            };

            this.Dispatch(GenerationSettingsActions.setSpritesheetSettings, newSettings);
            OnSettingsApplied?.Invoke();
            OnDismissRequested?.Invoke();
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Escape:
                    evt.StopPropagation();
                    OnCancelButtonPressed();
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    evt.StopPropagation();
                    OnOkButtonPressed();
                    break;
            }
        }
    }
}
