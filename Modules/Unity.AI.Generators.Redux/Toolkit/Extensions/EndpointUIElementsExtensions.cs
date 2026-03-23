using System;
using System.Threading.Tasks;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.Redux.Toolkit
{
    /// <summary>
    /// Matches the `EndpointOperationExtensions` API but with `VisualElement`
    /// </summary>
    static class EndpointUIElementsExtensions
    {
        /// <summary>
        /// Internal base method to funnel al VisualElement `UseResult` apis
        /// </summary>
        static Unsubscribe UseResult<TResult>(this VisualElement element, EndpointOperation<TResult> operation, Task<UseEndpointResult<TResult>> operationTask) =>
            element.OnLive(async () =>
            {
                operation.EnsureCacheItem();
                return (await operationTask).unsubscribe;
            });

        /// <summary>
        /// Plugs in to a visual element's lifecycle.
        /// </summary>
        public static Unsubscribe UseResult<TResult>(this VisualElement element, EndpointOperation<TResult> operation, Action<EndpointResult<TResult>> callback) =>
            element.UseResult(operation, operation.UseResult(callback));
        public static Unsubscribe UseResult<TResult>(this VisualElement element, EndpointOperation<TResult> operation, Func<EndpointResult<TResult>, bool> filter, Action<EndpointResult<TResult>> callback) =>
            element.UseResult(operation, operation.UseResult(filter, callback));
        public static Unsubscribe UseDone<TResult>(this VisualElement element, EndpointOperation<TResult> operation, Action<EndpointResult<TResult>> callback) =>
            element.UseResult(operation, operation.UseDone(callback));

        /// <summary>
        /// Only get notified on successful result.
        /// </summary>
        public static Unsubscribe Use<TResult>(this VisualElement element, EndpointOperation<TResult> operation, Action<TResult> callback) =>
            element.UseResult(operation, operation.Use(callback));
    }
}
