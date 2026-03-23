using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Panel
{
    [UxmlElement]
    partial class AIPanel : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/Modules/Unity.AI.Image/Panels/AIPanel/AIPanel.uxml";
        GeneratePanel m_GeneratePanel;
        InpaintingPanel m_InpaintingPanel;
        PixelatePanel m_PixelatePanel;
        RecolorPanel m_RecolorPanel;
        RemoveBackgroundPanel m_RemoveBackgroundPanel;
        UpscalePanel m_UpscalePanel;
        SpritesheetPanel m_SpritesheetPanel;

        public AIPanel()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);
            m_GeneratePanel = this.Q<GeneratePanel>();
            m_InpaintingPanel = this.Q<InpaintingPanel>();
            m_PixelatePanel = this.Q<PixelatePanel>();
            m_RecolorPanel = this.Q<RecolorPanel>();
            m_RemoveBackgroundPanel = this.Q<RemoveBackgroundPanel>();
            m_UpscalePanel = this.Q<UpscalePanel>();
            m_SpritesheetPanel = this.Q<SpritesheetPanel>();

            // Use() handles both initial value and changes
            this.Use(state => state.SelectRefinementMode(this), OnRefinementModeChanged);
        }

        void OnRefinementModeChanged(RefinementMode mode)
        {
            m_GeneratePanel.style.display = DisplayStyle.None;
            m_InpaintingPanel.style.display = DisplayStyle.None;
            m_PixelatePanel.style.display = DisplayStyle.None;
            m_RecolorPanel.style.display = DisplayStyle.None;
            m_RemoveBackgroundPanel.style.display = DisplayStyle.None;
            m_UpscalePanel.style.display = DisplayStyle.None;
            m_SpritesheetPanel.style.display = DisplayStyle.None;
            switch (mode)
            {
                case RefinementMode.Generation:
                    m_GeneratePanel.style.display = DisplayStyle.Flex;
                    break;
                case RefinementMode.Inpaint:
                    m_InpaintingPanel.style.display = DisplayStyle.Flex;
                    break;
                case RefinementMode.Pixelate:
                    m_PixelatePanel.style.display = DisplayStyle.Flex;
                    break;
                case RefinementMode.Recolor:
                    m_RecolorPanel.style.display = DisplayStyle.Flex;
                    break;
                case RefinementMode.Upscale:
                    m_UpscalePanel.style.display = DisplayStyle.Flex;
                    break;
                case RefinementMode.RemoveBackground:
                    m_RemoveBackgroundPanel.style.display = DisplayStyle.Flex;
                    break;
                case RefinementMode.Spritesheet:
                    m_SpritesheetPanel.style.display = DisplayStyle.Flex;
                    break;
            }
        }
    }
}
