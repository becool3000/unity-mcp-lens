using System.Threading.Tasks;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using Unity.AI.Assistant.Utils;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    partial class AssistantSettingsPage
    {
        const string k_InfoLabelText =
            "The Asset Knowledge add-on uses a local image model to tag and classify project assets, improving Assistant’s responses with more context. <color=#7BAEFA><link=\"packageInfo\">Read more</link></color>";

        const string k_EnableAssetKnowledgeToggleTooltip =
            "After the initial scan, Asset Knowledge automatically updates when assets are changed or added.";

        const string k_RemoveAssetKnowledgeButtonTooltip =
            "Removing the files will disable Asset Knowledge for all projects on this machine";

        const string k_PackageInfoLink =
            "https://docs.unity3d.com/Packages/com.unity.ai.assistant@1.0/manual/index.html";

        Toggle m_EnableAssetKnowledgeToggle;

        internal enum AssetKnowledgeState
        {
            NotDownloaded,
            Downloading,
            Downloaded
        }

        void InitializeViewForAssetKnowledge(TemplateContainer view)
        {
            var infoLabel = view.Q<Label>("infoLabel");
            infoLabel.text = k_InfoLabelText;
            infoLabel.enableRichText = true;
            infoLabel.RegisterCallback<PointerDownLinkTagEvent>(OnLinkClicked);

            m_EnableAssetKnowledgeToggle = view.Q<Toggle>("enableAssetKnowledge");

            m_EnableAssetKnowledgeToggle.tooltip = k_EnableAssetKnowledgeToggleTooltip;
            m_EnableAssetKnowledgeToggle.RegisterValueChangedCallback(evt =>
            {
                AssetKnowledge.AssetKnowledgeEnabled = evt.newValue;
            });

            AssetKnowledge.SearchEnabledChanged += enabled =>
            {
                m_EnableAssetKnowledgeToggle.SetValueWithoutNotify(enabled);
            };

            view.SetupButton("downloadSiglip2Button", DownloadAssetKnowledge);
            var removeSiglip2Button = view.SetupButton("removeSiglip2Button", RemoveAssetKnowledge);
            removeSiglip2Button.tooltip = k_RemoveAssetKnowledgeButtonTooltip;

            SetupAssetKnowledgeContainers(GetAssetKnowledgeState());
        }

        AssetKnowledgeState GetAssetKnowledgeState()
        {
            if (AssetKnowledge.IsAssetKnowledgeDownloaded())
            {
                return AssetKnowledgeState.Downloaded;
            }

            if (AssetKnowledge.IsDownloadingModel)
            {
                return AssetKnowledgeState.Downloading;
            }

            return AssetKnowledgeState.NotDownloaded;
        }

        internal void SetupAssetKnowledgeContainers(AssetKnowledgeState state)
        {
            m_EnableAssetKnowledgeToggle.value = AssetKnowledge.AssetKnowledgeEnabled;

            var enableAssetKnowledgeContainer = m_View.Q<VisualElement>("enableAssetKnowledgeContainer");
            var downloadAssetKnowledgeContainer = m_View.Q<VisualElement>("downloadAssetKnowledgeContainer");
            var removeAssetKnowledgeContainer = m_View.Q<VisualElement>("removeAssetKnowledgeContainer");
            var downloadingAssetKnowledgeContainer = m_View.Q<VisualElement>("downloadingAssetKnowledgeContainer");

            downloadAssetKnowledgeContainer.SetDisplay(false);
            enableAssetKnowledgeContainer.SetDisplay(false);
            removeAssetKnowledgeContainer.SetDisplay(false);
            downloadingAssetKnowledgeContainer.SetDisplay(false);

            switch (state)
            {
                case AssetKnowledgeState.Downloaded:
                    enableAssetKnowledgeContainer.SetDisplay(true);
                    removeAssetKnowledgeContainer.SetDisplay(true);
                    break;
                case AssetKnowledgeState.Downloading:
                    downloadingAssetKnowledgeContainer.SetDisplay(true);
                    var progressBar = m_View.Q<ProgressBar>("assetKnowledgeDownloadProgressBar");
                    AssetKnowledge.SetupModelDownloadProgressBar(progressBar);
                    break;
                case AssetKnowledgeState.NotDownloaded:
                    downloadAssetKnowledgeContainer.SetDisplay(true);
                    break;
            }
        }

        internal void RemoveAssetKnowledge(PointerUpEvent evt)
        {
            AssetKnowledge.RemoveAssetKnowledge();
            SetupAssetKnowledgeContainers(GetAssetKnowledgeState());
            AssetKnowledge.AssetKnowledgeEnabled = false;
        }

        void DownloadAssetKnowledge(PointerUpEvent evt)
        {
            SetupAssetKnowledgeContainers(AssetKnowledgeState.Downloading);

            Task.Run(async () =>
            {
                await AssetKnowledge.DownloadAssetKnowledge();

                MainThread.DispatchAndForget(() => { SetupAssetKnowledgeContainers(GetAssetKnowledgeState()); });
            });
        }

        void OnLinkClicked(PointerDownLinkTagEvent evt)
        {
            // There is only 1 link:
            if (evt.linkID != null)
            {
                UnityEngine.Application.OpenURL(k_PackageInfoLink);
            }
        }
    }
}
