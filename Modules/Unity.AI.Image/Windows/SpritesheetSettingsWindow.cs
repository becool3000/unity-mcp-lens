using System.Threading.Tasks;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Contexts;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Image.Components;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Windows
{
    class SpritesheetSettingsWindow : EditorWindow
    {
        IStore m_Store;
        VisualElement m_Element;

        TaskCompletionSource<bool> m_TaskCompletionSource;
        SpritesheetSettings m_View;

        public static async Task Open(IStore store, AssetReference asset, VisualElement element)
        {
            var window = EditorWindowExtensions.CreateWindow<SpritesheetSettingsWindow>(store, asset, "Spritesheet Settings", false);

            var tcs = new TaskCompletionSource<bool>();
            window.m_TaskCompletionSource = tcs;
            window.m_Store = store;
            window.m_Element = element;

            window.minSize = new Vector2(180, 184);
            window.maxSize = new Vector2(180, 184);
            window.ShowAuxWindow();

            await tcs.Task;
        }

        void CreateGUI()
        {
            if (m_View != null)
                m_View.OnDismissRequested -= Close;

            rootVisualElement.Clear();
            m_View = new SpritesheetSettings();
            m_View.ProvideContext(StoreExtensions.storeKey, m_Store);
            m_View.OnDismissRequested += Close;
            rootVisualElement.Add(m_View);
        }

        void OnDestroy() => m_TaskCompletionSource?.TrySetResult(true);
    }
}
