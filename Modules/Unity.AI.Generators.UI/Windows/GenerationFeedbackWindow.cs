using System;
using System.IO;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UI.Actions;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.UI
{
    class GenerationFeedbackWindow : EditorWindow
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/Modules/Unity.AI.Generators.UI/Windows/GenerationFeedbackWindow.uxml";

        AssetReference m_Asset;
        Uri m_GenerationUri;
        GenerationFeedbackSentiment m_Sentiment;
        string m_DialogType;
        IStoreApi m_StoreApi;

        TextField m_FeedbackTextField;
        Button m_SubmitButton;

        public static void ShowFeedbackWindow(AssetReference asset, Uri generationUri, GenerationFeedbackSentiment sentiment, string dialogType, IStoreApi storeApi)
        {
            var window = GetWindow<GenerationFeedbackWindow>(true, "Provide Feedback", true);
            window.minSize = new Vector2(300, 150);
            window.maxSize = new Vector2(800, 600);
            window.Initialize(asset, generationUri, sentiment, dialogType, storeApi);
            window.ShowUtility();
        }

        public void Initialize(AssetReference asset, Uri generationUri, GenerationFeedbackSentiment sentiment, string dialogType, IStoreApi storeApi)
        {
            m_Asset = asset;
            m_GenerationUri = generationUri;
            m_Sentiment = sentiment;
            m_DialogType = dialogType;
            m_StoreApi = storeApi;
            
            CreateGUI();
        }

        void CreateGUI()
        {
            rootVisualElement.Clear();

            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            if (uxml != null)
            {
                uxml.CloneTree(rootVisualElement);
            }

            m_FeedbackTextField = rootVisualElement.Q<TextField>("feedback-text-field");
            if (m_FeedbackTextField != null)
                m_FeedbackTextField.verticalScrollerVisibility = ScrollerVisibility.Auto;
            m_SubmitButton = rootVisualElement.Q<Button>("submit-button");

            var cancelButton = rootVisualElement.Q<Button>("cancel-button");
            if (cancelButton != null)
            {
                cancelButton.clicked += Close;
            }

            if (m_SubmitButton != null)
            {
                m_SubmitButton.clicked += SubmitFeedback;
            }

            m_FeedbackTextField?.Focus();
        }

        void SubmitFeedback()
        {
            if (m_StoreApi == null || m_GenerationUri == null || m_Asset == null)
            {
                Close();
                return;
            }

            var text = m_FeedbackTextField.text;
            
            _ = m_StoreApi.Dispatch(FeedbackActions.submitGenerationFeedback, new SubmitGenerationFeedbackPayload(
                m_Asset,
                m_GenerationUri.AbsoluteUri,
                m_Sentiment,
                m_DialogType,
                feedbackText: text,
                downloadedAssetId: Path.GetFileNameWithoutExtension(m_GenerationUri.GetLocalPath())));

            Close();
        }
    }
}
