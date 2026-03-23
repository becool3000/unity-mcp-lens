using System.Collections.Generic;
using System.Linq;
using Unity.AI.Image.Components;
using Unity.AI.Image.Services.SessionPersistence;
using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.Actions.Payloads;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Image.Services.Undo;
using Unity.AI.Image.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Contexts;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.ShortcutManagement;

namespace Unity.AI.Image.Windows
{
    class DoodleWindow : EditorWindow
    {
        static Store store => SharedStore.Store;

        /// <summary>
        /// Open a new Doodle window with the given args.
        /// </summary>
        /// <param name="args"> The arguments to open the window with. </param>
        /// <returns> The opened Doodle window (if successful). </returns>
        public static DoodleWindow Open(DoodleWindowArgs args)
        {
            var window = Resources.FindObjectsOfTypeAll<DoodleWindow>().FirstOrDefault();
            if (window)
            {
                var windowAsset = store.State.SelectAssetReference();
                var windowImageReferenceType = store.State.SelectImageReferenceType();
                var differentContext = windowAsset != args.asset || windowImageReferenceType != args.imageReferenceType;
                if (differentContext)
                {
                    if (!window.TryClose())
                    {
                        Debug.LogWarning("Aborting opening of new Doodle window because the current one has unsaved changes.");
                        return null;
                    }
                }
                else
                {
                    window.Focus();
                    return window;
                }
            }

            // update store with args
            store.Dispatch(DoodleWindowActions.setImageReferenceType, args.imageReferenceType);
            store.Dispatch(DoodleWindowActions.setAssetReference, args.asset);
            store.Dispatch(DoodleWindowActions.setSize, args.size);
            store.Dispatch(DoodleWindowActions.setLayer, (0, args.data));
            store.Dispatch(DoodleWindowActions.setShowBaseImage, args.showBaseImage);

            window = CreateInstance<DoodleWindow>();
            window.titleContent = new GUIContent("Doodle");
            window.saveChangesMessage = "The Doodle has unsaved changes. Do you want to save and apply them?";
            window.hasUnsavedChanges = false;
            window.minSize = new Vector2(256, 256);
            window.maxSize = new Vector2(4096, 2160);
            window.ShowUtility();
            return window;
        }

        /// <summary>
        /// Find any open Doodle window with the given asset and type.
        /// </summary>
        /// <param name="asset"> The asset to search for. </param>
        /// <param name="type"> The type of the asset to search for. </param>
        /// <returns> The found Doodle window (if successful). </returns>
        public static DoodleWindow GetWindow(AssetReference asset, ImageReferenceType type)
        {
            var window = Resources.FindObjectsOfTypeAll<DoodleWindow>().FirstOrDefault();
            if (window)
            {
                var windowAsset = store.State.SelectAssetReference();
                var windowImageReferenceType = store.State.SelectImageReferenceType();
                return windowAsset == asset && windowImageReferenceType == type ? window : null;
            }

            return null;
        }

        /// <summary>
        /// Request to reset the data of the Doodle for this window.
        /// </summary>
        /// <param name="layers"> The new data to reset the Doodle with. </param>
        /// <returns> True if the data was reset, false otherwise. </returns>
        public bool ResetData(IEnumerable<DoodleLayer> layers)
        {
            if (hasUnsavedChanges)
            {
                var r = EditorUtility.DisplayDialog("Unsaved Changes",
                    "You have unsaved changes. Do you want to discard them?", "Yes", "No");
                if (!r)
                    return false;
            }

            store.Dispatch(DoodleWindowActions.setLayers, layers);
            hasUnsavedChanges = false;
            // We only call ResetData just before closing the window
            // (when clicking the "clear" button of an image reference field),
            // so technically we could avoid clearing the history here.
            // But from a public API perspective, it makes sense to clear it.
            DoodleWindowHistory.instance.Clear();
            DoodleWindowHistory.instance.Push(store.State.SelectState());
            return true;
        }

        void OnDisable( )
        {
            DoodleWindowHistory.instance.StateChanged -= OnHistoryStateChanged;
            DoodleWindowHistory.instance.Clear();
            store.Dispatch(DoodleWindowActions.setAssetReference, null);
        }

        void OnHistoryStateChanged(DoodleWindowHistoryItem item)
        {
            store.Dispatch(DoodleWindowActions.setLayers, item.layers);
            CheckDirty();
        }

        void CheckDirty()
        {
            // check if the new state of the doodle is similar to the one in input field
            var state = store.State;
            var data = state.SelectMergedDoodleLayers();
            var type = state.SelectImageReferenceType();
            var refData = rootVisualElement.GetState().SelectGenerationSetting(rootVisualElement).SelectEditedDoodle(type);
            var sameData = data == refData || (refData != null && data != null && data.SequenceEqual(refData));
            hasUnsavedChanges = !sameData;
        }

