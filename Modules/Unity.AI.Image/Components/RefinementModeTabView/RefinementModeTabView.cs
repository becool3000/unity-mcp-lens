using System;
using System.Linq;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Image.Services.Utilities;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class RefinementModeTabView : TabView
    {
        RefinementMode[] m_RefinementModes;
        RefinementMode m_LastRefinementMode = RefinementMode.Generation;
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/RefinementModeTabView/RefinementModeTabView.uxml";

        public RefinementModeTabView()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            AddToClassList("refinement-mode-tabview");

            activeTabChanged += (_, _) =>
            {
                if (this.GetStoreApi() == null)
                    return;
                this.Dispatch(GenerationSettingsActions.setRefinementMode,
                    (RefinementMode)Math.Clamp((int)m_RefinementModes[selectedTabIndex], 0, Enum.GetNames(typeof(RefinementMode)).Length));
            };

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                var refinementMode = this.GetState().SelectRefinementMode(this);
                m_LastRefinementMode = refinementMode;
                if (m_RefinementModes != null)
                    selectedTabIndex = Array.IndexOf(m_RefinementModes, m_LastRefinementMode);
            });

            this.Use(state => state.SelectRefinementMode(this), refinementMode =>
            {
                m_LastRefinementMode = refinementMode;
                if (m_RefinementModes != null)
                    selectedTabIndex = Array.IndexOf(m_RefinementModes, m_LastRefinementMode);
            });
            this.UseAsset(asset =>
            {
                if (this.GetState() == null)
                    return;
                m_RefinementModes = this.GetState().SelectAvailableRefinementModesOrdered(asset).ToArray();
                var tabHeaders = this.Query<VisualElement>(className: "unity-tab__header").ToList();

                for (var i = 0; i < tabHeaders.Count; i++)
                {
                    if (i >= m_RefinementModes.Length)
                    {
                        tabHeaders[i].SetShown(false);
                        continue;
                    }

                    var label = tabHeaders[i].Q<Label>();
                    if (label != null)
                        label.text = m_RefinementModes[i].GetDisplayLabel();

                    var mode = m_RefinementModes[i];
                    tabHeaders[i].SetShown(m_RefinementModes.Contains(mode));
                }
                if (m_RefinementModes != null)
                    selectedTabIndex = Array.IndexOf(m_RefinementModes, m_LastRefinementMode);
            });
        }
    }
}
