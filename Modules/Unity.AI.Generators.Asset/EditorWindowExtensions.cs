using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Unity.AI.Generators.Contexts;
using Unity.AI.Generators.Redux;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using StoreExtensions = Unity.AI.Generators.UIElements.Extensions.StoreExtensions;

namespace Unity.AI.Generators.Asset
{
    static class EditorWindowExtensions
    {
        static Texture2D s_Icon;
        static AssetReference s_NextAssetContext = new();

        readonly static Dictionary<EditorWindow, Type> k_WindowLastAssetType = new();

        static void PutNextAssetContext(AssetReference asset)
        {
            s_NextAssetContext = asset;
        }

        static AssetReference TakeNextAssetContext()
        {
            var temp = s_NextAssetContext;
            s_NextAssetContext = new();
            return temp;
        }

        static void SetAssetContext(this EditorWindow window, AssetReference value)
        {
            if (!window.ValidateAssetContextChange(window.rootVisualElement.GetAssetContext(), value))
                return;

            if (window is IAssetEditorWindow assetEditorWindow)
                assetEditorWindow.asset = value;

            k_WindowLastAssetType[window] = AssetDatabase.GetMainAssetTypeAtPath(value.GetPath());
            window.rootVisualElement.SetAssetContext(value);
        }

        static bool ValidateAssetContextChange(this EditorWindow window, AssetReference current, AssetReference next)
        {
            if (!current.IsValid() || string.IsNullOrEmpty(current.GetPath()))
                return true;

            if (!next.IsValid() || string.IsNullOrEmpty(next.GetPath()))
                return false;

            if (!k_WindowLastAssetType.TryGetValue(window, out var lastAssetType))
                return true;

            var nextAssetType = AssetDatabase.GetMainAssetTypeAtPath(next.GetPath());

            if (lastAssetType == nextAssetType)
                return true;

            if (window is IAssetEditorWindow assetEditorWindow)
            {
                var allowedTypes = assetEditorWindow.allowedTypes;
                if (allowedTypes != null)
                {
                    foreach (var allowedType in allowedTypes)
                    {
                        if (allowedType.IsAssignableFrom(lastAssetType) && allowedType.IsAssignableFrom(nextAssetType))
                            return true;
                    }
                }
            }

            return false;
        }

        internal static Middleware AssetContextMiddleware(AssetReference value) => _ => next => async (action) =>
        {
            // Adds context to any action that warrants it.
            if (action is IContext<AssetContext> actionContext)
                actionContext.context = new(value);
            await next(action);
        };

        /// <summary>
        /// Ensure that the window uses the currently selected asset as context
        /// </summary>
        /// <param name="window">Window</param>
        public static void EnsureContext(this EditorWindow window)
        {
            var assetContext = new AssetReference();
            IStore store = null;
            if (window is IAssetEditorWindow assetEditorWindow)
            {
                assetContext = assetEditorWindow.asset;
                store = assetEditorWindow.store;
            }

            window.rootVisualElement.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            OnGeometryChanged(GeometryChangedEvent.GetPooled(window.rootVisualElement.contentRect, window.rootVisualElement.contentRect));

            var nextAssetContext = TakeNextAssetContext();
            window.rootVisualElement.ProvideContext(StoreExtensions.storeKey, (Store)store);
            var asset = nextAssetContext.IsValid() ? nextAssetContext : assetContext;
            try { window.SetAssetContext(asset); }
            catch
            {
                Debug.LogError($"Asset {asset.guid} at '{asset.GetPath()}' is not valid.");
                window.SetAssetContext(new AssetReference());
            }

            return;
            // not context related...
            void OnGeometryChanged(GeometryChangedEvent evt)
            {
                var minSize = 280f + 150f;
                if (window.rootVisualElement.Q<VisualElement>("right-section") is { } right &&
                    window.rootVisualElement.Q<VisualElement>("left-stack") is { } left)
                {
                    minSize = right.resolvedStyle.minWidth.value + left.resolvedStyle.minWidth.value;
                }
                var supportHorizontal = evt.newRect.width > minSize;
                window.rootVisualElement.EnableInClassList("horizontal-layout", supportHorizontal);
                window.rootVisualElement.EnableInClassList("vertical-layout", !supportHorizontal);
            }
        }

        /// <summary>
        /// Draw the lock context button
        /// </summary>
        /// <param name="window">Window</param>
        /// <param name="rect">Button rect</param>
        /// <returns>Button was pressed</returns>
        public static bool ShowContextLockButton(this EditorWindow window, Rect rect)
        {
            if (window is not IAssetEditorWindow assetEditorWindow)
                return false;
            EditorGUI.BeginChangeCheck();
            var lockIcon = assetEditorWindow.isLocked
                ? EditorGUIUtility.IconContent("IN LockButton on")
                : EditorGUIUtility.IconContent("IN LockButton");
            lockIcon.tooltip = assetEditorWindow.isLocked ? "Unlock the window" : "Lock the window to the current selection";
            if (GUI.Button(rect, lockIcon, GUIStyle.none))
                assetEditorWindow.isLocked = !assetEditorWindow.isLocked;
            return EditorGUI.EndChangeCheck();
        }