        void CreateGUI()
        {
            rootVisualElement.ProvideContext(StoreExtensions.storeKey, store);
            rootVisualElement.SetAssetContext(store.State.SelectAssetReference());
            var view = new DoodleView();
            view.saveRequested += SaveChanges;
            rootVisualElement.Add(view);
            hasUnsavedChanges = false;
            rootVisualElement.Q<DoodlePad>().RegisterValueChangedCallback(_ => hasUnsavedChanges = true);
            DoodleWindowHistory.instance.Push(store.State.SelectState());
            DoodleWindowHistory.instance.StateChanged += OnHistoryStateChanged;
        }

        /// <inheritdoc />
        public override void SaveChanges()
        {
            var state = store.State;
            var doodle = state.SelectMergedDoodleLayers();
            var type = state.SelectImageReferenceType();
            rootVisualElement.Dispatch(GenerationSettingsActions.applyEditedImageReferenceDoodle, (type, doodle));

            base.SaveChanges();
        }

        /// <inheritdoc />
        public override void DiscardChanges()
        {
            // nothing to do, we do not need to clean up the store
            base.DiscardChanges();
        }

        [Shortcut("DoodleWindow/File: Save", typeof(DoodleWindow), KeyCode.S, ShortcutModifiers.Action)]
        static void Save(ShortcutArguments args)
        {
            var window = args.context as DoodleWindow;
            if (window)
                window.SaveChanges();
        }

        [Shortcut("DoodleWindow/File: Close Tab", typeof(DoodleWindow), KeyCode.W, ShortcutModifiers.Action)]
        static void CloseTab(ShortcutArguments args)
        {
            var window = args.context as DoodleWindow;
            if (window)
                window.TryClose();
        }

        [Shortcut("DoodleWindow/Tools: Clear Doodle", typeof(DoodleWindow), KeyCode.Backspace, ShortcutModifiers.Action)]
        [Shortcut("DoodleWindow/Tools: Clear Doodle-Windows", typeof(DoodleWindow), KeyCode.Delete)]
        static void ClearDoodle(ShortcutArguments args)
        {
            var window = args.context as DoodleWindow;
            if (window) // a commanding system would be better instead of searching for the view
                window.rootVisualElement.Q<DoodleView>()?.ClearDoodle();
        }

        [Shortcut("DoodleWindow/Tools: Fill Doodle", typeof(DoodleWindow), KeyCode.Backspace, ShortcutModifiers.Alt)]
        static void FillDoodle(ShortcutArguments args)
        {
            var window = args.context as DoodleWindow;
            if (window) // a commanding system would be better instead of searching for the view
                window.rootVisualElement.Q<DoodleView>()?.FillDoodle();
        }

        [Shortcut("DoodleWindow/Tools: Select Brush", typeof(DoodleWindow), KeyCode.B)]
        static void SelectBrush(ShortcutArguments args)
        {
            var window = args.context as DoodleWindow;
            if (window) // a commanding system would be better instead of searching for the view
                window.rootVisualElement.Q<DoodleView>()?.SelectBrush();
        }

        [Shortcut("DoodleWindow/Tools: Select Eraser", typeof(DoodleWindow), KeyCode.E)]
        static void SelectEraser(ShortcutArguments args)
        {
            var window = args.context as DoodleWindow;
            if (window) // a commanding system would be better instead of searching for the view
                window.rootVisualElement.Q<DoodleView>()?.SelectEraser();
        }

        [Shortcut("DoodleWindow/Tools: Select Fill", typeof(DoodleWindow), KeyCode.F)]
        static void SelectFill(ShortcutArguments args)
        {
            var window = args.context as DoodleWindow;
            if (window) // a commanding system would be better instead of searching for the view
                window.rootVisualElement.Q<DoodleView>()?.SelectFill();
        }

        [Shortcut("DoodleWindow/Tools: Select Move", typeof(DoodleWindow), KeyCode.V)]
        static void SelectMove(ShortcutArguments args)
        {
            var window = args.context as DoodleWindow;
            if (window) // a commanding system would be better instead of searching for the view
                window.rootVisualElement.Q<DoodleView>()?.SelectMove();
        }

        [Shortcut("DoodleWindow/Tools: Decrease Brush Size", typeof(DoodleWindow), KeyCode.LeftBracket)]
        static void DecreaseBrushSize(ShortcutArguments args)
        {
            var window = args.context as DoodleWindow;
            if (window) // a commanding system would be better instead of searching for the view
                window.rootVisualElement.Q<DoodleView>()?.DecreaseBrushSize();
        }

        [Shortcut("DoodleWindow/Tools: Increase Brush Size", typeof(DoodleWindow), KeyCode.RightBracket)]
        static void IncreaseBrushSize(ShortcutArguments args)
        {
            var window = args.context as DoodleWindow;
            if (window) // a commanding system would be better instead of searching for the view
                window.rootVisualElement.Q<DoodleView>()?.IncreaseBrushSize();
        }

        [Shortcut("DoodleWindow/Tools: Toggle Show Base Image", typeof(DoodleWindow), KeyCode.I)]
        static void ToggleShowBaseImage(ShortcutArguments args)
        {
            var window = args.context as DoodleWindow;
            if (window) // a commanding system would be better instead of searching for the view
                window.rootVisualElement.Q<DoodleView>()?.ToggleShowBaseImage();
        }
    }
}
