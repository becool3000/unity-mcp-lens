using System;
using System.IO;
using Unity.AI.Sound.Components;
using Unity.AI.Sound.Services.SessionPersistence;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Contexts;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Sound.Windows
{
    class SoundEnvelopeWindow : EditorWindow, IAssetEditorWindow
    {
        public static void Display(string assetPath)
        {
            var asset = new AssetReference { guid = AssetDatabase.AssetPathToGUID(assetPath) };
            var window = EditorWindowExtensions.CreateAssetWindow<SoundEnvelopeWindow>(asset, $"Trim {Path.GetFileNameWithoutExtension(assetPath)}", false);
            window.minSize = new Vector2(640, 480);
            window.maxSize = new Vector2(3840, 2160);
            window.Show();
            window.Focus();
        }

        public AssetReference asset
        {
            get => assetContext;
            set
            {
                if (assetContext == value)
                    return;

                assetContext = value;
                titleContent.text = $"Trim {Path.GetFileNameWithoutExtension(value.GetPath())}";

                // refresh the titlebar
                Show();
                Focus();
            }
        }

        [SerializeField]
        AssetReference assetContext = new();

        public bool isLocked
        {
            get => isEditorLocked;
            set => isEditorLocked = value;
        }

        public IStore store => SharedStore.Store;

        [SerializeField]
        bool isEditorLocked = true;

        SoundEnvelope m_SoundEnvelope;

        void CreateGUI()
        {
            this.EnsureContext();
            rootVisualElement.Add(m_SoundEnvelope = new SoundEnvelope());
        }

        void ShowButton(Rect rect)
        {
            if (this.ShowContextLockButton(rect))
                this.OnContextChange();
        }

        void OnSelectionChange() => this.OnContextChange();

        void OnAssetMoved(string oldPath, string newPath)
        {
            if (asset.GetPath() == oldPath)
                titleContent.text = $"Trim {Path.GetFileNameWithoutExtension(newPath)}";
        }

        void OnEnable()
        {
            titleContent.image = null;
            AssetRenameWatcher.OnAssetMoved += OnAssetMoved;
        }

        void OnDisable()
        {
            AssetRenameWatcher.OnAssetMoved -= OnAssetMoved;
            m_SoundEnvelope?.SaveOnClose();
        }

        protected override void OnBackingScaleFactorChanged()
        {
            base.OnBackingScaleFactorChanged();
            if (rootVisualElement?.panel != null)
                rootVisualElement.ProvideContext(new ScreenScaleFactor(rootVisualElement.panel.scaledPixelsPerPoint));
        }

        void OnBecameVisible()
        {
            if (rootVisualElement?.panel != null)
                rootVisualElement.ProvideContext(new ScreenScaleFactor(rootVisualElement.panel.scaledPixelsPerPoint));
        }
    }
}