        /// <summary>
        /// Use the currently selected asset as context when window is IAssetEditorWindow with isLocked false
        /// </summary>
        /// <param name="window">Window</param>
        public static void OnContextChange(this EditorWindow window)
        {
            if (window is not IAssetEditorWindow assetEditorWindow)
                return;
            if (assetEditorWindow.isLocked)
                return;
            var assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            window.SetAssetContext(new AssetReference { guid = AssetDatabase.AssetPathToGUID(assetPath) });
        }

        /// <summary>
        /// Create an instance of EditorWindow with asset context
        /// </summary>
        /// <param name="asset">Initial asset context</param>
        /// <param name="title">Window title</param>
        /// <param name="useAiIcon"></param>
        /// <typeparam name="T">Window type</typeparam>
        /// <returns></returns>
        public static T CreateAssetWindow<T>(AssetReference asset, string title = null, bool useAiIcon = true) where T: EditorWindow, IAssetEditorWindow
        {
            T window = null;
            var windows = Resources.FindObjectsOfTypeAll<T>();
            if (windows.Length > 0)
                window = windows.FirstOrDefault(w => w.asset.IsValid() && w.asset.guid == asset.guid);

            if (window == null)
            {
                PutNextAssetContext(asset);

                // CreateInstance respects ShowUtility and ShowPopup, CreateWindow does not
                window = ScriptableObject.CreateInstance<T>();
                window.rootVisualElement.RegisterCallback<DetachFromPanelEvent>(_ => ((Store)window.store)?.Dispose());
            }
            else
            {
                window.SetAssetContext(asset);
            }

            if (!s_Icon)
                s_Icon = EditorGUIUtility.FindTexture("AISparkle Icon");

            window.titleContent = useAiIcon ? new GUIContent(title, s_Icon) : new GUIContent(title);

            return window;
        }

        /// <summary>
        /// Create an instance of EditorWindow
        /// </summary>
        /// <param name="store">Store context</param>
        /// <param name="title">Window title</param>
        /// <param name="storeOwner">Is this window the store owner</param>
        /// <param name="useAiIcon"></param>
        /// <typeparam name="T">Window type</typeparam>
        /// <returns></returns>
        public static T CreateWindow<T>(IStore store, string title = null, bool storeOwner = true, bool useAiIcon = true) where T: EditorWindow
        {
            T window = null;
            var windows = Resources.FindObjectsOfTypeAll<T>();
            if (windows.Length > 0)
                window = windows[0];

            if (window == null)
            {
                // CreateInstance respects ShowUtility and ShowPopup, CreateWindow does not
                window = ScriptableObject.CreateInstance<T>();
                if (storeOwner)
                    window.rootVisualElement.RegisterCallback<DetachFromPanelEvent>(_ => ((Store)store)?.Dispose());
            }
            if (store != null)
                window.rootVisualElement.ProvideContext(StoreExtensions.storeKey, store);
            if (useAiIcon && !s_Icon)
                s_Icon = EditorGUIUtility.FindTexture("AISparkle Icon");
            window.titleContent = new GUIContent(title, s_Icon);

            return window;
        }

        /// <summary>
        /// Create an instance of EditorWindow
        /// </summary>
        /// <param name="store">Store context</param>
        /// <param name="asset">Initial asset context</param>
        /// <param name="title">Window title</param>
        /// <param name="storeOwner">Is this window the store owner</param>
        /// <param name="useAiIcon"></param>
        /// <typeparam name="T">Window type</typeparam>
        /// <returns></returns>
        public static T CreateWindow<T>(IStore store, AssetReference asset, string title = null, bool storeOwner = true, bool useAiIcon = true) where T: EditorWindow
        {
            T window = null;
            var windows = Resources.FindObjectsOfTypeAll<T>();
            if (windows.Length > 0)
                window = windows[0];

            if (window == null)
            {
                // CreateInstance respects ShowUtility and ShowPopup, CreateWindow does not
                window = ScriptableObject.CreateInstance<T>();
                if (storeOwner)
                    window.rootVisualElement.RegisterCallback<DetachFromPanelEvent>(_ => ((Store)store)?.Dispose());
            }
            window.rootVisualElement.ProvideContext(StoreExtensions.storeKey, store);
            if (useAiIcon && !s_Icon)
                s_Icon = EditorGUIUtility.FindTexture("AISparkle Icon");
            window.titleContent = new GUIContent(title, s_Icon);
            window.SetAssetContext(asset);

            return window;
        }

        public static bool TryClose(this EditorWindow window)
        {
            if (window.hasUnsavedChanges)
            {
                var r = EditorUtility.DisplayDialogComplex("Unsaved Changes", window.saveChangesMessage,
                    "Save", "Cancel", "Discard");

                switch (r)
                {
                    case 0:
                        window.SaveChanges();
                        window.Close();
                        break;
                    case 1:
                        return false;
                    case 2:
                        window.DiscardChanges();
                        window.Close();
                        break;
                }
            }
            else
            {
                window.Close();
            }

            return true;
        }
    }
}
