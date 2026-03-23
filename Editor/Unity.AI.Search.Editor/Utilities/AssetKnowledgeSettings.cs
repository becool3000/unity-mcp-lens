using System;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Search.Editor.Knowledge
{
    [Serializable]
    [FilePath("Library/AI.Search/AssetKnowledgeSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    class AssetKnowledgeSettings : ScriptableSingleton<AssetKnowledgeSettings>
    {
        [SerializeField] public string lastDescriptorSignature;
        [SerializeField] bool searchEnabled;

        public static event Action<bool> SearchEnabledChanged;

        public static bool SearchEnabled
        {
            get => instance.searchEnabled;
            set
            {
                if (value != SearchEnabled)
                {
                    instance.searchEnabled = value;
                    SearchEnabledChanged?.Invoke(value);

                    instance.SaveNow();
                }
            }
        }
        
        /// <summary>
        /// Asset knowledge has a few dependencies, this wraps all checks in a single property:
        /// </summary>
        public static bool SearchUsable =>
#if SENTIS_AVAILABLE
            SearchEnabled && IsAssetKnowledgeModelDownloaded();
#else
            false;
#endif

        /// <summary>
        /// The Asset Import Pipeline can only be run synchronously, so we need to ensure to avoid all time-based
        /// and async-based processing. Async/Await method still do work, but timing logic won't, and Tensor Async operation
        /// in Inference Engine likewise won't work (stall forever).
        /// </summary>
        static bool IsPipelineSync => AssetDatabase.IsAssetImportWorkerProcess();

        static bool s_RunAsyncOverride = true;

        public static bool RunAsync
        {
            get => s_RunAsyncOverride && !IsPipelineSync;
            set => s_RunAsyncOverride = value;
        }

        public void SaveNow() => Save(true);
        
        public static bool IsAssetKnowledgeModelDownloaded()
        {
            return Services.ModelService.IsModelDownloaded(Services.Models.SigLip2.ModelInfo);
        }
    }
}
