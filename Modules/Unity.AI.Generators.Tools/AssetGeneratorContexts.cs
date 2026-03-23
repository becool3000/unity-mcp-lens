using System;
using System.Collections.Generic;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Contexts;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Actions;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Image.Services.Contexts;
using Unity.AI.Image.Services.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

using AnimateStore = Unity.AI.Animate.Services.SessionPersistence.SharedStore;
using ImageStore = Unity.AI.Image.Services.SessionPersistence.SharedStore;
using MaterialStore = Unity.AI.Pbr.Services.SessionPersistence.SharedStore;
using MeshStore = Unity.AI.Mesh.Services.SessionPersistence.SharedStore;
using SoundStore = Unity.AI.Sound.Services.SessionPersistence.SharedStore;

namespace Unity.AI.Generators.Tools
{
    /// <summary>
    /// Provides extension methods for setting up asset generator store contexts on VisualElements.
    /// </summary>
    static partial class AssetGenerators
    {
        static void SetAssetContext(VisualElement ve, Object obj)
        {
            var assetPath = AssetDatabase.GetAssetPath(obj);
            var asset = new AssetReference { guid = AssetDatabase.AssetPathToGUID(assetPath) };
            ve.SetAssetContext(asset);
            ve.ProvideContext(asset.IsCubemap() ? new PreviewScaleFactor(2) : new PreviewScaleFactor(1.25f));
            ve.ProvideContext(new WindowSettingsContext(replaceAssetOnSelect: true, disablePrecaching: true));
        }

        static void OnToast(this VisualElement ve, IEnumerable<GenerationFeedbackData> messages)
        {
            foreach (var feedback in messages)
            {
                ve.ShowToast(feedback.message);
                ve.Dispatch(GenerationActions.removeGenerationFeedback, ve.GetAsset());
            }
        }

        /// <summary>
        /// Sets the SoundStore context and asset context for the given VisualElement.
        /// </summary>
        /// <param name="ve">The VisualElement to provide context for.</param>
        /// <param name="obj">The Unity Object representing the asset.</param>
        public static void SetSoundContext(this VisualElement ve, Object obj)
        {
            ve.ProvideContext(StoreExtensions.storeKey, SoundStore.Store);
            SetAssetContext(ve, obj);
            ve.ProvideContext(new PreviewScaleFactor(1));
            ve.UseArray(state => Sound.Services.Stores.Selectors.Selectors.SelectGenerationFeedback(state, ve), ve.OnToast);
        }

        /// <summary>
        /// Sets the AnimateStore context and asset context for the given VisualElement.
        /// </summary>
        /// <param name="ve">The VisualElement to provide context for.</param>
        /// <param name="obj">The Unity Object representing the asset.</param>
        public static void SetAnimateContext(this VisualElement ve, Object obj)
        {
            ve.ProvideContext(StoreExtensions.storeKey, AnimateStore.Store);
            SetAssetContext(ve, obj);
            ve.UseArray(state => Animate.Services.Stores.Selectors.Selectors.SelectGenerationFeedback(state, ve), ve.OnToast);
        }

        /// <summary>
        /// Sets the ImageStore context and asset context for the given VisualElement.
        /// Also provides an ExternalDoodleEditor context.
        /// </summary>
        /// <param name="ve">The VisualElement to provide context for.</param>
        /// <param name="obj">The Unity Object representing the asset.</param>
        public static void SetImageContext(this VisualElement ve, Object obj)
        {
            ve.ProvideContext(StoreExtensions.storeKey, ImageStore.Store);
            ve.ProvideContext(ExternalDoodleEditor.key, new ExternalDoodleEditor(true));
            SetAssetContext(ve, obj);
            ve.UseArray(state => Image.Services.Stores.Selectors.Selectors.SelectGenerationFeedback(state, ve), ve.OnToast);
        }

        /// <summary>
        /// Sets the MaterialStore context and asset context for the given VisualElement.
        /// </summary>
        /// <param name="ve">The VisualElement to provide context for.</param>
        /// <param name="obj">The Unity Object representing the asset.</param>
        public static void SetMaterialContext(this VisualElement ve, Object obj)
        {
            ve.ProvideContext(StoreExtensions.storeKey, MaterialStore.Store);
            SetAssetContext(ve, obj);
            ve.UseArray(state => Pbr.Services.Stores.Selectors.Selectors.SelectGenerationFeedback(state, ve), ve.OnToast);
        }

        /// <summary>
        /// Sets the MeshStore context and asset context for the given VisualElement.
        /// </summary>
        /// <param name="ve">The VisualElement to provide context for.</param>
        /// <param name="obj">The Unity Object representing the asset.</param>
        public static void SetMeshContext(this VisualElement ve, Object obj)
        {
            ve.ProvideContext(StoreExtensions.storeKey, MeshStore.Store);
            SetAssetContext(ve, obj);
            ve.UseArray(state => Mesh.Services.Stores.Selectors.Selectors.SelectGenerationFeedback(state, ve), ve.OnToast);
        }
    }
}
