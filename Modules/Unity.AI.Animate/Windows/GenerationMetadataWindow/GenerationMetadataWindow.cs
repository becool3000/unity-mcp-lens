using System.Threading.Tasks;
using Unity.AI.Animate.Services.Stores.States;
using Unity.AI.Animate.Services.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Redux;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Animate.Windows.GenerationMetadataWindow
{
    class GenerationMetadataWindow : EditorWindow
    {
        public IStore store { get; set; }
        public GenerationMetadata generationMetadata { get; set; }

        TaskCompletionSource<bool> m_TaskCompletionSource;
        GenerationMetadataContent m_View;

        public static async Task Open(IStore store, AssetReference asset, VisualElement element, AnimationClipResult animationClipResult)
        {
            var window = EditorWindowExtensions.CreateWindow<GenerationMetadataWindow>(store, asset, "Generation Data", false);

            var tcs = new TaskCompletionSource<bool>();
            window.m_TaskCompletionSource = tcs;
            window.store = (Store)store;

            if (animationClipResult != null)
            {
                window.generationMetadata = await animationClipResult.GetMetadata();
                window.RefreshView();
            }

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
