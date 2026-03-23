using System;
using System.Threading.Tasks;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.Editor
{
    interface IAssetKnowledgeProvider
    {
        bool SearchEnabled { get; set; }
        bool SearchUsable { get; }
        Task DownloadAssetKnowledge();
        void RemoveAssetKnowledge();
        bool IsAssetKnowledgeDownloaded();
        bool IsDownloadingModel { get; }
        void SetupModelDownloadProgressBar(ProgressBar progressBar);
        event Action<bool> SearchEnabledChanged;
    }

    static class AssetKnowledge
    {
        static IAssetKnowledgeProvider s_Provider;

        public static void RegisterProvider(IAssetKnowledgeProvider provider)
        {
            s_Provider = provider;
        }

        internal static bool AssetKnowledgeEnabled
        {
            get => s_Provider?.SearchEnabled ?? false;
            set
            {
                if (s_Provider != null)
                    s_Provider.SearchEnabled = value;
            }
        }
        
        internal static bool AssetKnowledgeUsable => s_Provider?.SearchUsable ?? false;

        internal static event Action<bool> SearchEnabledChanged
        {
            add
            {
                if (s_Provider != null) 
                    s_Provider.SearchEnabledChanged += value;
            }
            remove
            {
                if (s_Provider != null)
                    s_Provider.SearchEnabledChanged -= value;
            }
        }

        internal static Task DownloadAssetKnowledge()
        {
            return s_Provider?.DownloadAssetKnowledge();
        }

        internal static void RemoveAssetKnowledge()
        {
            s_Provider?.RemoveAssetKnowledge();
        }

        internal static bool IsAssetKnowledgeDownloaded()
        {
            return s_Provider?.IsAssetKnowledgeDownloaded() ?? false;
        }

        internal static bool IsDownloadingModel => s_Provider?.IsDownloadingModel ?? false;

        internal static void SetupModelDownloadProgressBar(ProgressBar progressBar)
        {
            s_Provider?.SetupModelDownloadProgressBar(progressBar);
        }
    }
}
