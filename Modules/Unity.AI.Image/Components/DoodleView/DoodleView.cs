using System;
using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Image.Services.Undo;
using Unity.AI.Image.Services.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    class DoodleView : VisualElement
    {
        readonly DoodlePad m_DoodlePad;

        readonly Toggle m_ShowBaseImageToggle;

        readonly VisualElement m_Guides;

        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/DoodleView/DoodleView.uxml";

        public event Action saveRequested;

        public DoodleView()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            var toolGroup = this.Q<ToggleButtonGroup>("toolGroup");
            var clearButton = this.Q<Button>("clearButton");
            var brushSettingsToolbar = this.Q<VisualElement>("brushSettingsToolbar");
            var brushSizeLabel = this.Q<Label>("brushSizeLabel");
            var brushSizeSlider = this.Q<SliderInt>("brushSizeSlider");
            var brushColorField = this.Q<ColorField>("brushColorField");
            var saveButton = this.Q<Button>("saveButton");
            m_Guides = this.Q<VisualElement>("guides");
            m_DoodlePad = this.Q<DoodlePad>("doodlePad");
            m_ShowBaseImageToggle = this.Q<Toggle>("showBaseImageToggle");
            var baseImageOpacitySlider = this.Q<SliderInt>("baseImageOpacitySlider");

            // Config
            m_DoodlePad.RegisterCallback<GeometryChangedEvent>(OnCanvasGeometryChanged);

            // binding Dispatch
            m_ShowBaseImageToggle.RegisterValueChangedCallback(evt => this.Dispatch(DoodleWindowActions.setShowBaseImage, evt.newValue));
            baseImageOpacitySlider.RegisterValueChangedCallback(evt => this.Dispatch(DoodleWindowActions.setBaseImageOpacity, evt.newValue / 100f));
            m_DoodlePad.SetDoodleSize(Vector2Int.one * 256);
            m_DoodlePad.SetBrushSize(10);
            m_DoodlePad.RegisterValueChangedCallback(evt =>
            {
                this.Dispatch(DoodleWindowActions.setLayer, (0, evt.newValue));
                DoodleWindowHistory.instance.Push(this.GetState().SelectState());
            });
            toolGroup.RegisterValueChangedCallback(evt =>
            {
                var tool = DoodleTool.None;
                for (var i = 0; i < evt.newValue.length; i++)
                {
                    if (evt.newValue[i]) tool = (DoodleTool)i;
                }
                this.Dispatch(DoodleWindowActions.setTool, tool);
            });
            clearButton.clicked += ClearDoodle;
            brushSizeSlider.RegisterValueChangedCallback(evt => this.Dispatch(DoodleWindowActions.setBrushSize, evt.newValue));
            brushColorField.RegisterValueChangedCallback(evt => m_DoodlePad.SetBrushColor(evt.newValue));
            brushColorField.SetValueWithoutNotify(m_DoodlePad.brushColor);
            saveButton.clicked += () => saveRequested?.Invoke();

            // binding Select
            this.Use(DoodleWindowSelectors.SelectDoodleSize, size =>
            {
                m_DoodlePad.SetDoodleSize(size);
                OnCanvasGeometryChanged(null);
            });
            this.Use(DoodleWindowSelectors.SelectBrushSize, size =>
            {
                brushSizeSlider.SetValueWithoutNotify((int) size);
                m_DoodlePad.SetBrushSize(size);
            });
            this.UseArray(state => state.SelectDoodleLayerData(0), data => m_DoodlePad.SetValueWithoutNotify(data as byte[]));
            this.Use(DoodleWindowSelectors.SelectDoodleTool, doodleTool =>
            {
                switch (doodleTool)
                {
                    case DoodleTool.Brush:
                        m_DoodlePad.SetBrush();
                        ShowToolbarWithSettings(true, true);
                        break;
                    case DoodleTool.Eraser:
                        m_DoodlePad.SetEraser();
                        ShowToolbarWithSettings(false, true);
                        break;
                    case DoodleTool.Fill:
                        m_DoodlePad.SetBucketFill();
                        ShowToolbarWithSettings(true, false);
                        break;
                    case DoodleTool.None:
                    default:
                        m_DoodlePad.SetNone();
                        brushSettingsToolbar.SetShown(false);
                        break;
                }

                if (toolGroup.childCount == 0) return;
                var bitMask = doodleTool == DoodleTool.None ? 0 : (ulong) 1 << (int) doodleTool;
                toolGroup.SetValueWithoutNotify(new ToggleButtonGroupState(bitMask, toolGroup.childCount));
            });

            this.Use(DoodleWindowSelectors.SelectShowBaseImage, async showBaseImage =>
            {
                Texture2D baseImage = null;
                if (showBaseImage)
                {
                    var selectedGeneration = this.GetState().SelectSelectedGeneration(this);
                    if (selectedGeneration.IsValid())
                        baseImage = await TextureCache.GetTexture(selectedGeneration.uri);
                    else
                        baseImage = this.GetAsset()?.GetObject() as Texture2D;
                }
                m_DoodlePad.backgroundImage = baseImage;
                baseImageOpacitySlider.SetEnabled(showBaseImage);
                m_ShowBaseImageToggle.SetValueWithoutNotify(showBaseImage);
            });
            this.Use(DoodleWindowSelectors.SelectBaseImageOpacity, opacity =>
            {
                m_DoodlePad.backGroundImageOpacity = opacity;
                baseImageOpacitySlider.SetValueWithoutNotify((int)(opacity * 100));
            });

            this.UseAsset(SetAsset);
            this.Use(state => state.SelectSelectedGeneration( this), OnGenerationSelected);
            return;

            void ShowToolbarWithSettings(bool showColorField, bool showBrushSize)
            {
                brushSettingsToolbar.SetShown();
                brushColorField.SetShown(showColorField);
                brushSizeLabel.SetShown(showBrushSize);
                brushSizeSlider.SetShown(showBrushSize);
            }
        }

        void OnCanvasGeometryChanged(GeometryChangedEvent evt)
        {
            if (!m_DoodlePad.layout.IsValid())
                return;

            var elementSize = m_DoodlePad.GetDoodleSize();
            var containerSize = m_DoodlePad.layout.size;
            var offset = m_Guides.resolvedStyle.borderTopWidth * 2;
            ScaleToFit(m_Guides, containerSize, elementSize, offset);
        }

        static void ScaleToFit(VisualElement target, Vector2 containerSize, Vector2 elementSize, float offset)
        {
            var scaleX = containerSize.x / elementSize.x;
            var scaleY = containerSize.y / elementSize.y;

            var scale = Mathf.Min(scaleX, scaleY);
            var newWidth = Mathf.RoundToInt(elementSize.x * scale) + offset;
            var newHeight = Mathf.RoundToInt(elementSize.y * scale) + offset;
            target.style.width = newWidth;
            target.style.height = newHeight;

            var newPositionX = (containerSize.x - newWidth) * .5f;
            var newPositionY = (containerSize.y - newHeight) * .5f;

            target.style.left = newPositionX;
            target.style.top = newPositionY;
        }

        public void ClearDoodle()
        {
            if (EditorUtility.DisplayDialog("Clear Doodle", "Are you sure you want to clear the doodle?", "Yes", "No"))
                m_DoodlePad.value = null;
        }

        public void FillDoodle()
        {
            m_DoodlePad.Fill();
        }

        public void SelectBrush()
        {
            this.Dispatch(DoodleWindowActions.setTool, DoodleTool.Brush);
        }

        public void SelectEraser()
        {
            this.Dispatch(DoodleWindowActions.setTool, DoodleTool.Eraser);
        }

        public void SelectFill()
        {
            this.Dispatch(DoodleWindowActions.setTool, DoodleTool.Fill);
        }

        public void SelectMove()
        {
            this.Dispatch(DoodleWindowActions.setTool, DoodleTool.None);
        }

        public void DecreaseBrushSize()
        {
            var currentBrushSize = this.GetState().SelectBrushSize();
            this.Dispatch(DoodleWindowActions.setBrushSize, Mathf.Clamp(currentBrushSize - 1, 3f, 50f));
        }

        public void IncreaseBrushSize()
        {
            var currentBrushSize = this.GetState().SelectBrushSize();
            this.Dispatch(DoodleWindowActions.setBrushSize, Mathf.Clamp(currentBrushSize + 1, 3f, 50f));
        }

        public void ToggleShowBaseImage()
        {
            this.Dispatch(DoodleWindowActions.setShowBaseImage, !this.GetState().SelectShowBaseImage());
        }

        async void OnGenerationSelected(TextureResult result)
        {
            if (!m_ShowBaseImageToggle.value)
                return;

            if (result.IsValid())
                m_DoodlePad.backgroundImage = await TextureCache.GetTexture(result.uri);
            else
                m_DoodlePad.backgroundImage = this.GetAsset()?.GetObject() as Texture2D;
        }

        void SetAsset(AssetReference obj)
        {
            m_DoodlePad.backgroundImage = m_ShowBaseImageToggle.value ? obj?.GetObject() as Texture2D : null;
        }
    }
}
