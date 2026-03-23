using System;
using Unity.AI.Generators.Contexts;
using UnityEngine.UIElements;
using Unity.AI.Generators.Redux;
using UnityEditor;

namespace Unity.AI.Generators.UIElements.Extensions
{
    static class StoreExtensions
    {
        public const string storeKey = "store";
        public const string storeApiKey = "storeApiKey";

        /// <summary>
        /// Hook into the standard UI update cycle
        ///     eg: Ensures there is a model in context and that the element is visible (attached to a panel)
        /// </summary>
        public static void UseStore(this VisualElement element, Action<Store> callback) => element.UseContext(storeKey, callback);
        public static void UseStoreApi(this VisualElement element, Action<IStoreApi> callback) => element.UseContext(storeApiKey, callback);

        /// <summary>
        /// Simplifies dispatching actions from components.
        ///
        /// Key responsibilities:
        /// - Handles finding the component's store context and dispatching an action to it.
        /// </summary>

        // Naming this `DispatchAny` as it currently seems to obfuscate all the other methods.
        //public static void DispatchAny(this IStoreApi api, object action) => api.DispatchAsync(action);
        public static void Dispatch(this VisualElement element, string type) =>
          element.GetStoreApi().Dispatch(type);
        public static void Dispatch<TPayload>(this VisualElement element, string type, TPayload payload) =>
            element.GetStoreApi().Dispatch(type, payload);
        public static void Dispatch<TAction>(this VisualElement element, TAction action) =>
            element.GetStoreApi().Dispatch(action);
        public static void Dispatch<TPayload>(this VisualElement element, StandardAction<TPayload> action, TPayload payload) =>
            element.GetStoreApi().Dispatch(action, payload);
        public static void Dispatch<TArgs>(this VisualElement element, ICreateAction<TArgs> action, TArgs args) =>
            element.GetStoreApi().Dispatch(action, args);
        public static void Dispatch<TAction, TArgs>(this VisualElement element, ICreateAction<TAction, TArgs> action, TArgs args) =>
            element.GetStoreApi().Dispatch(action, args);

        /// <summary>
        /// Get the current Store from context
        /// </summary>
        public static Store GetStore(this VisualElement element) => element.GetContext<Store>(storeKey);
        public static Store GetStore(this EditorWindow window) => window.rootVisualElement.GetStore();

        /// <summary>
        /// Get the current Store Api
        /// </summary>
        public static IStoreApi GetStoreApi(this VisualElement element) =>
            element.GetContext<IStoreApi>(storeApiKey) ?? element.GetStore();
        public static void SetStoreApi(this VisualElement element, IStoreApi api) =>
            element.ProvideContext(storeApiKey, api);
        public static void SetStoreApi(this VisualElement element, Middleware middleware) =>
            element.ProvideContext(storeApiKey, element.GetStore().CreateApi(middleware));

        /// <summary>
        /// Get the store state from an element
        /// </summary>
        public static IState GetState(this VisualElement element) =>
            element.GetStore()?.State;
    }
}
