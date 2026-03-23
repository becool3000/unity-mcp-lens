using System;
using System.Threading.Tasks;
using Unity.AI.Assistant.Editor;
using Unity.AI.Search.Editor.Knowledge;
using UnityEditor;
using UnityEngine.UIElements;
using AssetKnowledge = Unity.AI.Assistant.Editor.AssetKnowledge;

namespace Unity.AI.Search.Editor.Services
{
    [InitializeOnLoad]
    class AssetKnowledgeProvider : IAssetKnowledgeProvider
    {
        static AssetKnowledgeProvider()
        {
            AssetKnowledge.RegisterProvider(new AssetKnowledgeProvider());
        }

        public bool SearchEnabled
        {
            get => AssetKnowledgeSettings.SearchEnabled;
            set => AssetKnowledgeSettings.SearchEnabled = value;
        }

        public bool SearchUsable => AssetKnowledgeSettings.SearchUsable;

        public event Action<bool> SearchEnabledChanged
        {
            add => AssetKnowledgeSettings.SearchEnabledChanged += value;
            remove => AssetKnowledgeSettings.SearchEnabledChanged -= value;
        }

        public Task DownloadAssetKnowledge()
        {
            return ModelService.TryFetchModel(
                Models.SigLip2.ModelInfo);
        }

        public void RemoveAssetKnowledge()
        {
            ModelService.RemoveModel(
                Models.SigLip2.ModelInfo);
        }

        public bool IsAssetKnowledgeDownloaded()
        {
            return AssetKnowledgeSettings.IsAssetKnowledgeModelDownloaded();
        }

        public bool IsDownloadingModel => ModelService.IsDownloadingModel;

        public void SetupModelDownloadProgressBar(ProgressBar progressBar)
        {
            var progress = ModelService.ModelDownloadProgress;
            if (progress != null)
            {
                progress.ProgressBar = progressBar;
            }
        }
    }
}
