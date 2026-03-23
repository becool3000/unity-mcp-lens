using System.Threading.Tasks;
using Unity.AI.Sound.Services.Stores.States;
using Unity.AI.Sound.Services.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Redux;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Sound.Windows
{
    class GenerationMetadataWindow : EditorWindow
    {
        public IStore store { get; set; }
        public GenerationMetadata generationMetadata { get; set; }

        TaskCompletionSource<bool> m_TaskCompletionSource;
        GenerationMetadataContent m_View;

        public static async Task Open(IStore store, AssetReference asset, VisualElement element, AudioClipResult audioClipResult)
        {
            var window = EditorWindowExtensions.CreateWindow<GenerationMetadataWindow>(store, asset, "Generation Data", false);

            var tcs = new TaskCompletionSource<bool>();
            window.m_TaskCompletionSource = tcs;
            window.store = (Store)store;

            if (audioClipResult != null)
            {
                window.generationMetadata = await audioClipResult.GetMetadata();
                window.RefreshView();
            }

            window.minSize = new Vector2(300, 100);
            window.maxSize = new Vector2(600, 500);
            window.ShowAuxWindow();

            await tcs.Task;
        }

        void CreateGUI()
        {
            RefreshView();
        }

        void RefreshView()
        {
            if (m_View != null)
                m_View.OnDismissRequested -= Close;

            rootVisualElement.Clear();
            m_View = new GenerationMetadataContent(store, generationMetadata);
            m_View.OnDismissRequested += Close;
            rootVisualElement.Add(m_View);
        }

        void OnDestroy() => m_TaskCompletionSource?.TrySetResult(true);
    }
}
