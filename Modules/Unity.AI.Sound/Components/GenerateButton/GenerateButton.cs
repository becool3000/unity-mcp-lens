using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Sound.Services.Stores.Actions;
using Unity.AI.Sound.Services.Stores.Selectors;
using Unity.AI.Sound.Services.Utilities;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Actions;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.AI.Toolkit.Accounts.Manipulators;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Accounts.Services;

namespace Unity.AI.Sound.Components
{
    [UxmlElement]
    partial class GenerateButton : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Sound/Components/GenerateButton/GenerateButton.uxml";
        const int k_ReenableDelay = 5000;

        readonly Button m_Button;
        readonly Label m_Label;
        readonly GenerateButtonInsufficientPointsManipulator m_InsufficientPointsManipulator;
        CancellationTokenSource m_CancellationTokenSource;

        [UxmlAttribute]
        public string text
        {
            get => m_Label.text;
            set => m_Label.text = value;
        }

        [UxmlAttribute]
        public bool quoteMonitor { get; set; } = true;

        public GenerateButton()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            this.AddManipulator(new GeneratorsSessionStatusTracker());

            m_InsufficientPointsManipulator = new GenerateButtonInsufficientPointsManipulator(() => OnGenerationValidationResultsChanged(this.GetState().SelectGenerationValidationResult(this)));
            this.AddManipulator(m_InsufficientPointsManipulator);

            m_Button = this.Q<Button>("generate-button");
            m_Label = m_Button.Q<Label>();
            m_Button.clickable = new Clickable(OnGenerate);

            this.Use(state => state.SelectGenerationAllowed(this), OnGenerationAllowedChanged);
            // ReSharper disable once AsyncVoidLambda
            this.UseAsset(async asset => {
                if (!quoteMonitor)
                    return;

                // ReSharper disable once AsyncVoidLambda
                this.Use(state => state.SelectGenerationValidationSettings(this), async settings => {
                    await EditorTask.Yield();
                    _ = this.GetStoreApi().Dispatch(GenerationResultsActions.quoteAudioClipsMain, settings.asset);
                });

                await EditorTask.Yield();
                _ = this.GetStoreApi().Dispatch(GenerationResultsActions.checkDownloadRecovery, asset);
            });
            this.Use(state => state.SelectGenerationValidationResult(this), OnGenerationValidationResultsChanged);
        }

        void OnGenerationValidationResultsChanged(GenerationValidationResult result)
        {
            m_InsufficientPointsManipulator.OnGenerationValidationResultChanged(result);
            if (result.success)
            {
                tooltip = "";
                m_Button.SetEnabled(Account.pointsBalance.CanAfford(result.cost));
                return;
            }

            tooltip = result.feedback.Count > 0 ? string.Join("\n", result.feedback.Select(f => f.message)) : string.Empty;
        }

        void OnGenerationAllowedChanged(bool allowed)
        {
            m_Button.SetEnabled(allowed);

            m_CancellationTokenSource?.Cancel();
            m_CancellationTokenSource?.Dispose();
            m_CancellationTokenSource = null;
            if (!allowed)
            {
                m_CancellationTokenSource = new();
                _ = ReenableGenerateButton(m_CancellationTokenSource.Token);
            }
        }
        async Task ReenableGenerateButton(CancellationToken token)
        {
            try
            {
                await EditorTask.Delay(k_ReenableDelay, token);
                if (!token.IsCancellationRequested)
                    this.Dispatch(GenerationActions.setGenerationAllowed, new(this.GetAsset(), true));
            }
            catch (TaskCanceledException)
            {
                // The token was cancelled, so do nothing
            }
        }

        void OnAttachToPanel(AttachToPanelEvent evt) =>
            evt.destinationPanel.visualTree.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.NoTrickleDown);

        void OnDetachFromPanel(DetachFromPanelEvent evt) =>
            evt.originPanel.visualTree.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.NoTrickleDown);

        void OnKeyDown(KeyDownEvent evt)
        {
            if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && evt.actionKey && m_Button.enabledInHierarchy)
            {
                evt.StopPropagation();
                OnGenerate();
            }
        }

        void OnGenerate() =>
            this.GetStoreApi().Dispatch(GenerationResultsActions.generateAudioClipsMain, this.GetAsset());
    }
}
