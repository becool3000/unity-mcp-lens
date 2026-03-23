using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Contexts;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Image.Components;
using Unity.AI.Image.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Windows
{
    class AddToPromptWindow : EditorWindow
    {
        IStore store { get; set; }
        Dictionary<ImageReferenceType, bool> typesValidationResults { get; set; }

        TaskCompletionSource<bool> m_TaskCompletionSource;
        AddToPromptView m_View;
        VisualElement element { get; set; }

        public static async Task Open(IStore store, AssetReference asset, VisualElement element, Dictionary<ImageReferenceType, bool> typesValidationResults)
        {
            var window = EditorWindowExtensions.CreateWindow<AddToPromptWindow>(store, asset,"Select which operator to Add", false);

            var tcs = new TaskCompletionSource<bool>();
            window.m_TaskCompletionSource = tcs;
            window.store = store;
            window.element = element;

            window.typesValidationResults = typesValidationResults;

            window.minSize = new Vector2(782, 586);
            window.maxSize = new Vector2(782, 586);
            window.ShowAuxWindow();

            await tcs.Task;
        }

        void CreateGUI()
        {
            if (m_View != null)
                m_View.OnDismissRequested -= Close;

            rootVisualElement.Clear();
            m_View = new AddToPromptView(typesValidationResults);
            m_View.ProvideContext(StoreExtensions.storeKey, store);
            m_View.OnDismissRequested += Close;
            rootVisualElement.Add(m_View);
        }

        void OnDestroy() => m_TaskCompletionSource?.TrySetResult(true);
    }
}
